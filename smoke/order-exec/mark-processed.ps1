# MarkProcessed - record the processed order and move its CSV to processed\.
# Input: { file, orderId, ..., ai: { status, answer } }
$ErrorActionPreference = "Stop"

$file = [string]$input_data.file
$dir = "C:\temp\order-exec"

$status = "unknown"
if ($input_data.ai -and $input_data.ai.status) { $status = [string]$input_data.ai.status }
$summary = ""
if ($input_data.ai -and $input_data.ai.answer) { $summary = [string]$input_data.ai.answer }
if ($summary.Length -gt 2000) { $summary = $summary.Substring(0, 2000) + " ...[truncated]" }

# Move the CSV out of the drop folder first so a marker failure below cannot cause reprocessing.
$processedDir = Join-Path $dir "processed"
if (-not (Test-Path $processedDir)) { New-Item -ItemType Directory -Path $processedDir -Force | Out-Null }
Move-Item -Path $file -Destination (Join-Path $processedDir (Split-Path $file -Leaf)) -Force

$marker = Join-Path $dir ".processed.json"
$list = @()
if (Test-Path $marker) { try { $m = Get-Content $marker -Raw | ConvertFrom-Json; if ($m) { $list = @($m) } } catch {} }
$list += [pscustomobject]@{ file=$file; processedAt=(Get-Date).ToString("o"); aiStatus=$status; summary=$summary }
$list | ConvertTo-Json -Depth 5 | Set-Content -Path $marker -Encoding UTF8

return [pscustomobject]@{ ok=$true; status=$status }
