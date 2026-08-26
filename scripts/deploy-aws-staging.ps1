[CmdletBinding()]
param(
    [string]$TerraformDirectory = (Join-Path $PSScriptRoot "..\infra\aws"),
    [string]$AwsProfile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-TerraformOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [switch]$Sensitive,
        [switch]$Json
    )

    $arguments = @("output")
    if ($Json) {
        $arguments += "-json"
    }
    else {
        $arguments += "-raw"
    }
    $arguments += $Name

    $value = & terraform @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "terraform output $Name failed."
    }

    return $value
}

function Invoke-Aws {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $awsArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($AwsProfile)) {
        $awsArguments += @("--profile", $AwsProfile)
    }

    $awsArguments += @("--region", $script:awsRegion)
    $result = & aws @awsArguments @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AWS CLI command failed: aws $($Arguments -join ' ')"
    }

    return $result
}

function ConvertTo-ConnectionStringValue {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return '"' + $Value.Replace('"', '""') + '"'
}

function Put-ConnectionSecret {
    param(
        [Parameter(Mandatory)]
        [string]$SecretArn,
        [Parameter(Mandatory)]
        [string]$ConnectionString
    )

    $secretValue = @{ connectionString = $ConnectionString } | ConvertTo-Json -Compress
    Invoke-Aws -Arguments @(
        "secretsmanager",
        "put-secret-value",
        "--secret-id",
        $SecretArn,
        "--secret-string",
        $secretValue,
        "--output",
        "json"
    ) | Out-Null
}

function Get-TaskContainerExitCode {
    param(
        [Parameter(Mandatory)]
        [string]$Cluster,
        [Parameter(Mandatory)]
        [string]$TaskArn,
        [Parameter(Mandatory)]
        [string]$ContainerName
    )

    $taskJson = Invoke-Aws -Arguments @(
        "ecs",
        "describe-tasks",
        "--cluster",
        $Cluster,
        "--tasks",
        $TaskArn,
        "--output",
        "json"
    )
    $task = ($taskJson | ConvertFrom-Json).tasks[0]
    $container = $task.containers | Where-Object { $_.name -eq $ContainerName } | Select-Object -First 1

    if ($null -eq $container -or $null -eq $container.exitCode) {
        throw "Task $TaskArn stopped without an exit code for container '$ContainerName'. stoppedReason: $($task.stoppedReason)"
    }

    return [int]$container.exitCode
}

function Invoke-OneShotTask {
    param(
        [Parameter(Mandatory)]
        [string]$TaskDefinitionArn,
        [Parameter(Mandatory)]
        [string]$ContainerName,
        [string]$OverridesJson
    )

    $runArguments = @(
        "ecs",
        "run-task",
        "--cluster",
        $script:clusterName,
        "--task-definition",
        $TaskDefinitionArn,
        "--launch-type",
        "FARGATE",
        "--network-configuration",
        $script:networkConfiguration,
        "--count",
        "1",
        "--query",
        "tasks[0].taskArn",
        "--output",
        "text"
    )

    if (-not [string]::IsNullOrWhiteSpace($OverridesJson)) {
        $runArguments += @("--overrides", $OverridesJson)
    }

    $taskArn = (Invoke-Aws -Arguments $runArguments).Trim()
    if ([string]::IsNullOrWhiteSpace($taskArn) -or $taskArn -eq "None") {
        throw "ECS did not start the $ContainerName one-shot task."
    }

    Invoke-Aws -Arguments @(
        "ecs",
        "wait",
        "tasks-stopped",
        "--cluster",
        $script:clusterName,
        "--tasks",
        $taskArn
    ) | Out-Null

    $exitCode = Get-TaskContainerExitCode -Cluster $script:clusterName -TaskArn $taskArn -ContainerName $ContainerName
    if ($exitCode -ne 0) {
        throw "The $ContainerName task exited $exitCode. Review CloudWatch logs in $script:logGroupName before retrying. The API service was not updated."
    }

    return $taskArn
}

