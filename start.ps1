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

# Start Vite dev server
npm run dev
