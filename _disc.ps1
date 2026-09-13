Get-ChildItem -Recurse -Include *.sh, *.psm1 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("SH: " + $_.FullName) }
Get-ChildItem -Recurse -Filter *.tpl -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch 'node_modules' } | ForEach-Object { Write-Output ("TPL: " + $_.FullName) }
Get-ChildItem -Recurse -Directory -Filter *workspace* -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch 'node_modules' } | ForEach-Object { Write-Output ("WS: " + $_.FullName) }
