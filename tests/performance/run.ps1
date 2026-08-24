[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Single', 'Multi')]
    [string]$Topology,

    [switch]$ValidateTopology
)

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

# Give each future run a separate, timestamped location for reproducible artifacts.
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$resultsDirectory = Join-Path $PSScriptRoot 'results'
$resultDirectory = Join-Path $resultsDirectory $timestamp

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\\..')).Path
$composeDirectory = Join-Path $repositoryRoot 'infra\\local'
$composeFileArguments = @(
    '--file'
    (Join-Path $composeDirectory 'compose.yaml')
    '--file'
    (Join-Path $composeDirectory "compose.$($Topology.ToLowerInvariant()).yaml")
)

$topologyConfiguration = switch ($Topology) {
    'Single' {
        [PSCustomObject]@{
            ComposeProjectName = 'pulseflow-performance-single'
            IngressPort = 5255
            RabbitMqMetricsPort = 15693
            ResourceServices = @('api', 'rabbitmq', 'redis', 'postgres')
        }
    }
    'Multi' {
        [PSCustomObject]@{
            ComposeProjectName = 'pulseflow-performance-multi'
            IngressPort = 5256
            RabbitMqMetricsPort = 15694
            ResourceServices = @('haproxy', 'api-1', 'api-2', 'rabbitmq', 'redis', 'postgres')
        }
    }
}

$composeProjectName = $topologyConfiguration.ComposeProjectName
$resourceServices = $topologyConfiguration.ResourceServices
$livenessUri = "http://localhost:$($topologyConfiguration.IngressPort)/health/live"
$rabbitMqMetricsUri = "http://localhost:$($topologyConfiguration.RabbitMqMetricsPort)/metrics/detailed?family=queue_coarse_metrics"
$expectedComposeServices = @($resourceServices + 'migrations' | Sort-Object)

# Use ports distinct from normal local Compose defaults and from the other performance
# topology so either isolated performance stack can coexist with a normal stack.
$env:PULSEFLOW_INGRESS_PORT = $topologyConfiguration.IngressPort
$env:PULSEFLOW_RABBITMQ_METRICS_PORT = $topologyConfiguration.RabbitMqMetricsPort
$env:BASE_URL = "http://localhost:$($topologyConfiguration.IngressPort)"

if ($ValidateTopology) {
    $resolvedComposeServices = @(
        & $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName config --services
    ) | ForEach-Object { $_.Trim() } | Sort-Object

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resolve the Compose configuration for topology '$Topology'."
    }

    $missingServices = @($expectedComposeServices | Where-Object { $_ -notin $resolvedComposeServices })
    $unexpectedServices = @($resolvedComposeServices | Where-Object { $_ -notin $expectedComposeServices })
    if ($missingServices.Count -ne 0 -or $unexpectedServices.Count -ne 0) {
        throw "Topology '$Topology' resolved unexpected Compose services. Missing: $($missingServices -join ', '). Unexpected: $($unexpectedServices -join ', ')."
    }

    Write-Host "Topology '$Topology' resource services: $($resourceServices -join ', ')."
    Write-Host "Topology '$Topology' Compose services: $($resolvedComposeServices -join ', ')."
    return
}

$k6Command = Get-Command k6 -ErrorAction SilentlyContinue
if ($null -eq $k6Command) {
    throw 'Grafana k6 is unavailable. Install k6, then run this script again.'
}

New-Item -ItemType Directory -Path $resultDirectory -ErrorAction Stop | Out-Null
Write-Output $resultDirectory

$livenessPollInterval = [TimeSpan]::FromSeconds(2)
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

function Get-RequiredNonNegativeDouble {
    param(
        [object]$Value,
        [string]$Description
    )

    if ($null -eq $Value) {
        throw "$Description is missing."
    }

    try {
        $numericValue = [double]::Parse(
            $Value.ToString(),
            [Globalization.CultureInfo]::InvariantCulture
        )
    }
    catch {
        throw "$Description is not a valid number."
    }

    if ([double]::IsNaN($numericValue) -or [double]::IsInfinity($numericValue) -or $numericValue -lt 0) {
        throw "$Description must be a non-negative finite number."
    }

    return $numericValue
}

