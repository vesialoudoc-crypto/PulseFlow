$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    $pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwshCommand) {
        throw 'PowerShell 7 (pwsh) is required to run the performance runner. Install pwsh, then run this script again.'
    }

    Write-Host 'Restarting performance runner with PowerShell 7...'
    & $pwshCommand.Source -NoProfile -File $PSCommandPath @args
    exit $LASTEXITCODE
}

Write-Host "Running performance runner with PowerShell $($PSVersionTable.PSVersion)."

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
$rabbitMqMetricsUri = 'http://localhost:15692/metrics/detailed?family=queue_coarse_metrics'
$rabbitMqMetricsPollIntervalSeconds = 2

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
            $response = Invoke-WebRequest -Uri $livenessUri -Method Get
            if ($response.StatusCode -ne 200) {
                throw "The API liveness endpoint returned HTTP $($response.StatusCode)."
            }

            Write-Host 'API is live.'
            $apiIsLive = $true
            break
        }
        catch {
            $isConnectionError = $_.CategoryInfo.Category -eq 'ConnectionError'
            $isResponseEnded =
                $_.FullyQualifiedErrorId -like '*ResponseEnded*' -or
                $_.ErrorDetails.Message -like '*ResponseEnded*'

            if (-not ($isConnectionError -or $isResponseEnded)) {
                throw
            }

            # The API may not have bound its port yet or may have closed a request while starting.
        }

        Start-Sleep -Seconds $livenessPollInterval.TotalSeconds
    }

    # Run the fixed baseline scenario and preserve its summary so the observed result can be reviewed later.
    $scenarioPath = Join-Path $PSScriptRoot 'ingestion-baseline.js'
    $summaryPath = Join-Path $resultDirectory 'k6-summary.json'
    $rabbitMqCsvPath = Join-Path $resultDirectory 'rabbitmq.csv'

    'timestamp_utc,messages_ready,messages_unacknowledged,messages_total' |
        Set-Content -LiteralPath $rabbitMqCsvPath -Encoding utf8 -ErrorAction Stop

    # Sample RabbitMQ in a background job so k6 retains its existing console output.
    # Persist only the configured queue's backlog gauges, not complete Prometheus responses.
    $rabbitMqMetricsSampler = Start-Job -ArgumentList $rabbitMqMetricsUri, $rabbitMqCsvPath, $rabbitMqMetricsPollIntervalSeconds -ScriptBlock {
        param(
            [string]$MetricsUri,
            [string]$CsvPath,
            [int]$PollIntervalSeconds
        )

        function Get-QueueMetricValue {
            param(
                [string[]]$MetricLines,
                [string]$MetricName
            )

            $metricPattern =
                '^' + [regex]::Escape($MetricName) + '\{[^}]*queue="pulseflow\.ingestion-batches"[^}]*\}\s+(.+)$'
            $metricLine = $MetricLines |
                Where-Object {
                    $_ -match $metricPattern
                } |
                Select-Object -First 1

            if ($null -eq $metricLine) {
                return $null
            }

            return ($metricLine -split '\s+')[-1]
        }

        while ($true) {
            $capturedAtUtc = [DateTime]::UtcNow

            try {
                $response = Invoke-WebRequest -Uri $MetricsUri -Method Get -ErrorAction Stop
                $metricLines = $response.Content -split "`r?`n"
                $messagesReady = Get-QueueMetricValue $metricLines 'rabbitmq_detailed_queue_messages_ready'
                $messagesUnacknowledged = Get-QueueMetricValue $metricLines 'rabbitmq_detailed_queue_messages_unacked'
                $messagesTotal = Get-QueueMetricValue $metricLines 'rabbitmq_detailed_queue_messages'

                "$($capturedAtUtc.ToString('O')),$messagesReady,$messagesUnacknowledged,$messagesTotal" |
                    Add-Content -LiteralPath $CsvPath -Encoding utf8 -ErrorAction Stop
            }
            catch {
                Write-Warning "Unable to sample RabbitMQ metrics: $($_.Exception.Message)"
            }

            Start-Sleep -Seconds $PollIntervalSeconds
        }
    }

    Write-Host "Running k6 scenario '$scenarioPath'..."
    $k6ExitCode = $null

    try {
        & $k6Command.Source run "--summary-export=$summaryPath" $scenarioPath
        $k6ExitCode = $LASTEXITCODE
    }
    finally {
        Stop-Job -Job $rabbitMqMetricsSampler -ErrorAction SilentlyContinue
        Wait-Job -Job $rabbitMqMetricsSampler | Out-Null
        Remove-Job -Job $rabbitMqMetricsSampler -Force
    }

    if ($k6ExitCode -ne 0) {
        throw "Grafana k6 failed with exit code $k6ExitCode."
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
