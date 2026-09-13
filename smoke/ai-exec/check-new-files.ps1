# CheckNewFiles - scan the ai-exec drop folder for unprocessed CSVs.
# Emits: { hasNew, file (first pending path or null), pendingCount, filesInDrop }
$dir = "C:\temp\ai-exec"
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

# Processed marker: array of { file, processedAt, aiStatus, summary }
$marker = Join-Path $dir ".processed.json"
$processed = @()
if (Test-Path $marker) {
    try {
        $m = Get-Content $marker -Raw | ConvertFrom-Json
        if ($m) { foreach ($x in @($m)) { if ($x.file) { $processed += [string]$x.file } } }
    } catch {}
}

# Top-level CSVs only (processed/ and failed/ subfolders are excluded by non-recursive listing).
# Require 10s of age so a file still being copied is not picked up mid-write.
$files = @(Get-ChildItem -Path $dir -Filter "*.csv" -File | Sort-Object LastWriteTime)
$new = @()
foreach ($f in $files) {
    if (($processed -notcontains $f.FullName) -and ((Get-Date) - $f.LastWriteTime -gt [TimeSpan]::FromSeconds(10))) {
        $new += $f.FullName
    }
}

$file = $null
if ($new.Count -gt 0) { $file = [string]$new[0] }

return [pscustomobject]@{
    hasNew       = ($new.Count -gt 0)
    file         = $file
    pendingCount = $new.Count
    filesInDrop  = @($files | ForEach-Object { $_.Name })
}
