# CheckNewFiles - scan C:\temp\order-exec for unprocessed order CSVs.
# Emits: { hasNew, file (first pending path or null), pendingCount, filesInDrop }
$ErrorActionPreference = "Stop"
$dir = "C:\temp\order-exec"
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$marker = Join-Path $dir ".processed.json"
$processed = @()
if (Test-Path $marker) {
    try {
        $m = Get-Content $marker -Raw | ConvertFrom-Json
        if ($m) { foreach ($x in @($m)) { if ($x.file) { $processed += [string]$x.file } } }
    } catch {}
}

$candidates = @(Get-ChildItem -Path $dir -Filter *.csv -File | Where-Object { (([datetime]::UtcNow - $_.LastWriteTimeUtc).TotalSeconds -ge 10) })
$pending = @($candidates | Where-Object { $processed -notcontains $_.FullName })

return [pscustomobject]@{
    hasNew = ($pending.Count -gt 0)
    file = $(if ($pending.Count -gt 0) { $pending[0].FullName } else { $null })
    pendingCount = $pending.Count
    filesInDrop = @($candidates | ForEach-Object { $_.Name })
}
