# Start StepFlow Builder
# Usage: .\start.ps1

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '  StepFlow Builder' -ForegroundColor Cyan
Write-Host ''

# Install dependencies if missing
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
