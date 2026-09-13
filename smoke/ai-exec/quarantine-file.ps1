# QuarantineFile - move a CSV that failed DataExchange import into C:\temp\ai-exec\failed\.
# Input: { file, ..., importError: { error, cause } } (catch resultPath merge)
$file = [string]$input_data.file

$errText = "unknown"
if ($input_data.importError -and $input_data.importError.error) {
    $e = [string]$input_data.importError.error
    if ($e.Length -gt 1000) { $e = $e.Substring(0, 1000) + " ...[truncated]" }
    $errText = $e
}

$moved = $false
if ($file -and (Test-Path $file)) {
    $failedDir = "C:\temp\ai-exec\failed"
    if (-not (Test-Path $failedDir)) { New-Item -ItemType Directory -Path $failedDir -Force | Out-Null }
    Move-Item -Path $file -Destination (Join-Path $failedDir (Split-Path $file -Leaf)) -Force
    $moved = $true
}

return [pscustomobject]@{ quarantined = $file; moved = $moved; importError = $errText }
