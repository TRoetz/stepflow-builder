# Start StepFlow Builder
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'

# Frontend project lives in the sibling StepFlow-UI folder
$uiRoot = Join-Path $PSScriptRoot '..\StepFlow-UI'

Write-Host ''
Write-Host '  StepFlow Builder' -ForegroundColor Cyan
Write-Host ''

# --- Stop any running instances before starting fresh -----------------------

function Stop-ProcessesOnPort {
    param([int]$Port, [string]$Name)
    $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($null -ne $conns) {
        Write-Host "  Stopping running $Name (port $Port)..." -ForegroundColor Yellow
        $conns | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object {
            Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue
        }
    }
}

# Backend app process (StepFunctionsApp.exe), however it was started
Get-Process -Name 'StepFunctionsApp' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host '  Stopping running .NET Execution Engine...' -ForegroundColor Yellow
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

# Parent 'dotnet run' processes for this project (may not have bound the port yet)
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine -like "*$PSScriptRoot*"
} | ForEach-Object {
    Write-Host '  Stopping running dotnet process...' -ForegroundColor Yellow
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}

# Frontend dev server (vite) on port 3001, backend on port 5001, fake test APIs on port 5095
Stop-ProcessesOnPort -Port 3001 -Name 'Frontend Dev Server'
Stop-ProcessesOnPort -Port 5001 -Name '.NET Execution Engine'
Stop-ProcessesOnPort -Port 5095 -Name 'Fake Test API Host'

# Wait for the ports to be released before starting again
foreach ($port in @(5001, 3001, 5095)) {
    $attempts = 0
    while (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        if (++$attempts -ge 20) { break }
        Start-Sleep -Milliseconds 500
    }
}

# --- Install dependencies if missing ----------------------------------------

if (-not (Test-Path (Join-Path $uiRoot 'node_modules'))) {
    Write-Host '  Installing frontend dependencies...' -ForegroundColor Yellow
    Push-Location $uiRoot
    npm install
    Pop-Location
    Write-Host '  Done.' -ForegroundColor Green
    Write-Host ''
}

# Start .NET 10 Backend in separate terminal window
Write-Host '  Starting .NET 10 Execution Engine...' -ForegroundColor Cyan
Start-Process dotnet -ArgumentList "run"

# Start Fake Test API Host on port 5095 (target of Flows/FakeData_*.json)
Write-Host '  Starting Fake Test API Host (port 5095)...' -ForegroundColor Cyan
Start-Process dotnet -ArgumentList "run", "--project", "Stepflow-Builder-Tests"

# Start Vite dev server (Proxies /api to .NET backend on port 5001)
Write-Host '  Starting Frontend Dev Server (port 3001)...' -ForegroundColor Cyan
Push-Location $uiRoot
npm run dev
Pop-Location
