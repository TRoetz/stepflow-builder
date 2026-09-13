# MarkProcessed - record the file in .processed.json and move it to C:\temp\ai-exec\processed\.
# Input: { file, import: {...}, ai: { status, answer, ... } }
$file = [string]$input_data.file
$dir = "C:\temp\ai-exec"

$marker = Join-Path $dir ".processed.json"
$list = @()
if (Test-Path $marker) {
    try {
        $m = Get-Content $marker -Raw | ConvertFrom-Json
        if ($m) { $list = @($m) }
    } catch {}
}

$aiStatus = "unknown"
$summary = ""
if ($input_data.ai) {
    if ($input_data.ai.status) { $aiStatus = [string]$input_data.ai.status }
    if ($input_data.ai.answer) {
        $s = [string]$input_data.ai.answer
        if ($s.Length -gt 2000) { $s = $s.Substring(0, 2000) + " ...[truncated]" }
        $summary = $s
    }
}

$list += [pscustomobject]@{
    file        = $file
    processedAt = (Get-Date).ToString("o")
    aiStatus    = $aiStatus
    summary     = $summary
}
$list | ConvertTo-Json -Depth 5 | Set-Content -Path $marker -Encoding UTF8

$moved = $false
if ($file -and (Test-Path $file)) {
    $procDir = Join-Path $dir "processed"
    if (-not (Test-Path $procDir)) { New-Item -ItemType Directory -Path $procDir -Force | Out-Null }
    Move-Item -Path $file -Destination (Join-Path $procDir (Split-Path $file -Leaf)) -Force
    $moved = $true
}

return [pscustomobject]@{ marked = $file; movedToProcessed = $moved; totalProcessed = $list.Count }
