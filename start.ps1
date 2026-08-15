# Start StepFlow Builder
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'

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

# Frontend dev server (vite) on port 3001 and backend on port 5001
Stop-ProcessesOnPort -Port 3001 -Name 'Frontend Dev Server'
Stop-ProcessesOnPort -Port 5001 -Name '.NET Execution Engine'

# Wait for the ports to be released before starting again
foreach ($port in @(5001, 3001)) {
    $attempts = 0
    while (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        if (++$attempts -ge 20) { break }
        Start-Sleep -Milliseconds 500
    }
}

# --- Install dependencies if missing ----------------------------------------

if (-not (Test-Path 'node_modules')) {
    Write-Host '  Installing dependencies...' -ForegroundColor Yellow
    npm install
    Write-Host '  Done.' -ForegroundColor Green
    Write-Host ''
}

# Start .NET 10 Backend in separate terminal window
Write-Host '  Starting .NET 10 Execution Engine...' -ForegroundColor Cyan
Start-Process dotnet -ArgumentList "run"

# Start Vite dev server (Proxies /api to .NET backend on port 5001)
Write-Host '  Starting Frontend Dev Server (port 3001)...' -ForegroundColor Cyan
npm run dev
