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

$composeProjectName = 'pulseflow-performance'
$livenessUri = 'http://localhost:5254/health/live'
$livenessTimeout = [TimeSpan]::FromSeconds(120)
$livenessPollInterval = [TimeSpan]::FromSeconds(2)

Write-Host "Removing the previous '$composeProjectName' Compose stack and its volumes..."
& $dockerCommand.Source compose --project-name $composeProjectName down --volumes --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "Failed to remove the previous '$composeProjectName' Compose stack."
}

Write-Host "Building and starting the '$composeProjectName' Compose stack..."
& $dockerCommand.Source compose --project-name $composeProjectName up --build --detach
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build and start the '$composeProjectName' Compose stack."
}

Write-Host "Waiting up to $($livenessTimeout.TotalSeconds) seconds for the API liveness endpoint..."
$livenessDeadline = (Get-Date).Add($livenessTimeout)

while ((Get-Date) -lt $livenessDeadline) {
    try {
        $response = Invoke-WebRequest -Uri $livenessUri -Method Get -SkipHttpErrorCheck -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Host 'API is live.'
            return
        }
    }
    catch {
        # The API may not have bound its port yet.
    }

    Start-Sleep -Seconds $livenessPollInterval.TotalSeconds
}

throw "The API did not become live at '$livenessUri' within $($livenessTimeout.TotalSeconds) seconds."
