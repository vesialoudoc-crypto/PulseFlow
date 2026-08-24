$ErrorActionPreference = 'Stop'

# Fail early with actionable errors so a performance run never starts with missing local dependencies.
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
if ($null -eq $dockerCommand) {
    throw 'Docker is unavailable. Install Docker Desktop or Docker Engine, then run this script again.'
}

& $dockerCommand.Source info 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is unavailable. Start Docker Desktop or Docker Engine, then run this script again.'
}

$k6Command = Get-Command k6 -ErrorAction SilentlyContinue
if ($null -eq $k6Command) {
    throw 'Grafana k6 is unavailable. Install k6, then run this script again.'
}

# Give each future run a separate, timestamped location for reproducible artifacts.
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$resultsDirectory = Join-Path $PSScriptRoot 'results'
$resultDirectory = Join-Path $resultsDirectory $timestamp

New-Item -ItemType Directory -Path $resultDirectory -ErrorAction Stop | Out-Null
Write-Output $resultDirectory

$composeProjectName = 'pulseflow-performance'
$livenessUri = 'http://localhost:5254/health/live'
$livenessPollInterval = [TimeSpan]::FromSeconds(2)

# Reset this dedicated Compose project so each measurement begins without state from a previous run.
Write-Host "Removing the previous '$composeProjectName' Compose stack and its volumes..."
& $dockerCommand.Source compose --project-name $composeProjectName down --volumes --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "Failed to remove the previous '$composeProjectName' Compose stack."
}

try {
    Write-Host "Building and starting the '$composeProjectName' Compose stack..."
    & $dockerCommand.Source compose --project-name $composeProjectName up --build --detach
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build and start the '$composeProjectName' Compose stack."
    }

    # Wait for a serving API so later performance steps do not measure startup time or transient connection failures.
    Write-Host 'Waiting for the API liveness endpoint...'
    $apiIsLive = $false

    while (-not $apiIsLive) {
        try {
            $response = Invoke-WebRequest -Uri $livenessUri -Method Get -SkipHttpErrorCheck -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host 'API is live.'
                $apiIsLive = $true
                break
            }
        }
        catch {
            # The API may not have bound its port yet.
        }

        Start-Sleep -Seconds $livenessPollInterval.TotalSeconds
    }

    # Run the fixed baseline scenario and preserve its summary so the observed result can be reviewed later.
    $scenarioPath = Join-Path $PSScriptRoot 'ingestion-baseline.js'
    $summaryPath = Join-Path $resultDirectory 'k6-summary.json'

    Write-Host "Running k6 scenario '$scenarioPath'..."
    & $k6Command.Source run "--summary-export=$summaryPath" $scenarioPath
    if ($LASTEXITCODE -ne 0) {
        throw "Grafana k6 failed with exit code $LASTEXITCODE."
    }

    Write-Host "Saved k6 summary to '$summaryPath'."
}
finally {
    Write-Host "Cleaning up the '$composeProjectName' Compose stack..."
    & $dockerCommand.Source compose --project-name $composeProjectName down --volumes --remove-orphans
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clean up the '$composeProjectName' Compose stack."
    }

    Write-Host "Cleaned up the '$composeProjectName' Compose stack."
}
