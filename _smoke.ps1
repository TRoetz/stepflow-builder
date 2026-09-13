$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

$backend = Get-ChildItem 'StepFunctionsApp\*.csproj' | Select-Object -First 1
$dah = Get-ChildItem 'DynamicApiHost\*.csproj' | Select-Object -First 1
Write-Output ("BACKEND-PROJ: " + $backend.FullName)
Write-Output ("DAH-PROJ: " + $dah.FullName)
if (-not $backend -or -not $dah) { Write-Output 'MISSING SERVER PROJECTS - abort'; exit 1 }

$env:DynamicApi__ManagementPort = '5099'
$backendProc = Start-Process dotnet -PassThru -NoNewWindow -ArgumentList @('run', '--project', $backend.FullName, '--no-launch-profile')
$env:Engine__BaseUrl = 'http://localhost:5099'
$dahProc = Start-Process dotnet -PassThru -NoNewWindow -ArgumentList @('run', '--project', $dah.FullName, '--no-launch-profile', '--', '--urls', 'http://localhost:5002')

function Wait-Port($port, $timeoutSec) {
  $deadline = (Get-Date).AddSeconds($timeoutSec)
  while ((Get-Date) -lt $deadline) {
    $cli = New-Object System.Net.Sockets.TcpClient
    try { $cli.Connect('localhost', $port); $cli.Close(); return $true } catch { Start-Sleep -Milliseconds 500 } finally { $cli.Dispose() }
  }
  return $false
}

$ok1 = Wait-Port 5099 120
$ok2 = Wait-Port 5002 120
Write-Output ("BACKEND-5099-UP=" + $ok1)
Write-Output ("DAH-5002-UP=" + $ok2)

$exit = 1
if ($ok1 -and $ok2) {
  python "$PSScriptRoot\e2e-parity-check.py" 2>&1 | Tee-Object -FilePath '_parity.log'
  $exit = $LASTEXITCODE
} else {
  Write-Output 'SKIPPED parity script - services not up'
}

Stop-Process -Id $backendProc.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $dahProc.Id -Force -ErrorAction SilentlyContinue
Write-Output ("PARITY-EXIT: " + $exit)