if (-not (Get-Command terraform -ErrorAction SilentlyContinue)) {
    throw "Terraform must be available on PATH."
}

if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw "AWS CLI v2 must be available on PATH."
}

Push-Location $TerraformDirectory
try {
    $script:awsRegion = Invoke-TerraformOutput -Name "aws_region"
    $script:clusterName = Invoke-TerraformOutput -Name "api_cluster_name"
    $apiServiceName = Invoke-TerraformOutput -Name "api_service_name"
    $apiTaskDefinitionArn = Invoke-TerraformOutput -Name "api_task_definition_arn"
    $migrationTaskDefinitionArn = Invoke-TerraformOutput -Name "migration_task_definition_arn"
    $databaseVerifierTaskDefinitionArn = Invoke-TerraformOutput -Name "database_verifier_task_definition_arn"
    $apiUrl = Invoke-TerraformOutput -Name "api_url"
    $rdsMasterSecretArn = Invoke-TerraformOutput -Name "rds_master_user_secret_arn"
    $databaseConnectionSecretArn = Invoke-TerraformOutput -Name "database_connection_secret_arn"
    $rabbitMqConnectionSecretArn = Invoke-TerraformOutput -Name "rabbitmq_connection_secret_arn"
    $rabbitMqBrokerId = Invoke-TerraformOutput -Name "rabbitmq_broker_id"
    $rabbitMqEndpoint = Invoke-TerraformOutput -Name "rabbitmq_amqps_endpoint"
    $rabbitMqUsername = Invoke-TerraformOutput -Name "rabbitmq_username"
    $rabbitMqPassword = Invoke-TerraformOutput -Name "rabbitmq_password" -Sensitive
    $script:logGroupName = Invoke-TerraformOutput -Name "cloudwatch_log_group_name"
    $apiSecurityGroupId = Invoke-TerraformOutput -Name "api_security_group_id"
    $apiSubnetIds = Invoke-TerraformOutput -Name "api_subnet_ids" -Json | ConvertFrom-Json

    $script:networkConfiguration = "awsvpcConfiguration={subnets=[$($apiSubnetIds -join ',')],securityGroups=[$apiSecurityGroupId],assignPublicIp=ENABLED}"

    $rabbitMqBrokerState = (Invoke-Aws -Arguments @(
        "mq",
        "describe-broker",
        "--broker-id",
        $rabbitMqBrokerId,
        "--query",
        "BrokerState",
        "--output",
        "text"
    )).Trim()
    if ($rabbitMqBrokerState -ne "RUNNING") {
        throw "Amazon MQ broker state is $rabbitMqBrokerState, expected RUNNING. The API service was not updated."
    }

    $rdsMasterSecret = Invoke-Aws -Arguments @(
        "secretsmanager",
        "get-secret-value",
        "--secret-id",
        $rdsMasterSecretArn,
        "--query",
        "SecretString",
        "--output",
        "text"
    ) | ConvertFrom-Json

    $postgresConnectionString = "Host=$(ConvertTo-ConnectionStringValue $rdsMasterSecret.host);Port=$($rdsMasterSecret.port);Database=$(ConvertTo-ConnectionStringValue $rdsMasterSecret.dbname);Username=$(ConvertTo-ConnectionStringValue $rdsMasterSecret.username);Password=$(ConvertTo-ConnectionStringValue $rdsMasterSecret.password);Ssl Mode=Require;Trust Server Certificate=true"
    Put-ConnectionSecret -SecretArn $databaseConnectionSecretArn -ConnectionString $postgresConnectionString

    $rabbitMqUri = [System.Uri]$rabbitMqEndpoint
    $escapedRabbitMqUsername = [System.Uri]::EscapeDataString($rabbitMqUsername)
    $escapedRabbitMqPassword = [System.Uri]::EscapeDataString($rabbitMqPassword)
    $rabbitMqConnectionString = "amqps://$escapedRabbitMqUsername`:$escapedRabbitMqPassword@$($rabbitMqUri.Host):$($rabbitMqUri.Port)/"
    Put-ConnectionSecret -SecretArn $rabbitMqConnectionSecretArn -ConnectionString $rabbitMqConnectionString

    $migrationTaskArn = Invoke-OneShotTask -TaskDefinitionArn $migrationTaskDefinitionArn -ContainerName "pulseflow-migration"
    Write-Host "Migration task succeeded: $migrationTaskArn"

    Invoke-Aws -Arguments @(
        "ecs",
        "update-service",
        "--cluster",
        $script:clusterName,
        "--service",
        $apiServiceName,
        "--task-definition",
        $apiTaskDefinitionArn,
        "--desired-count",
        "1",
        "--output",
        "json"
    ) | Out-Null

    Invoke-Aws -Arguments @(
        "ecs",
        "wait",
        "services-stable",
        "--cluster",
        $script:clusterName,
        "--services",
        $apiServiceName
    ) | Out-Null

    foreach ($path in @("/health/live", "/health/ready")) {
        $response = Invoke-WebRequest -Uri "$apiUrl$path" -TimeoutSec 15 -SkipHttpErrorCheck
        if ($response.StatusCode -ne 200) {
            throw "$path returned HTTP $($response.StatusCode) after ECS reported the service stable."
        }
        Write-Host "$path returned HTTP 200."
    }

    $smokeEventId = [Guid]::NewGuid().ToString()
    $smokeSource = "pulseflow.aws.staging.smoke"
    $smokeEvent = [ordered]@{
        eventId    = $smokeEventId
        type       = "pulseflow.aws.staging.smoke"
        source     = $smokeSource
        occurredAt = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        payload    = @{ deployment = "aws-staging"; smokeTest = $true }
    } | ConvertTo-Json -Compress

    $ingestionResponse = Invoke-WebRequest -Uri "$apiUrl/api/events" -Method Post -ContentType "application/x-ndjson" -Body "$smokeEvent`n" -TimeoutSec 15 -SkipHttpErrorCheck
    if ($ingestionResponse.StatusCode -ne 202) {
        throw "The AWS ingestion smoke test returned HTTP $($ingestionResponse.StatusCode), expected 202."
    }
    Write-Host "Ingestion smoke test returned HTTP 202 for event $smokeEventId."

    $escapedSmokeSource = $smokeSource.Replace("'", "''")
    $verificationCommand = "set -eu; for attempt in `$(seq 1 30); do count=`$(psql -tAc `"SELECT count(*) FROM events WHERE source = '$escapedSmokeSource' AND event_id = '$smokeEventId'::uuid;`" | tr -d '[:space:]'); if [ `"`$count`" = '1' ]; then exit 0; fi; sleep 2; done; exit 1"
    $verifierOverrides = @{
        containerOverrides = @(
            @{
                name    = "database-verifier"
                command = @("sh", "-ec", $verificationCommand)
            }
        )
    } | ConvertTo-Json -Compress -Depth 5

    $verifierTaskArn = Invoke-OneShotTask -TaskDefinitionArn $databaseVerifierTaskDefinitionArn -ContainerName "database-verifier" -OverridesJson $verifierOverrides
    Write-Host "Database verification succeeded: $verifierTaskArn"

    $cleanupCommand = "set -eu; psql -v ON_ERROR_STOP=1 -c `"DELETE FROM events WHERE source = '$escapedSmokeSource' AND event_id = '$smokeEventId'::uuid;`""
    $cleanupOverrides = @{
        containerOverrides = @(
            @{
                name    = "database-verifier"
                command = @("sh", "-ec", $cleanupCommand)
            }
        )
    } | ConvertTo-Json -Compress -Depth 5

    $cleanupTaskArn = Invoke-OneShotTask -TaskDefinitionArn $databaseVerifierTaskDefinitionArn -ContainerName "database-verifier" -OverridesJson $cleanupOverrides
    Write-Host "Removed the exact smoke-test record: $cleanupTaskArn"
    Write-Host "AWS staging deployment completed successfully at $apiUrl"
}
finally {
    Pop-Location
}
