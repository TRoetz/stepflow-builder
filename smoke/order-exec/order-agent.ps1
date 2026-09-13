# AskAi - autonomous tool-calling agent that generates an invoice for a validated order.
# Input: { orderId, customerName, lines[{productId,name,price,qty,lineTotal}], total, file }
# Emits (merged at $.ai): { status, iterations, answer, toolLog }
# Throws on unrecoverable failure -> States.TaskFailed -> HandleFailure (no stock side effects).
$ErrorActionPreference = "Stop"

$model = "Ternary-Bonsai-27B-Q2_0.gguf"
$apiBase = "http://192.168.10.175:8080/v1"
$apiUrl = $apiBase + "/chat/completions"
$dbq = Join-Path "C:\temp\order-exec" "dbq.py"

function Trunc([string]$s) {
    if ($null -eq $s) { return "" }
    if ($s.Length -gt 1500) { return $s.Substring(0, 1500) + " ...[truncated]" }
    return $s
}

function Log([string]$msg) { [Console]::Error.WriteLine("[AskAi] " + $msg) }

# Wait until the AI server is healthy again: /v1/models lists a model AND a minimal completion returns choices.
# The local llama.cpp server intermittently 500s / answers empty under sustained load and recovers after a reload.
function Wait-ServerHealthy() {
    for ($w = 0; $w -lt 18; $w++) {   # ~3 minute budget: probe + 10s sleep per round
        try {
            $m = Invoke-RestMethod -Uri ($apiBase + "/models") -TimeoutSec 5 -ErrorAction Stop
            if (@($m.data).Count -gt 0) {
                $miniBody = '{"model":"' + $model + '","messages":[{"role":"user","content":"Say OK"}],"max_tokens":8,"temperature":0}'
                $mini = Invoke-RestMethod -Uri $apiUrl -Method Post -ContentType "application/json" -Body $miniBody -TimeoutSec 60 -ErrorAction Stop
                if ($mini.choices -and @($mini.choices).Count -gt 0) { return $true }
            }
        } catch {}
        Start-Sleep -Seconds 10
    }
    return $false
}

function Invoke-LocalTool([string]$name, [string]$argsJson) {
    if ($name -eq "query_db") {
        try {
            $q = (ConvertFrom-Json $argsJson).sql
            $out = & python "$dbq" $q 2>&1 | Out-String
            return ("rows: " + $out.Trim())
        } catch {
            return ('{"error":"query_db failed: ' + $_.Exception.Message.Replace('"', '\"') + '"}')
        }
    }
    if ($name -eq "write_file") {
        try {
            $p = (ConvertFrom-Json $argsJson).path
            $c = (ConvertFrom-Json $argsJson).content
            $dir = Split-Path $p -Parent
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
            Set-Content -Path $p -Value $c -Encoding UTF8
            return ('{"ok":true,"path":"' + $p.Replace('"', '\"') + '"}')
        } catch {
            return ('{"error":"write_file failed: ' + $_.Exception.Message.Replace('"', '\"') + '"}')
        }
    }
    return ('{"error":"unknown tool: ' + $name.Replace('"', '\"') + '"}')
}

$orderId = [string]$input_data.orderId
$orderInfo = [pscustomobject]@{
    orderId = $orderId
    customerName = [string]$input_data.customerName
    lines = @($input_data.lines)
    total = [double]$input_data.total
    dateUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
}

$systemPrompt = @"
You are an order fulfillment assistant for a coffee equipment shop. You receive one validated customer order as JSON with fields: orderId, customerName, lines (array of {productId, name, price, qty, lineTotal}), total, dateUtc.
Your job:
1) Use query_db to look up full details for each ordered product from the products table (columns: id, name, price, stock, category, description).
2) Use query_db to find complementary products that pair well with what was purchased and are still in stock: SELECT DISTINCT p.id, p.name, p.price, p.description FROM complements c JOIN products p ON p.id = c.complement_id WHERE c.product_id IN (<ordered product ids>) AND p.stock > 0. Only recommend products not already part of this order.
3) Use write_file to create the invoice at C:\temp\order-exec\output\invoice-<orderId>.txt (substitute the actual orderId). The file must contain: a header with the word INVOICE, the orderId, customer name and the dateUtc value from the order JSON (do not invent any other date); an itemized list of each line as "name - qty x unit price = line total" plus a grand total in USD; a short friendly message about the purchased product(s) tailored to this order based on their descriptions; and a section titled "You might also like" listing 2-4 complementary products, each with a one-line reason it pairs well.
4) Reply with a brief plain-text summary: the invoice file path, the grand total, and which complementary products you recommended.
"@

