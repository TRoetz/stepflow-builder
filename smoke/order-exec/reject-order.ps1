# RejectOrder - order failed validation: write a rejection notice, record marker, move CSV to failed\.
# Input: { file, orderId, customerName, errors[] }
$ErrorActionPreference = "Stop"

$file = [string]$input_data.file
$orderId = if ($input_data.orderId) { [string]$input_data.orderId } else { "unknown" }
$errors = @($input_data.errors | ForEach-Object { [string]$_ })
if (-not $errors -or $errors.Count -eq 0) { $errors = @("validation failed (no detail)") }

$outDir = "C:\temp\order-exec\output"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$notice = Join-Path $outDir ("rejected-" + $orderId + ".txt")
$text = "ORDER REJECTION`nOrderId: $orderId`nCustomer: $($input_data.customerName)`nDate (UTC): $(Get-Date).ToUniversalTime().ToString('o')`n`nReasons:`n" + (($errors | ForEach-Object { "- $_" }) -join "`n")
Set-Content -Path $notice -Value $text -Encoding UTF8

$dir = "C:\temp\order-exec"
$s = ($errors -join "; ")
if ($s.Length -gt 2000) { $s = $s.Substring(0, 2000) + " ...[truncated]" }
$marker = Join-Path $dir ".processed.json"
$list = @()
if (Test-Path $marker) { try { $m = Get-Content $marker -Raw | ConvertFrom-Json; if ($m) { $list = @($m) } } catch {} }
$list += [pscustomobject]@{ file=$file; processedAt=(Get-Date).ToString("o"); aiStatus="rejected"; summary=$s }
$list | ConvertTo-Json -Depth 5 | Set-Content -Path $marker -Encoding UTF8

if (Test-Path $file) {
    $failedDir = Join-Path $dir "failed"
    if (-not (Test-Path $failedDir)) { New-Item -ItemType Directory -Path $failedDir -Force | Out-Null }
    Move-Item -Path $file -Destination (Join-Path $failedDir (Split-Path $file -Leaf)) -Force
}

return [pscustomobject]@{ ok=$true; orderId=$orderId; notice=$notice }