function Get-RequiredK6MetricValue {
    param(
        [object]$Summary,
        [string]$MetricName,
        [string]$ValueName,
        [string]$SummaryPath
    )

    $metric = $Summary.metrics.PSObject.Properties[$MetricName].Value
    if ($null -eq $metric) {
        throw "Grafana k6 summary '$SummaryPath' does not contain metric '$MetricName'."
    }

    $value = $metric.PSObject.Properties[$ValueName].Value
    return Get-RequiredNonNegativeDouble $value "Grafana k6 metric '$MetricName' value '$ValueName' in '$SummaryPath'"
}

function Get-CurrentRunPerformanceReport {
    param(
        [string]$SummaryPath,
        [string]$RabbitMqCsvPath,
        [string]$ContainerCsvPath,
        [string]$DatabasePath,
        [string]$ResultDirectory,
        [string]$Duration,
        [string[]]$ResourceServices
    )

    foreach ($artifactPath in @($SummaryPath, $RabbitMqCsvPath, $ContainerCsvPath, $DatabasePath)) {
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Required performance artifact '$artifactPath' was not created for this run."
        }
    }

    try {
        $summary = Get-Content -LiteralPath $SummaryPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Failed to parse Grafana k6 summary '$SummaryPath': $($_.Exception.Message)"
    }

    $requestCount = Get-RequiredK6MetricValue $summary 'http_reqs' 'count' $SummaryPath
    $requestsPerSecond = Get-RequiredK6MetricValue $summary 'http_reqs' 'rate' $SummaryPath
    $virtualUsers = Get-RequiredK6MetricValue $summary 'vus_max' 'max' $SummaryPath
    $averageLatency = Get-RequiredK6MetricValue $summary 'http_req_duration' 'avg' $SummaryPath
    $p90Latency = Get-RequiredK6MetricValue $summary 'http_req_duration' 'p(90)' $SummaryPath
    $p95Latency = Get-RequiredK6MetricValue $summary 'http_req_duration' 'p(95)' $SummaryPath
    $maxLatency = Get-RequiredK6MetricValue $summary 'http_req_duration' 'max' $SummaryPath
    $httpFailureRate = Get-RequiredK6MetricValue $summary 'http_req_failed' 'value' $SummaryPath

    if ($httpFailureRate -gt 1) {
        throw "Grafana k6 metric 'http_req_failed' rate in '$SummaryPath' must not exceed 1."
    }

    $statusCheck = $summary.root_group.checks.PSObject.Properties['status is 202'].Value
    if ($null -eq $statusCheck) {
        throw "Grafana k6 summary '$SummaryPath' does not contain the 'status is 202' check result."
    }

    $acceptedRequestCount = Get-RequiredNonNegativeDouble $statusCheck.passes "Grafana k6 'status is 202' check passes in '$SummaryPath'"
    $failed202CheckCount = Get-RequiredNonNegativeDouble $statusCheck.fails "Grafana k6 'status is 202' check failures in '$SummaryPath'"

    try {
        $databaseContents = Get-Content -LiteralPath $DatabasePath -Raw -ErrorAction Stop
    }
    catch {
        throw "Failed to read database verification '$DatabasePath': $($_.Exception.Message)"
    }

    $acceptedDatabaseMatch = [regex]::Match($databaseContents, '(?m)^Accepted HTTP 202 count:\s*(\d+)\s*$')
    $persistedDatabaseMatch = [regex]::Match($databaseContents, '(?m)^Persisted row count:\s*(\d+)\s*$')
    if (-not $acceptedDatabaseMatch.Success -or -not $persistedDatabaseMatch.Success) {
        throw "Database verification '$DatabasePath' does not contain the required accepted HTTP 202 and persisted row counts."
    }

    $acceptedDatabaseCount = Get-RequiredNonNegativeDouble $acceptedDatabaseMatch.Groups[1].Value "Accepted HTTP 202 count in '$DatabasePath'"
    $persistedRowCount = Get-RequiredNonNegativeDouble $persistedDatabaseMatch.Groups[1].Value "Persisted row count in '$DatabasePath'"

    if ($acceptedDatabaseCount -ne $acceptedRequestCount) {
        throw "Database verification '$DatabasePath' accepted HTTP 202 count does not match the current run's k6 summary."
    }

    try {
        $rabbitMqRows = @(Import-Csv -LiteralPath $RabbitMqCsvPath -ErrorAction Stop)
    }
    catch {
        throw "Failed to parse RabbitMQ samples '$RabbitMqCsvPath': $($_.Exception.Message)"
    }

    if ($rabbitMqRows.Count -eq 0) {
        throw "RabbitMQ samples '$RabbitMqCsvPath' do not contain any measurements."
    }

    $rabbitMqSamples = for ($index = 0; $index -lt $rabbitMqRows.Count; $index++) {
        $row = $rabbitMqRows[$index]

        try {
            $timestamp = [DateTimeOffset]::Parse(
                $row.timestamp_utc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind
            )
        }
        catch {
            throw "RabbitMQ sample $($index + 1) in '$RabbitMqCsvPath' has an invalid timestamp."
        }

        [PSCustomObject]@{
            Index = $index
            Timestamp = $timestamp
            MessagesReady = Get-RequiredNonNegativeDouble $row.messages_ready "RabbitMQ messages_ready in sample $($index + 1) of '$RabbitMqCsvPath'"
            MessagesUnacknowledged = Get-RequiredNonNegativeDouble $row.messages_unacknowledged "RabbitMQ messages_unacknowledged in sample $($index + 1) of '$RabbitMqCsvPath'"
            MessagesTotal = Get-RequiredNonNegativeDouble $row.messages_total "RabbitMQ messages_total in sample $($index + 1) of '$RabbitMqCsvPath'"
        }
    }

    $rabbitMqSamples = @($rabbitMqSamples | Sort-Object Timestamp, Index)
    $peakReady = ($rabbitMqSamples | Measure-Object -Property MessagesReady -Maximum).Maximum
    $peakUnacknowledged = ($rabbitMqSamples | Measure-Object -Property MessagesUnacknowledged -Maximum).Maximum
    $peakTotalSample = $rabbitMqSamples |
        Sort-Object @{ Expression = 'MessagesTotal'; Descending = $true }, Timestamp, Index |
        Select-Object -First 1
    $peakTotalBacklog = $peakTotalSample.MessagesTotal
    $drainSample = $rabbitMqSamples |
        Where-Object { $_.Timestamp -gt $peakTotalSample.Timestamp -and $_.MessagesTotal -eq 0 } |
        Select-Object -First 1

    if ($null -eq $drainSample) {
        throw "RabbitMQ samples '$RabbitMqCsvPath' do not contain a zero-backlog sample later than the peak total backlog sample."
    }

    $drainTimeSeconds = ($drainSample.Timestamp - $peakTotalSample.Timestamp).TotalSeconds

    try {
        $containerRows = @(Import-Csv -LiteralPath $ContainerCsvPath -ErrorAction Stop)
    }
    catch {
        throw "Failed to parse container samples '$ContainerCsvPath': $($_.Exception.Message)"
    }

    if ($containerRows.Count -eq 0) {
        throw "Container samples '$ContainerCsvPath' do not contain any measurements."
    }

    $resourcePeaks = @{}
    foreach ($service in $ResourceServices) {
        $serviceRows = @($containerRows | Where-Object { $_.service -eq $service })
        if ($serviceRows.Count -eq 0) {
            throw "Container samples '$ContainerCsvPath' do not contain measurements for service '$service'."
        }

        $cpuSamples = for ($index = 0; $index -lt $serviceRows.Count; $index++) {
            Get-RequiredNonNegativeDouble $serviceRows[$index].cpu_percent "Container CPU metric for service '$service' in sample $($index + 1) of '$ContainerCsvPath'"
        }
        $memorySamples = for ($index = 0; $index -lt $serviceRows.Count; $index++) {
            Get-RequiredNonNegativeDouble $serviceRows[$index].memory_usage_mb "Container memory metric for service '$service' in sample $($index + 1) of '$ContainerCsvPath'"
        }

        $resourcePeaks[$service] = [PSCustomObject]@{
            Cpu = ($cpuSamples | Measure-Object -Maximum).Maximum
            Memory = ($memorySamples | Measure-Object -Maximum).Maximum
        }
    }

    $culture = [Globalization.CultureInfo]::InvariantCulture
    $acceptedEqualsPersisted = $acceptedRequestCount -eq $persistedRowCount

    Write-Host ''
    Write-Host '=== PERFORMANCE REPORT ==='
    Write-Host "VUs: $VirtualUsers"
    Write-Host "Duration: $Duration"
    Write-Host "Result directory: $ResultDirectory"
    Write-Host ''
    Write-Host 'HTTP:'
    Write-Host "Requests: $($requestCount.ToString('N0', $culture))"
    Write-Host "Requests/sec: $($requestsPerSecond.ToString('F2', $culture))"
    Write-Host "Avg latency: $($averageLatency.ToString('F2', $culture)) ms"
    Write-Host "p90 latency: $($p90Latency.ToString('F2', $culture)) ms"
    Write-Host "p95 latency: $($p95Latency.ToString('F2', $culture)) ms"
    Write-Host "Max latency: $($maxLatency.ToString('F2', $culture)) ms"
    Write-Host "HTTP failures: $(($httpFailureRate * 100).ToString('F2', $culture))%"
    Write-Host "Failed 202 checks: $($failed202CheckCount.ToString('N0', $culture))"
    Write-Host "Accepted HTTP 202: $($acceptedRequestCount.ToString('N0', $culture))"
    Write-Host ''
    Write-Host 'Persistence:'
    Write-Host "Persisted PostgreSQL rows: $($persistedRowCount.ToString('N0', $culture))"
    Write-Host "Accepted == persisted: $acceptedEqualsPersisted"
    Write-Host ''
    Write-Host 'RabbitMQ:'
    Write-Host "Peak ready: $($peakReady.ToString('N0', $culture))"
    Write-Host "Peak unacknowledged: $($peakUnacknowledged.ToString('N0', $culture))"
    Write-Host "Peak total backlog: $($peakTotalBacklog.ToString('N0', $culture))"
    Write-Host "Approximate drain time: $($drainTimeSeconds.ToString('F1', $culture)) s"
    Write-Host ''
    Write-Host 'Resources:'
    foreach ($service in $ResourceServices) {
        Write-Host "$service peak CPU: $($resourcePeaks[$service].Cpu.ToString('F2', $culture)) %"
        Write-Host "$service peak memory: $($resourcePeaks[$service].Memory.ToString('F2', $culture)) MB"
    }
    Write-Host '=========================='
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
& $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName down --volumes --remove-orphans
if ($LASTEXITCODE -ne 0) {
    throw "Failed to remove the previous '$composeProjectName' Compose stack."
}

