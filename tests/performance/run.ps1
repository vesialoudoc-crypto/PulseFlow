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
$downstreamCompletionTimeout = [TimeSpan]::FromMinutes(5)

function Get-AcceptedRequestCountFromK6Summary {
    param([string]$SummaryPath)

    if (-not (Test-Path -LiteralPath $SummaryPath -PathType Leaf)) {
        throw "Grafana k6 summary '$SummaryPath' was not created."
    }

    $summary = Get-Content -LiteralPath $SummaryPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $statusCheck = $summary.root_group.checks.PSObject.Properties['status is 202'].Value

    if ($null -eq $statusCheck -or $null -eq $statusCheck.passes) {
        throw "Grafana k6 summary '$SummaryPath' does not contain the 'status is 202' check result."
    }

    $acceptedRequestCount = [long]$statusCheck.passes
    if ($acceptedRequestCount -lt 0) {
        throw "Grafana k6 summary '$SummaryPath' contains an invalid 'status is 202' pass count."
    }

    return $acceptedRequestCount
}

function Get-RabbitMqQueueMetricValue {
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
        throw "RabbitMQ metric '$MetricName' was not found for queue 'pulseflow.ingestion-batches'."
    }

    return [int]($metricLine -split '\s+')[-1]
}

function Get-RabbitMqQueueStatus {
    $response = Invoke-WebRequest -Uri $rabbitMqMetricsUri -Method Get -ErrorAction Stop
    $metricLines = $response.Content -split "`r?`n"

    return [PSCustomObject]@{
        MessagesReady = Get-RabbitMqQueueMetricValue $metricLines 'rabbitmq_detailed_queue_messages_ready'
        MessagesUnacknowledged = Get-RabbitMqQueueMetricValue $metricLines 'rabbitmq_detailed_queue_messages_unacked'
    }
}

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
    $containerCsvPath = Join-Path $resultDirectory 'containers.csv'
    $databasePath = Join-Path $resultDirectory 'database.txt'

    'timestamp_utc,messages_ready,messages_unacknowledged,messages_total' |
        Set-Content -LiteralPath $rabbitMqCsvPath -Encoding utf8 -ErrorAction Stop

    'timestamp_utc,service,cpu_percent,memory_usage_mb' |
        Set-Content -LiteralPath $containerCsvPath -Encoding utf8 -ErrorAction Stop

    $rabbitMqMetricsSampler = $null
    $containerMetricsSampler = $null
    $k6ExitCode = $null

    try {
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

        $serviceContainerIds = @{}
        foreach ($service in @('api', 'rabbitmq', 'redis', 'postgres')) {
            $containerId = & $dockerCommand.Source compose --project-name $composeProjectName ps -q $service
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
                throw "Failed to find the running container for Compose service '$service'."
            }

            $serviceContainerIds[$service] = $containerId.Trim()
        }

        # Sample in a background job so k6 retains its existing console output.
        # Continue through queue drain because persistence can still consume resources after k6 finishes sending requests.
        $containerMetricsSampler = Start-Job -ArgumentList $dockerCommand.Source, $serviceContainerIds, $containerCsvPath, $rabbitMqMetricsPollIntervalSeconds -ScriptBlock {
        param(
            [string]$DockerCommandPath,
            [hashtable]$ServiceContainerIds,
            [string]$CsvPath,
            [int]$PollIntervalSeconds
        )

        function ConvertTo-Megabytes {
            param([string]$MemoryUsage)

            if ($MemoryUsage -notmatch '^\s*([0-9]+(?:\.[0-9]+)?)\s*([A-Za-z]+)') {
                return $null
            }

            $value = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
            $bytesPerUnit = switch ($Matches[2]) {
                'B' { 1 }
                'kB' { 1000 }
                'KB' { 1000 }
                'KiB' { 1024 }
                'MB' { 1000000 }
                'MiB' { 1048576 }
                'GB' { 1000000000 }
                'GiB' { 1073741824 }
                default { return $null }
            }

            return $value * $bytesPerUnit / 1000000
        }

        while ($true) {
            $sampleStartedAtUtc = [DateTime]::UtcNow
            $containerIds = @($ServiceContainerIds.Values)
            $statsOutput = & $DockerCommandPath stats --no-stream --format '{{.Container}}|{{.CPUPerc}}|{{.MemUsage}}' $containerIds 2>$null
            $statsExitCode = $LASTEXITCODE
            $rows = foreach ($service in @('api', 'rabbitmq', 'redis', 'postgres')) {
                $cpuPercent = $null
                $memoryUsageMb = $null
                $containerIdPrefix = $ServiceContainerIds[$service].Substring(0, [Math]::Min(12, $ServiceContainerIds[$service].Length))
                $stats = $statsOutput | Where-Object { $_.StartsWith($containerIdPrefix) } | Select-Object -First 1

                if ($statsExitCode -eq 0 -and $stats -match '^\S+\|\s*([0-9]+(?:\.[0-9]+)?)%\|(.+)$') {
                    $cpuPercent = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
                    $memoryUsageMb = ConvertTo-Megabytes $Matches[2]
                }

                $cpuValue = if ($null -eq $cpuPercent) { '' } else { $cpuPercent.ToString('F2', [Globalization.CultureInfo]::InvariantCulture) }
                $memoryValue = if ($null -eq $memoryUsageMb) { '' } else { $memoryUsageMb.ToString('F3', [Globalization.CultureInfo]::InvariantCulture) }

                "$($sampleStartedAtUtc.ToString('O')),$service,$cpuValue,$memoryValue"
            }

            $rows | Add-Content -LiteralPath $CsvPath -Encoding utf8 -ErrorAction Stop

            $sampleDurationMilliseconds = ([DateTime]::UtcNow - $sampleStartedAtUtc).TotalMilliseconds
            $sleepMilliseconds = [Math]::Max(0, [int]($PollIntervalSeconds * 1000 - $sampleDurationMilliseconds))
            if ($sleepMilliseconds -gt 0) {
                Start-Sleep -Milliseconds $sleepMilliseconds
            }
        }
        }

        Write-Host "Running k6 scenario '$scenarioPath'..."
        & $k6Command.Source run "--summary-export=$summaryPath" $scenarioPath
        $k6ExitCode = $LASTEXITCODE

        if ($k6ExitCode -ne 0) {
            throw "Grafana k6 failed with exit code $k6ExitCode."
        }

        $acceptedRequestCount = Get-AcceptedRequestCountFromK6Summary $summaryPath
        Write-Host "Accepted HTTP 202 count from k6 summary: $acceptedRequestCount."

        # An empty queue sample alone is not proof of completion: RabbitMQ metrics can briefly
        # report 0/0 immediately after k6 exits. Confirm completion only when PostgreSQL reaches
        # the number of requests that k6 verified as HTTP 202.
        Write-Host "Waiting for 'pulseflow.ingestion-batches' processing to complete..."
        $downstreamCompletionDeadline = [DateTime]::UtcNow.Add($downstreamCompletionTimeout)
        $persistedRowCount = $null

        while ($true) {
            $queueStatus = Get-RabbitMqQueueStatus

            if ($queueStatus.MessagesReady -ne 0 -or $queueStatus.MessagesUnacknowledged -ne 0) {
                Write-Host "Queue backlog: ready=$($queueStatus.MessagesReady), unacknowledged=$($queueStatus.MessagesUnacknowledged)."
            }
            else {
                $persistedRowCount = & $dockerCommand.Source compose --project-name $composeProjectName exec -T postgres `
                    psql -U pulseflow -d pulseflow -tAc 'SELECT COUNT(*) FROM events;'
                if ($LASTEXITCODE -ne 0) {
                    throw 'Failed to query the persisted event count from PostgreSQL.'
                }

                $persistedRowCount = [long]$persistedRowCount.Trim()
                Write-Host "Queue is empty; persisted rows=$persistedRowCount, accepted HTTP 202=$acceptedRequestCount."

                if ($persistedRowCount -eq $acceptedRequestCount) {
                    break
                }
            }

            if ([DateTime]::UtcNow -ge $downstreamCompletionDeadline) {
                break
            }

            Start-Sleep -Seconds $rabbitMqMetricsPollIntervalSeconds
        }

        $databaseCheckCapturedAtUtc = [DateTime]::UtcNow
        @(
            "Accepted HTTP 202 count: $acceptedRequestCount"
            "Persisted row count: $persistedRowCount"
            "Final DB check captured at (UTC): $($databaseCheckCapturedAtUtc.ToString('O'))"
        ) | Set-Content -LiteralPath $databasePath -Encoding utf8 -ErrorAction Stop

        Write-Host "Saved database result to '$databasePath'."

        if ($persistedRowCount -ne $acceptedRequestCount) {
            throw "Downstream processing was not confirmed within $($downstreamCompletionTimeout.TotalMinutes) minutes: persisted row count $persistedRowCount does not match accepted HTTP 202 count $acceptedRequestCount."
        }
    }
    finally {
        Stop-Job -Job $containerMetricsSampler -ErrorAction SilentlyContinue
        Wait-Job -Job $containerMetricsSampler -ErrorAction SilentlyContinue | Out-Null
        Remove-Job -Job $containerMetricsSampler -Force -ErrorAction SilentlyContinue

        Stop-Job -Job $rabbitMqMetricsSampler -ErrorAction SilentlyContinue
        Wait-Job -Job $rabbitMqMetricsSampler -ErrorAction SilentlyContinue | Out-Null
        Remove-Job -Job $rabbitMqMetricsSampler -Force -ErrorAction SilentlyContinue
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
