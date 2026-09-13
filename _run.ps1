$repo = $PSScriptRoot
Set-Location $repo
$ErrorActionPreference = 'Continue'

Write-Output "== PROJECTS =="
$projFiles = Get-ChildItem -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch '\\obj\\' }
foreach ($p in $projFiles) { Write-Output ("PROJ: " + $p.FullName) }

$testProj = $projFiles | Where-Object { $_.Name -match '(?i)test' }
foreach ($tp in $testProj) {
  Write-Output ("== TESTS: " + $tp.FullName + " ==")
  dotnet test $tp.FullName --nologo -v minimal 2>&1 | Select-Object -Last 8
}

Write-Output "== LAUNCH SETTINGS =="
foreach ($ls in (Get-ChildItem -Recurse -Filter launchSettings.json -ErrorAction SilentlyContinue)) {
  Write-Output ("LS: " + $ls.FullName)
  Get-Content $ls.FullName -Raw
}

Write-Output "== APPSETTINGS =="
foreach ($af in (Get-ChildItem -Recurse -Filter appsettings*.json | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })) {
  Write-Output ("CFG: " + $af.FullName)
  Get-Content $af.FullName -Raw
}

Write-Output "== DOCKER =="
foreach ($dc in (Get-ChildItem -Recurse -Filter '*compose*.yml' -ErrorAction SilentlyContinue)) {
  Write-Output ("DC: " + $dc.FullName)
  Get-Content $dc.FullName -Raw
}