$userMessage = "Order to process:`n" + ($orderInfo | ConvertTo-Json -Depth 10)

$tools = @(
    [pscustomobject]@{ type="function"; function=[pscustomobject]@{ name="query_db"; description="Run a read-only SQL query against the shop database (tables: products(id,name,price,stock,category,description), complements(product_id,complement_id)). Returns rows as text."; parameters=[pscustomobject]@{ type="object"; properties=[pscustomobject]@{ sql=[pscustomobject]@{ type="string"; description="The SQL SELECT statement" } }; required=@("sql") } } },
    [pscustomobject]@{ type="function"; function=[pscustomobject]@{ name="write_file"; description="Write text content to a file path (creates parent directories). Use for the invoice."; parameters=[pscustomobject]@{ type="object"; properties=[pscustomobject]@{ path=[pscustomobject]@{ type="string" }; content=[pscustomobject]@{ type="string" } }; required=@("path","content") } } },
    [pscustomobject]@{ type="function"; function=[pscustomobject]@{ name="verify_output_folder"; description="List the contents of C:\temp\order-exec\output to confirm files were written."; parameters=[pscustomobject]@{ type="object"; properties=@{}; required=@() } } }
)

$messages = @(
    [pscustomobject]@{ role="system"; content=$systemPrompt },
    [pscustomobject]@{ role="user"; content=$userMessage }
)

$toolLog = @()
$maxIter = 15
$maxAttempts = 3
$finalAnswer = ""
$i = 0

for ($i = 0; $i -lt $maxIter; $i++) {
    # Per-iteration retry: on API failure wait for the server to recover, then resend the same request.
    $resp = $null
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        try {
            $body = [pscustomobject]@{ model=$model; messages=@($messages); tools=@($tools); tool_choice="auto"; max_tokens=4096; temperature=0.3 }
            $resp = Invoke-RestMethod -Uri $apiUrl -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 20) -TimeoutSec 900
            if (-not $resp.choices -or @($resp.choices).Count -eq 0) {
                throw ("AI returned no choices: " + (Trunc (($resp | ConvertTo-Json -Depth 5))))
            }
            break
        } catch {
            if ($attempt -ge $maxAttempts) {
                throw ("execution failed at iteration " + ($i + 1) + " after " + $maxAttempts + " attempts: " + $_.Exception.Message)
            }
            Log ("iteration " + ($i+1) + " attempt " + $attempt + " failed: " + $_.Exception.Message + "; waiting for server recovery")
            Start-Sleep -Seconds 20
            if (-not (Wait-ServerHealthy)) { Log "server did not report healthy within budget; retrying anyway" }
        }
    }

    $choice = $resp.choices[0]
    $msg = $choice.message

    # Hashtable, not PSCustomObject: tool_calls is added conditionally and PS fixed objects reject new properties.
    $assistantMsg = @{ role="assistant"; content="" }
    if ($null -ne $msg.content) { $assistantMsg["content"] = [string]$msg.content }
    if ($msg.tool_calls) {
        $tcs = @()
        foreach ($tc in @($msg.tool_calls)) {
            $argsStr = "{}"
            if ($tc.function.arguments) { $argsStr = [string]$tc.function.arguments }
            $tcs += [pscustomobject]@{ id=[string]$tc.id; type="function"; function=[pscustomobject]@{ name=[string]$tc.function.name; arguments=$argsStr } }
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
            $toolLog += [pscustomobject]@{ iteration=($i+1); tool=$tname; arguments=(Trunc $argsStr); result=(Trunc $tresult) }
            $messages += [pscustomobject]@{ role="tool"; tool_call_id=[string]$tc.id; content=$tresult }
        }
    } else {
        if ($msg.content) { $finalAnswer = [string]$msg.content }
        if ($choice.finish_reason -eq "length") { Log ("warning: final answer truncated at iteration " + ($i+1)) }
        break
    }
}

if ($i -ge $maxIter) { throw ("iteration limit reached after " + $maxIter + " iterations without a final answer") }

$status = "completed"
if ($resp.choices[0].finish_reason -eq "length") { $status = "truncated" }

return [pscustomobject]@{ status=$status; iterations=($i+1); answer=(Trunc $finalAnswer); toolLog=@($toolLog) }
