$ErrorActionPreference = 'Stop'

$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $dockerCommand) {
    throw 'Docker is unavailable. Install Docker Desktop or Docker Engine, then run this script again.'
}

& $dockerCommand.Source info 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is unavailable. Start Docker Desktop or Docker Engine, then run this script again.'
}

if ($null -eq (Get-Command k6 -ErrorAction SilentlyContinue)) {
    throw 'Grafana k6 is unavailable. Install k6, then run this script again.'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$resultsDirectory = Join-Path $PSScriptRoot 'results'
$resultDirectory = Join-Path $resultsDirectory $timestamp

New-Item -ItemType Directory -Path $resultDirectory -ErrorAction Stop | Out-Null
Write-Output $resultDirectory
