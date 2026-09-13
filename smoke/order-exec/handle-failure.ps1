# HandleFailure - catch-all for state failures: move CSV to failed\ and record a marker.
# Input: { file, orderId?, failError: { error, cause? }, ... }
$ErrorActionPreference = "Stop"

$file = [string]$input_data.file
$dir = "C:\temp\order-exec"

$reason = "unknown failure"
if ($input_data.failError -and $input_data.failError.error) { $reason = [string]$input_data.failError.error }
elseif ($input_data.failError) { $reason = ([pscustomobject]$input_data.failError | ConvertTo-Json -Depth 5 -Compress) }
if ($reason.Length -gt 2000) { $reason = $reason.Substring(0, 2000) + " ...[truncated]" }

$failedDir = Join-Path $dir "failed"
if (-not (Test-Path $failedDir)) { New-Item -ItemType Directory -Path $failedDir -Force | Out-Null }
if (Test-Path $file) { Move-Item -Path $file -Destination (Join-Path $failedDir (Split-Path $file -Leaf)) -Force }

$marker = Join-Path $dir ".processed.json"
$list = @()
if (Test-Path $marker) { try { $m = Get-Content $marker -Raw | ConvertFrom-Json; if ($m) { $list = @($m) } } catch {} }
$list += [pscustomobject]@{ file=$file; processedAt=(Get-Date).ToString("o"); aiStatus="failed"; summary=$reason }
$list | ConvertTo-Json -Depth 5 | Set-Content -Path $marker -Encoding UTF8

return [pscustomobject]@{ ok=$true; reason=$reason }
