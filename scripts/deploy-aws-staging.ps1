[CmdletBinding(DefaultParameterSetName = "Plan")]
param(
    [string]$TerraformDirectory = (Join-Path $PSScriptRoot "..\infra\aws"),
    [string]$AwsProfile,
    [Parameter(ParameterSetName = "Apply", Mandatory)]
    [switch]$Apply,
    [Parameter(ParameterSetName = "Deploy")]
    [switch]$Deploy,
    [Parameter(ParameterSetName = "Destroy")]
    [switch]$Destroy,
    [Parameter(ParameterSetName = "Destroy")]
    [switch]$DeleteBootstrapSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($DeleteBootstrapSecret -and -not $Destroy) {
    throw "-DeleteBootstrapSecret is valid only together with -Destroy."
}

. (Join-Path $PSScriptRoot "Import-PulseFlowDotEnv.ps1")
. (Join-Path $PSScriptRoot "Resolve-PulseFlowExecutable.ps1")
. (Join-Path $PSScriptRoot "Resolve-PulseFlowRdsConnectionMetadata.ps1")

$script:ghcrBootstrapSecretName = "pulseflow-staging/bootstrap/ghcr"
$script:requiredSavedPlanTerraformVersion = "1.15.8"

function Get-RequiredEnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name, [System.EnvironmentVariableTarget]::Process)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required environment variable '$Name' is missing or empty in the repository-root .env file."
    }

    return $value
}

function Get-GhcrImageRepository {
    param(
        [Parameter(Mandatory)]
        [string]$Image
    )

    $match = [regex]::Match($Image, "^ghcr\.io/(?<repository>[a-z0-9][a-z0-9._-]*/pulseflow-api):sha-[0-9a-f]{40}$")
    if (-not $match.Success) {
        throw "PULSEFLOW_IMAGE must be an immutable GHCR pulseflow-api image with a full lowercase SHA tag."
    }

    return $match.Groups["repository"].Value
}

