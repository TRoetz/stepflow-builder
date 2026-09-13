$target = Get-ChildItem *.ps1 | Select-String 'BACKEND-PROJ' -List | Select-Object -First 1 -ExpandProperty Path
Write-Output ("EXEC: " + $target)
& powershell -NoProfile -ExecutionPolicy Bypass -File $target
