$cutoff = (Get-Date).AddMinutes(-30)
Write-Output "=== StepFlow-UI/src modified in last 30m ==="
Get-ChildItem 'C:\Source\stepflow-builder\StepFlow-UI\src' -Recurse -File | Where-Object LastWriteTime -gt $cutoff | ForEach-Object { Write-Output ($_.FullName + '  ' + $_.LastWriteTime) }
Write-Output "=== StepFlow-UI root modified in last 30m ==="
Get-ChildItem 'C:\Source\stepflow-builder\StepFlow-UI' -File | Where-Object LastWriteTime -gt $cutoff | ForEach-Object { Write-Output ($_.FullName + '  ' + $_.LastWriteTime) }