function Invoke-GhcrImageVerification {
    param(
        [Parameter(Mandatory)]
        [string]$Image,
        [Parameter(Mandatory)]
        [string]$Username,
        [Parameter(Mandatory)]
        [string]$Token
    )

    $repository = Get-GhcrImageRepository -Image $Image
    $tokenEndpoint = "https://ghcr.io/token?service=ghcr.io&scope=$([System.Uri]::EscapeDataString("repository:$repository:pull"))"
    $basicCredential = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$Username`:$Token"))
    $client = [System.Net.Http.HttpClient]::new()

    try {
        $tokenRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $tokenEndpoint)
        $tokenRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Basic", $basicCredential)
        $tokenResponse = $client.SendAsync($tokenRequest).GetAwaiter().GetResult()
        try {
            if (-not $tokenResponse.IsSuccessStatusCode) {
                throw "Unable to verify PULSEFLOW_IMAGE against GHCR. Check GHCR_USERNAME, GHCR_TOKEN package-read access, and that the image is published."
            }

            $registryToken = (($tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).token)
            if ([string]::IsNullOrWhiteSpace($registryToken)) {
                throw "Unable to verify PULSEFLOW_IMAGE against GHCR. GHCR did not return a registry access token."
            }
        }
        finally {
            $tokenResponse.Dispose()
            $tokenRequest.Dispose()
        }

        $manifestRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Head, "https://ghcr.io/v2/$repository/manifests/$($Image.Split(':')[-1])")
        $manifestRequest.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $registryToken)
        foreach ($manifestMediaType in @(
            "application/vnd.oci.image.index.v1+json",
            "application/vnd.oci.image.manifest.v1+json",
            "application/vnd.docker.distribution.manifest.v2+json"
        )) {
            $manifestRequest.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new($manifestMediaType))
        }
        $manifestResponse = $client.SendAsync($manifestRequest).GetAwaiter().GetResult()
        try {
            if (-not $manifestResponse.IsSuccessStatusCode) {
                throw "Unable to verify PULSEFLOW_IMAGE against GHCR. Check GHCR_USERNAME, GHCR_TOKEN package-read access, and that the image is published."
            }
        }
        finally {
            $manifestResponse.Dispose()
            $manifestRequest.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

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

    $value = & $script:terraformExecutable @arguments
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
    $result = & $script:awsExecutable @awsArguments @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AWS CLI command failed."
    }

    return $result
}

function Set-GhcrRegistryCredentialSecret {
    param(
        [Parameter(Mandatory)]
        [string]$Username,
        [Parameter(Mandatory)]
        [string]$Token
    )

    $awsArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($AwsProfile)) {
        $awsArguments += @("--profile", $AwsProfile)
    }
    $awsArguments += @("--region", $script:awsRegion)

    $describeResult = & $script:awsExecutable @awsArguments @(
        "secretsmanager",
        "describe-secret",
        "--secret-id",
        $script:ghcrBootstrapSecretName,
        "--query",
        "ARN",
        "--output",
        "text"
    ) 2>&1

    $secretExists = $LASTEXITCODE -eq 0
    if ($secretExists) {
        $secretArn = ($describeResult | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($secretArn) -or $secretArn -eq "None") {
            throw "AWS returned an invalid ARN for the existing GHCR credential secret."
        }

        return $secretArn
    }
    elseif (($describeResult | Out-String) -notmatch "ResourceNotFoundException") {
        throw "Unable to identify the GHCR credential secret. Check AWS access before retrying."
    }

    $secretJson = [ordered]@{
        username = $Username
        password = $Token
    } | ConvertTo-Json -Compress
    $temporarySecretFile = New-TemporaryFile

    try {
        [System.IO.File]::WriteAllText(
            $temporarySecretFile.FullName,
            $secretJson,
            [System.Text.UTF8Encoding]::new($false)
        )
        $secretStringFileReference = "file://$($temporarySecretFile.FullName)"

        $secretArn = (Invoke-Aws -Arguments @(
            "secretsmanager",
            "create-secret",
            "--name",
            $script:ghcrBootstrapSecretName,
            "--description",
            "PulseFlow ECS private GHCR registry credential",
            "--secret-string",
            $secretStringFileReference,
            "--query",
            "ARN",
            "--output",
            "text"
        )).Trim()
    }
    finally {
        Remove-Item -LiteralPath $temporarySecretFile.FullName -Force -ErrorAction SilentlyContinue
        $secretJson = $null
    }

    if ([string]::IsNullOrWhiteSpace($secretArn) -or $secretArn -eq "None") {
        throw "AWS did not return a GHCR credential secret ARN."
    }

    return $secretArn
}

function Get-GhcrRegistryCredentialSecretArn {
    $awsArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($AwsProfile)) {
        $awsArguments += @("--profile", $AwsProfile)
    }
    $awsArguments += @("--region", $script:awsRegion)

    $describeResult = & $script:awsExecutable @awsArguments @(
        "secretsmanager",
        "describe-secret",
        "--secret-id",
        $script:ghcrBootstrapSecretName,
        "--query",
        "ARN",
        "--output",
        "text"
    ) 2>&1

    if ($LASTEXITCODE -ne 0) {
        if (($describeResult | Out-String) -match "ResourceNotFoundException") {
            throw "The external GHCR bootstrap secret '$script:ghcrBootstrapSecretName' was not found. Terraform destroy requires its existing ARN and will not recreate the secret."
        }

        throw "Unable to resolve the external GHCR bootstrap secret ARN. Check AWS access before retrying."
    }

    $secretArn = ($describeResult | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($secretArn) -or $secretArn -eq "None") {
        throw "AWS returned an invalid ARN for the external GHCR bootstrap secret."
    }

    return $secretArn
}

function Remove-GhcrRegistryCredentialSecret {
    param(
        [Parameter(Mandatory)]
        [string]$SecretArn
    )

    Invoke-Aws -Arguments @(
        "secretsmanager",
        "delete-secret",
        "--secret-id",
        $SecretArn,
        "--force-delete-without-recovery",
        "--output",
        "json"
    ) | Out-Null
}

function Confirm-AwsCallerIdentity {
    Invoke-Aws -Arguments @(
        "sts",
        "get-caller-identity",
        "--output",
        "json"
    ) | Out-Host
    Write-Host "AWS caller identity verified."
}

function Initialize-TerraformConfiguration {
    & $script:terraformExecutable init -input=false
    if ($LASTEXITCODE -ne 0) {
        throw "terraform init failed."
    }
}

function Invoke-AwsStagingPlan {
    param(
        [Parameter(Mandatory)]
        [string]$TerraformDirectory,
        [Parameter(Mandatory)]
        [string]$PulseFlowImage,
        [Parameter(Mandatory)]
        [string]$GhcrUsername,
        [Parameter(Mandatory)]
        [string]$GhcrToken
    )

    Confirm-AwsCallerIdentity

    $ghcrSecretArn = Set-GhcrRegistryCredentialSecret -Username $GhcrUsername -Token $GhcrToken
    Write-Host "GHCR credential secret is ready in AWS Secrets Manager."

    Invoke-GhcrImageVerification -Image $PulseFlowImage -Username $GhcrUsername -Token $GhcrToken
    Write-Host "PULSEFLOW_IMAGE is published and accessible through GHCR."

    Push-Location $TerraformDirectory
    try {
        & $script:terraformExecutable fmt -check -recursive
        if ($LASTEXITCODE -ne 0) {
            throw "terraform fmt -check -recursive failed."
        }

        Initialize-TerraformConfiguration

        & $script:terraformExecutable validate
        if ($LASTEXITCODE -ne 0) {
            throw "terraform validate failed."
        }

        $planArguments = @(
            "plan",
            "-input=false",
            "-out",
            "pulseflow-staging.tfplan",
            "-var",
            "aws_region=$script:awsRegion",
            "-var",
            "pulseflow_image=$PulseFlowImage",
            "-var",
            "ghcr_registry_credentials_secret_arn=$ghcrSecretArn"
        )
        if (-not [string]::IsNullOrWhiteSpace($AwsProfile)) {
            $planArguments += @("-var", "aws_profile=$AwsProfile")
        }

        & $script:terraformExecutable @planArguments
        if ($LASTEXITCODE -ne 0) {
            throw "terraform plan failed."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Terraform plan saved to infra/aws/pulseflow-staging.tfplan. Terraform apply was not run."
}

function Invoke-AwsStagingApply {
    param(
        [Parameter(Mandatory)]
        [string]$TerraformDirectory
    )

    Confirm-AwsCallerIdentity

    $savedPlanPath = Join-Path $TerraformDirectory "pulseflow-staging.tfplan"
    if (-not (Test-Path -LiteralPath $savedPlanPath -PathType Leaf)) {
        throw "Reviewed saved Terraform plan is missing: infra/aws/pulseflow-staging.tfplan. Run pwsh ./scripts/deploy-aws-staging.ps1, review the plan, and explicitly approve it before retrying -Apply."
    }

    Push-Location $TerraformDirectory
    try {
        Initialize-TerraformConfiguration

        & $script:terraformExecutable apply -input=false "pulseflow-staging.tfplan"
        if ($LASTEXITCODE -ne 0) {
            throw "Terraform could not apply the reviewed saved plan. Ensure infra/aws/pulseflow-staging.tfplan was created with the installed Terraform version and is still compatible. The script did not generate a replacement plan."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Terraform apply succeeded. AWS staging infrastructure exists, but PulseFlow.Api has not been deployed: the ECS service desired count remains zero. Run -Deploy explicitly when ready."
}

function Invoke-AwsStagingDestroy {
    param(
        [Parameter(Mandatory)]
        [string]$TerraformDirectory,
        [Parameter(Mandatory)]
        [string]$PulseFlowImage,
        [Parameter(Mandatory)]
        [switch]$DeleteBootstrapSecret
    )

    Confirm-AwsCallerIdentity
    $ghcrSecretArn = Get-GhcrRegistryCredentialSecretArn

    Push-Location $TerraformDirectory
    try {
        Initialize-TerraformConfiguration

        $destroyArguments = @(
            "destroy",
            "-input=false",
            "-auto-approve",
            "-var",
            "aws_region=$script:awsRegion",
            "-var",
            "pulseflow_image=$PulseFlowImage",
            "-var",
            "ghcr_registry_credentials_secret_arn=$ghcrSecretArn"
        )
        if (-not [string]::IsNullOrWhiteSpace($AwsProfile)) {
            $destroyArguments += @("-var", "aws_profile=$AwsProfile")
        }

        & $script:terraformExecutable @destroyArguments
        if ($LASTEXITCODE -ne 0) {
            throw "terraform destroy failed. Terraform-managed staging resources may remain; inspect Terraform output and state before retrying."
        }

        $remainingResources = @(& $script:terraformExecutable state list)
        if ($LASTEXITCODE -ne 0) {
            throw "terraform state list failed after destroy; unable to verify that no Terraform-managed resources remain."
        }

        if (($remainingResources | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
            throw "Terraform destroy completed, but Terraform state still reports managed resources. Inspect terraform state list before any further action."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Terraform destroy succeeded and Terraform reports no managed staging resources remaining."

    if ($DeleteBootstrapSecret) {
        Remove-GhcrRegistryCredentialSecret -SecretArn $ghcrSecretArn
        Write-Host "The external GHCR bootstrap credential secret was not Terraform-managed and was deleted because -DeleteBootstrapSecret was specified."
        return
    }

    Write-Host "The external GHCR bootstrap credential secret '$script:ghcrBootstrapSecretName' was not Terraform-managed and remains reusable."
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

function Get-RdsMasterCredentials {
    param(
        [Parameter(Mandatory)]
        [string]$MasterSecretArn
    )

    $masterSecretJson = Invoke-Aws -Arguments @(
        "secretsmanager",
        "get-secret-value",
        "--secret-id",
        $MasterSecretArn,
        "--query",
        "SecretString",
        "--output",
        "text"
    )

    try {
        $masterSecret = $masterSecretJson | ConvertFrom-Json
    }
    catch {
        throw "The RDS-managed master credential secret contains invalid JSON."
    }

    return [pscustomobject]@{
        Username = Get-PulseFlowRequiredStringProperty `
            -InputObject $masterSecret `
            -PropertyName "username" `
            -Description "RDS-managed master credential secret username"
        Password = Get-PulseFlowRequiredStringProperty `
            -InputObject $masterSecret `
            -PropertyName "password" `
            -Description "RDS-managed master credential secret password"
    }
}

function Get-RdsConnectionMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$MasterSecretArn
    )

    $describeDbInstancesJson = Invoke-Aws -Arguments @(
        "rds",
        "describe-db-instances",
        "--output",
        "json"
    )

    return Resolve-PulseFlowRdsConnectionMetadata `
        -MasterSecretArn $MasterSecretArn `
        -DescribeDbInstancesJson $describeDbInstancesJson
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotenvPath = Join-Path $repositoryRoot ".env"
Import-PulseFlowDotEnv -Path $dotenvPath

$requiredEnvironmentVariables = @{}
if ($PSCmdlet.ParameterSetName -eq "Plan") {
    $requiredVariableNames = @(
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_REGION",
        "AWS_DEFAULT_REGION",
        "GHCR_USERNAME",
        "GHCR_TOKEN",
        "PULSEFLOW_IMAGE"
    )
}
elseif ($PSCmdlet.ParameterSetName -eq "Destroy") {
    $requiredVariableNames = @(
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_REGION",
        "AWS_DEFAULT_REGION",
        "PULSEFLOW_IMAGE"
    )
}
else {
    $requiredVariableNames = @(
        "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY",
        "AWS_REGION",
        "AWS_DEFAULT_REGION"
    )
}

foreach ($variableName in $requiredVariableNames) {
    $requiredEnvironmentVariables[$variableName] = Get-RequiredEnvironmentVariable -Name $variableName
}

if ($requiredEnvironmentVariables["AWS_REGION"] -ne $requiredEnvironmentVariables["AWS_DEFAULT_REGION"]) {
    throw "AWS_REGION and AWS_DEFAULT_REGION in the repository-root .env file must match."
}

$script:awsRegion = $requiredEnvironmentVariables["AWS_REGION"]
if ($PSCmdlet.ParameterSetName -in @("Plan", "Destroy")) {
    Get-GhcrImageRepository -Image $requiredEnvironmentVariables["PULSEFLOW_IMAGE"] | Out-Null
}

$terraformResolution = Resolve-PulseFlowTerraformExecutable -PreferredVersion $script:requiredSavedPlanTerraformVersion
$script:terraformExecutable = $terraformResolution.Path
$script:terraformVersion = $terraformResolution.Version
if ([string]::IsNullOrWhiteSpace($script:terraformVersion)) {
    throw "Unable to determine the Terraform version from '$script:terraformExecutable'."
}

$awsResolution = Resolve-PulseFlowAwsCliExecutable
$script:awsExecutable = $awsResolution.Path
$script:awsCliVersion = $awsResolution.Version

if ($Apply -and $script:terraformVersion -ne $script:requiredSavedPlanTerraformVersion) {
    throw "The saved plan requires Terraform $script:requiredSavedPlanTerraformVersion, but '$script:terraformExecutable' reports $script:terraformVersion. -Apply stopped before terraform apply. Install or expose Terraform $script:requiredSavedPlanTerraformVersion; do not regenerate the reviewed plan automatically."
}

if ($PSCmdlet.ParameterSetName -eq "Plan") {
    Invoke-AwsStagingPlan `
        -TerraformDirectory $TerraformDirectory `
        -PulseFlowImage $requiredEnvironmentVariables["PULSEFLOW_IMAGE"] `
        -GhcrUsername $requiredEnvironmentVariables["GHCR_USERNAME"] `
        -GhcrToken $requiredEnvironmentVariables["GHCR_TOKEN"]
    return
}

if ($Apply) {
    Invoke-AwsStagingApply -TerraformDirectory $TerraformDirectory
    return
}

if ($Destroy) {
    Invoke-AwsStagingDestroy `
        -TerraformDirectory $TerraformDirectory `
        -PulseFlowImage $requiredEnvironmentVariables["PULSEFLOW_IMAGE"] `
        -DeleteBootstrapSecret:$DeleteBootstrapSecret
    return
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

    $rdsMasterCredentials = Get-RdsMasterCredentials -MasterSecretArn $rdsMasterSecretArn
    $rdsConnectionMetadata = Get-RdsConnectionMetadata -MasterSecretArn $rdsMasterSecretArn

    $postgresConnectionString = "Host=$(ConvertTo-ConnectionStringValue $rdsConnectionMetadata.Host);Port=$($rdsConnectionMetadata.Port);Database=$(ConvertTo-ConnectionStringValue $rdsConnectionMetadata.Database);Username=$(ConvertTo-ConnectionStringValue $rdsMasterCredentials.Username);Password=$(ConvertTo-ConnectionStringValue $rdsMasterCredentials.Password);Ssl Mode=Require;Trust Server Certificate=true"
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
