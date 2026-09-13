# AskAi - autonomous tool-calling loop against a local OpenAI-compatible endpoint.
# Input: { file, import: { enrichedRows: [ {Task, Description}, ... ] } }
# Emits: { status, iterations, answer, toolLog }
$ErrorActionPreference = "Stop"

$model  = "Terron-Bonsai-27B-Q2_0.gguf"
$apiUrl = "http://192.168.10.175:8080/v1/chat/completions"

function Trunc([string]$s) {
    if ($null -eq $s) { return "" }
    if ($s.Length -gt 4000) { return $s.Substring(0, 4000) + " ...[truncated]" }
    return $s
}

function Invoke-LocalTool([string]$name, [string]$argsJson) {
    try {
        $a = if ($argsJson -and $argsJson.Trim()) { $argsJson | ConvertFrom-Json } else { $null }
        switch ($name) {
            "run_command" {
                if (-not $a.command) { throw "run_command requires 'command'" }
                $tmp = Join-Path $env:TEMP ("sf_ai_" + [guid]::NewGuid().ToString("N") + ".ps1")
                Set-Content -Path $tmp -Value ([string]$a.command) -Encoding UTF8
                $outFile = "$tmp.out"
                $errFile = "$tmp.err"
                $p = Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", $tmp) -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru -WindowStyle Hidden
                $so = ""; $se = ""
                if (Test-Path $outFile) { $so = Get-Content $outFile -Raw }
                if (Test-Path $errFile) { $se = Get-Content $errFile -Raw }
                return ([pscustomobject]@{ exitCode = $p.ExitCode; stdout = (Trunc $so); stderr = (Trunc $se) } | ConvertTo-Json -Depth 5 -Compress)
            }
            "read_file" {
                if (-not $a.path) { throw "read_file requires 'path'" }
                if (-not (Test-Path ([string]$a.path))) { return ([pscustomobject]@{ error = ("file not found: " + [string]$a.path) } | ConvertTo-Json -Depth 5 -Compress) }
                $c = Get-Content ([string]$a.path) -Raw
                return (Trunc $c)
            }
            "write_file" {
                if (-not $a.path) { throw "write_file requires 'path'" }
                $p2 = [string]$a.path
                $parent = Split-Path $p2 -Parent
                if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                Set-Content -Path $p2 -Value ([string]$a.content) -Encoding UTF8
                return ([pscustomobject]@{ ok = $true; path = $p2; bytes = (Get-Item $p2).Length } | ConvertTo-Json -Depth 5 -Compress)
            }
            default { throw ("unknown tool: " + $name) }
        }
    } catch {
        return ([pscustomobject]@{ error = ($_.Exception.Message) } | ConvertTo-Json -Depth 5 -Compress)
    }
}

# Tasks come from the DataExchange import result.
$tasks = @()
if ($input_data.import -and $input_data.import.enrichedRows) { $tasks = @($input_data.import.enrichedRows) }
if ($tasks.Count -eq 0) { return [pscustomobject]@{ status = "skipped"; reason = "no enriched rows in import result" } }

$systemPrompt = @"
You are an autonomous task-execution agent running on a Windows machine. You will be given a list of tasks, each with a Task and Description. Execute every task using your tools: run_command executes any PowerShell command (services, scripts, system operations); read_file reads a file's content; write_file creates or overwrites a file. Work through the tasks in order. After each tool call, inspect its result; if something fails, try to fix it or record the failure and continue with the remaining tasks. Make reasonable decisions yourself - never ask questions. When all tasks are complete (or you have exhausted reasonable attempts), reply with a final plain-text summary listing what was executed for each task and any failures.
"@

$messages = @()
$messages += [pscustomobject]@{ role = "system"; content = $systemPrompt }
$messages += [pscustomobject]@{ role = "user"; content = ("Tasks to execute (JSON):" + [Environment]::NewLine + ($tasks | ConvertTo-Json -Depth 5)) }

$tools = @(
    [pscustomobject]@{ type = "function"; function = [pscustomobject]@{ name = "run_command"; description = "Execute a PowerShell command on this Windows machine. Returns exitCode, stdout and stderr."; parameters = [pscustomobject]@{ type = "object"; properties = @{ command = [pscustomobject]@{ type = "string"; description = "PowerShell command line to execute" } }; required = @("command") } } },
    [pscustomobject]@{ type = "function"; function = [pscustomobject]@{ name = "read_file"; description = "Read the contents of a file from this machine."; parameters = [pscustomobject]@{ type = "object"; properties = @{ path = [pscustomobject]@{ type = "string"; description = "Absolute file path" } }; required = @("path") } } },
    [pscustomobject]@{ type = "function"; function = [pscustomobject]@{ name = "write_file"; description = "Create or overwrite a file with the given content. Parent directories are created automatically."; parameters = [pscustomobject]@{ type = "object"; properties = @{ path = [pscustomobject]@{ type = "string" }; content = [pscustomobject]@{ type = "string" } }; required = @("path", "content") } } }
)

$toolLog = @()
$maxIter = 15
$finalAnswer = ""
$status = "completed"

try {
    for ($i = 0; $i -lt $maxIter; $i++) {
        $body = [pscustomobject]@{ model = $model; messages = @($messages); tools = @($tools); tool_choice = "auto"; max_tokens = 8192; temperature = 0.3 }
        $resp = Invoke-RestMethod -Uri $apiUrl -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 20) -TimeoutSec 900

        if (-not $resp.choices -or @($resp.choices).Count -eq 0) {
            throw ("AI returned no choices: " + (Trunc (($resp | ConvertTo-Json -Depth 5))))
        }
        $choice = $resp.choices[0]
        $msg = $choice.message

        # Hashtable, not PSCustomObject: tool_calls is added conditionally and PS fixed objects reject new properties.
        $assistantMsg = @{ role = "assistant"; content = "" }
        if ($null -ne $msg.content) { $assistantMsg["content"] = [string]$msg.content }
        if ($msg.tool_calls) {
            $tcs = @()
            foreach ($tc in @($msg.tool_calls)) {
                $argsStr = "{}"
                if ($tc.function.arguments) { $argsStr = [string]$tc.function.arguments }
                $tcs += [pscustomobject]@{ id = [string]$tc.id; type = "function"; function = [pscustomobject]@{ name = [string]$tc.function.name; arguments = $argsStr } }
            }
            $assistantMsg["tool_calls"] = @($tcs)
        }
        $messages += $assistantMsg

        if ($choice.finish_reason -eq "tool_calls" -and $msg.tool_calls) {
            foreach ($tc in @($msg.tool_calls)) {
                $tname = [string]$tc.function.name
                $argsStr = "{}"
                if ($tc.function.arguments) { $argsStr = [string]$tc.function.arguments }
                $tresult = Invoke-LocalTool $tname $argsStr
                $toolLog += [pscustomobject]@{ iteration = ($i + 1); tool = $tname; arguments = (Trunc $argsStr); result = (Trunc $tresult) }
                $messages += [pscustomobject]@{ role = "tool"; tool_call_id = [string]$tc.id; content = $tresult }
            }
        } else {
            if ($msg.content) { $finalAnswer = [string]$msg.content }
            if ($choice.finish_reason -eq "length") { $status = "truncated" }
            break
        }
    }
    if ($i -ge $maxIter) { $status = "iteration_limit" }
} catch {
    $status = "error"
    $finalAnswer = ("execution failed at iteration " + ($i + 1) + ": " + $_.Exception.Message)
}

return [pscustomobject]@{ status = $status; iterations = ($i + 1); answer = (Trunc $finalAnswer); toolLog = @($toolLog) }