try {
    Write-Host "Building and starting the '$composeProjectName' Compose stack..."
    & $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName up --build --detach
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
        foreach ($service in $resourceServices) {
            $containerId = & $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName ps -q $service
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
                throw "Failed to find the running container for Compose service '$service'."
            }

            $serviceContainerIds[$service] = $containerId.Trim()
        }

        # Sample in a background job so k6 retains its existing console output.
        # Continue through queue drain because persistence can still consume resources after k6 finishes sending requests.
        $containerMetricsSampler = Start-Job -ArgumentList $dockerCommand.Source, $serviceContainerIds, $containerCsvPath, $rabbitMqMetricsPollIntervalSeconds, $resourceServices -ScriptBlock {
        param(
            [string]$DockerCommandPath,
            [hashtable]$ServiceContainerIds,
            [string]$CsvPath,
            [int]$PollIntervalSeconds,
            [string[]]$ResourceServices
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
            $rows = foreach ($service in $ResourceServices) {
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
                $persistedRowCount = & $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName exec -T postgres `
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

    Get-CurrentRunPerformanceReport `
        -SummaryPath $summaryPath `
        -RabbitMqCsvPath $rabbitMqCsvPath `
        -ContainerCsvPath $containerCsvPath `
        -DatabasePath $databasePath `
        -ResultDirectory $resultDirectory `
        -Duration '10s' `
        -ResourceServices $resourceServices
}
finally {
    Write-Host "Cleaning up the '$composeProjectName' Compose stack..."
    & $dockerCommand.Source compose @composeFileArguments --project-name $composeProjectName down --volumes --remove-orphans
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clean up the '$composeProjectName' Compose stack."
    }

    Write-Host "Cleaned up the '$composeProjectName' Compose stack."
}
