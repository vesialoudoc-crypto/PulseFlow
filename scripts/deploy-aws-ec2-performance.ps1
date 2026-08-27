[CmdletBinding(DefaultParameterSetName = "Plan")]
param(
    [string]$TerraformDirectory = (Join-Path $PSScriptRoot "..\infra\aws-ec2-performance"),
    [string]$AwsProfile,
    [Parameter(ParameterSetName = "Apply", Mandatory)]
    [switch]$Apply,
    [Parameter(ParameterSetName = "Deploy")]
    [switch]$Deploy,
    [Parameter(ParameterSetName = "Baseline")]
    [switch]$RunBaseline,
    [Parameter(ParameterSetName = "Baseline")]
    [ValidateRange(1, 100000)]
    [int]$VirtualUsers = 10,
    [Parameter(ParameterSetName = "Stop")]
    [switch]$Stop,
    [Parameter(ParameterSetName = "Destroy", Mandatory)]
    [switch]$Destroy,
    [Parameter(ParameterSetName = "Destroy")]
    [switch]$DeleteBootstrapSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Import-PulseFlowDotEnv.ps1")
. (Join-Path $PSScriptRoot "Get-PulseFlowActiveTaggedEc2InstanceIds.ps1")
. (Join-Path $PSScriptRoot "Resolve-PulseFlowExecutable.ps1")

# This secret already bootstraps the proven managed AWS environment. It contains only
# a read-only GHCR username/password JSON value and is intentionally reused instead
# of creating another persistent Secrets Manager charge for this disposable topology.
$script:ghcrBootstrapSecretName = "pulseflow-staging/bootstrap/ghcr"
$script:requiredSavedPlanTerraformVersion = "1.15.8"
$script:effectiveAwsProfile = $AwsProfile
$script:plannedAwsAccountId = $null
$script:plannedAwsRegion = $null

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

function Get-OptionalEnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name, [System.EnvironmentVariableTarget]::Process)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value.Trim()
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

function Invoke-Aws {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $awsArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
        $awsArguments += @("--profile", $script:effectiveAwsProfile)
    }

    $awsArguments += @("--region", $script:awsRegion)
    $result = & $script:awsExecutable @awsArguments @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AWS CLI command failed."
    }

    return ($result | Out-String).Trim()
}

function Invoke-TerraformOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
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

    return ($value | Out-String).Trim()
}

function Set-ApplyCredentialContextFromSavedPlan {
    $planPath = Join-Path $TerraformDirectory "pulseflow-ec2-performance.tfplan"
    if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
        throw "Saved plan '$planPath' was not found. Run this script without a lifecycle switch, review the plan, then run -Apply."
    }

    $terraformExecutable = (Resolve-PulseFlowTerraformExecutable -PreferredVersion $script:requiredSavedPlanTerraformVersion).Path
    Push-Location $TerraformDirectory
    try {
        $savedPlanJson = & $terraformExecutable show -json $planPath
        if ($LASTEXITCODE -ne 0) {
            throw "terraform show -json failed for saved plan '$planPath'."
        }
    }
    finally {
        Pop-Location
    }

    $savedPlan = ($savedPlanJson | Out-String) | ConvertFrom-Json
    $plannedProfileVariable = $savedPlan.variables.PSObject.Properties["aws_profile"]
    $plannedProfile = if ($null -eq $plannedProfileVariable) { $null } else { $plannedProfileVariable.Value.value }
    $plannedAccountOutput = $savedPlan.planned_values.outputs.PSObject.Properties["aws_account_id"]
    $plannedAccountId = if ($null -eq $plannedAccountOutput) { $null } else { $plannedAccountOutput.Value.value }
    $plannedRegionOutput = $savedPlan.planned_values.outputs.PSObject.Properties["aws_region"]
    $plannedRegion = if ($null -eq $plannedRegionOutput) { $null } else { $plannedRegionOutput.Value.value }

    if ([string]::IsNullOrWhiteSpace([string]$plannedAccountId)) {
        throw "Saved plan '$planPath' does not contain the planned AWS account ID. Create a complete plan before -Apply."
    }

    if ([string]::IsNullOrWhiteSpace([string]$plannedRegion)) {
        throw "Saved plan '$planPath' does not contain the planned AWS region. Create a complete plan before -Apply."
    }

    if ([string]::IsNullOrWhiteSpace([string]$plannedProfile)) {
        if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
            throw "Saved plan '$planPath' uses the default AWS credential chain, but -AwsProfile '$($script:effectiveAwsProfile)' was supplied. Recreate the plan with that profile instead."
        }
    }
    else {
        if (
            -not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile) `
            -and $script:effectiveAwsProfile -cne [string]$plannedProfile
        ) {
            throw "Saved plan '$planPath' was created with AWS profile '$plannedProfile', but -AwsProfile '$($script:effectiveAwsProfile)' was supplied."
        }

        $script:effectiveAwsProfile = [string]$plannedProfile
    }

    $script:plannedAwsAccountId = [string]$plannedAccountId
    $script:plannedAwsRegion = [string]$plannedRegion
}

function Assert-TerraformStateAccountMatchesCurrent {
    Push-Location $TerraformDirectory
    try {
        $stateAccountId = Invoke-TerraformOutput -Name "aws_account_id"
        $stateRegion = Invoke-TerraformOutput -Name "aws_region"
    }
    finally {
        Pop-Location
    }

    if ([string]::IsNullOrWhiteSpace($stateAccountId)) {
        throw "Terraform state does not contain aws_account_id. Refuse to run this lifecycle command without a state account guard."
    }

    if ($stateAccountId -cne $script:awsAccountId) {
        throw "Terraform state targets AWS account '$stateAccountId', but the active AWS CLI/Terraform credential context resolves to '$script:awsAccountId'."
    }

    if ([string]::IsNullOrWhiteSpace($stateRegion)) {
        throw "Terraform state does not contain aws_region. Refuse to run this lifecycle command without a state region guard."
    }

    if ($stateRegion -cne $script:awsRegion) {
        throw "Terraform state targets AWS region '$stateRegion', but AWS_REGION resolves to '$script:awsRegion'."
    }
}

function Set-GhcrRegistryCredentialSecret {
    param(
        [Parameter(Mandatory)]
        [string]$Username,
        [Parameter(Mandatory)]
        [string]$Token
    )

    $awsArguments = @()
    if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
        $awsArguments += @("--profile", $script:effectiveAwsProfile)
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

    if ($LASTEXITCODE -eq 0) {
        $secretArn = ($describeResult | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($secretArn) -or $secretArn -eq "None") {
            throw "AWS returned an invalid ARN for the existing GHCR credential secret."
        }

        $existingSecretJson = Invoke-Aws -Arguments @(
            "secretsmanager",
            "get-secret-value",
            "--secret-id",
            $secretArn,
            "--query",
            "SecretString",
            "--output",
            "text"
        )

        try {
            $existingCredential = $existingSecretJson | ConvertFrom-Json
            $credentialMatches = (
                $existingCredential.username -ceq $Username `
                -and $existingCredential.password -ceq $Token
            )
        }
        catch {
            $credentialMatches = $false
        }

        if ($credentialMatches) {
            return $secretArn
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

            Invoke-Aws -Arguments @(
                "secretsmanager",
                "put-secret-value",
                "--secret-id",
                $secretArn,
                "--secret-string",
                "file://$($temporarySecretFile.FullName)"
            ) | Out-Null
        }
        finally {
            Remove-Item -LiteralPath $temporarySecretFile.FullName -Force -ErrorAction SilentlyContinue
            $secretJson = $null
            $existingSecretJson = $null
        }

        return $secretArn
    }

    if (($describeResult | Out-String) -notmatch "ResourceNotFoundException") {
        throw "Unable to identify the shared GHCR credential secret. Check AWS access before retrying."
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

        $secretArn = (Invoke-Aws -Arguments @(
                "secretsmanager",
                "create-secret",
                "--name",
                $script:ghcrBootstrapSecretName,
                "--description",
                "PulseFlow shared private GHCR registry credential",
                "--secret-string",
                "file://$($temporarySecretFile.FullName)",
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
    $secretArn = (Invoke-Aws -Arguments @(
            "secretsmanager",
            "describe-secret",
            "--secret-id",
            $script:ghcrBootstrapSecretName,
            "--query",
            "ARN",
            "--output",
            "text"
        )).Trim()

    if ([string]::IsNullOrWhiteSpace($secretArn) -or $secretArn -eq "None") {
        throw "AWS returned an invalid ARN for the shared GHCR credential secret."
    }

    return $secretArn
}

function Get-NodeIds {
    $nodeIdsJson = Invoke-TerraformOutput -Name "node_instance_ids" -Json
    $nodeIds = $nodeIdsJson | ConvertFrom-Json

    foreach ($role in @("app", "rabbitmq", "redis", "postgres", "loadgen")) {
        if ([string]::IsNullOrWhiteSpace([string]$nodeIds.$role)) {
            throw "Terraform output node_instance_ids did not contain the required '$role' node."
        }
    }

    return $nodeIds
}

function Get-ExistingEnvironmentNodeIds {
    $instancesJson = Invoke-Aws -Arguments @(
        "ec2",
        "describe-instances",
        "--filters",
        "Name=tag:Project,Values=PulseFlow",
        "Name=tag:Environment,Values=performance",
        "Name=tag:ManagedBy,Values=Terraform",
        "--output",
        "json"
    )

    return @(Get-PulseFlowActiveTaggedEc2InstanceIds -DescribeInstancesJson $instancesJson)
}

function Invoke-SsmShellCommand {
    param(
        [Parameter(Mandatory)]
        [string]$InstanceId,
        [Parameter(Mandatory)]
        [string]$Command,
        [ValidateRange(30, 2592000)]
        [int]$TimeoutSeconds = 900
    )

    $parametersFile = New-TemporaryFile
    try {
        $parametersJson = @{ commands = @($Command) } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText(
            $parametersFile.FullName,
            $parametersJson,
            [System.Text.UTF8Encoding]::new($false)
        )

        $commandId = (Invoke-Aws -Arguments @(
                "ssm",
                "send-command",
                "--document-name",
                "AWS-RunShellScript",
                "--instance-ids",
                $InstanceId,
                "--parameters",
                "file://$($parametersFile.FullName)",
                "--timeout-seconds",
                $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
                "--query",
                "Command.CommandId",
                "--output",
                "text"
            )).Trim()
    }
    finally {
        Remove-Item -LiteralPath $parametersFile.FullName -Force -ErrorAction SilentlyContinue
    }

    if ([string]::IsNullOrWhiteSpace($commandId) -or $commandId -eq "None") {
        throw "AWS Systems Manager did not return a command ID for node '$InstanceId'."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds + 60)
    while ([DateTime]::UtcNow -lt $deadline) {
        $awsArguments = @()
        if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
            $awsArguments += @("--profile", $script:effectiveAwsProfile)
        }
        $awsArguments += @("--region", $script:awsRegion)

        $invocationJson = & $script:awsExecutable @awsArguments @(
            "ssm",
            "get-command-invocation",
            "--command-id",
            $commandId,
            "--instance-id",
            $InstanceId,
            "--output",
            "json"
        ) 2>&1

        if ($LASTEXITCODE -ne 0) {
            if (($invocationJson | Out-String) -match "InvocationDoesNotExist") {
                Start-Sleep -Seconds 2
                continue
            }

            throw "Unable to read AWS Systems Manager command invocation '$commandId' for node '$InstanceId'."
        }

        $invocation = ($invocationJson | Out-String) | ConvertFrom-Json
        switch ([string]$invocation.Status) {
            "Success" {
                $standardOutput = [string]$invocation.StandardOutputContent
                if (-not [string]::IsNullOrWhiteSpace($standardOutput)) {
                    Write-Host $standardOutput.TrimEnd()
                }

                return
            }
            { $_ -in @("Failed", "Cancelled", "TimedOut", "Cancelling") } {
                $errorOutput = [string]$invocation.StandardErrorContent
                throw "AWS Systems Manager command '$commandId' failed on node '$InstanceId' with status '$($invocation.Status)': $errorOutput"
            }
            default {
                Start-Sleep -Seconds 2
            }
        }
    }

    throw "Timed out while waiting for AWS Systems Manager command '$commandId' on node '$InstanceId'."
}

function Wait-ForNodeBootstrap {
    param(
        [Parameter(Mandatory)]
        [string]$InstanceId
    )

    $agentDeadline = [DateTime]::UtcNow.AddMinutes(15)
    while ([DateTime]::UtcNow -lt $agentDeadline) {
        $pingStatus = Invoke-Aws -Arguments @(
            "ssm",
            "describe-instance-information",
            "--filters",
            "Key=InstanceIds,Values=$InstanceId",
            "--query",
            "InstanceInformationList[0].PingStatus",
            "--output",
            "text"
        )

        if ($pingStatus -eq "Online") {
            break
        }

        Start-Sleep -Seconds 5
    }

    if ([DateTime]::UtcNow -ge $agentDeadline) {
        throw "AWS Systems Manager agent did not become online for node '$InstanceId' within 15 minutes."
    }

    Invoke-SsmShellCommand -InstanceId $InstanceId -TimeoutSeconds 900 -Command @'
for attempt in $(seq 1 180); do
  if test -f /opt/pulseflow/.bootstrap-complete && docker compose version >/dev/null 2>&1; then
    exit 0
  fi
  sleep 5
done
echo "PulseFlow bootstrap did not complete within 15 minutes." >&2
exit 1
'@
}

function Wait-ForNodesBootstrap {
    param(
        [Parameter(Mandatory)]
        [object]$NodeIds
    )

    foreach ($role in @("app", "rabbitmq", "redis", "postgres", "loadgen")) {
        Write-Host "Waiting for $role node bootstrap..."
        Wait-ForNodeBootstrap -InstanceId $NodeIds.$role
    }
}

function Start-Nodes {
    param(
        [Parameter(Mandatory)]
        [object]$NodeIds
    )

    $instanceIds = @($NodeIds.app, $NodeIds.rabbitmq, $NodeIds.redis, $NodeIds.postgres, $NodeIds.loadgen)
    $instancesJson = Invoke-Aws -Arguments (@("ec2", "describe-instances", "--instance-ids") + $instanceIds + @("--output", "json"))
    $instances = (($instancesJson | ConvertFrom-Json).Reservations | ForEach-Object { $_.Instances })
    $stoppedInstanceIds = @($instances | Where-Object { $_.State.Name -eq "stopped" } | ForEach-Object { $_.InstanceId })

    if ($stoppedInstanceIds.Count -ne 0) {
        Invoke-Aws -Arguments (@("ec2", "start-instances", "--instance-ids") + $stoppedInstanceIds) | Out-Null
    }

    Invoke-Aws -Arguments (@("ec2", "wait", "instance-running", "--instance-ids") + $instanceIds) | Out-Null
    Wait-ForNodesBootstrap -NodeIds $NodeIds
}

function Stop-Nodes {
    param(
        [Parameter(Mandatory)]
        [object]$NodeIds
    )

    $instanceIds = @($NodeIds.app, $NodeIds.rabbitmq, $NodeIds.redis, $NodeIds.postgres, $NodeIds.loadgen)
    Stop-InstanceIds -InstanceIds $instanceIds
}

function Stop-ExistingEnvironmentNodes {
    $instanceIds = Get-ExistingEnvironmentNodeIds
    if ($instanceIds.Count -eq 0) {
        Write-Warning "No active EC2 instances matched the exact PulseFlow performance Terraform tags after apply/bootstrap failure."
        return
    }

    Write-Warning "Terraform node outputs were unavailable or incomplete. Attempting to stop $($instanceIds.Count) EC2 instance(s) discovered by the exact PulseFlow performance Terraform tags."
    Stop-InstanceIds -InstanceIds $instanceIds
}

function Stop-InstanceIds {
    param(
        [Parameter(Mandatory)]
        [string[]]$InstanceIds
    )

    if ($InstanceIds.Count -eq 0) {
        return
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(15)

    while ([DateTime]::UtcNow -lt $deadline) {
        $instancesJson = Invoke-Aws -Arguments (@("ec2", "describe-instances", "--instance-ids") + $instanceIds + @("--output", "json"))
        $instances = @(($instancesJson | ConvertFrom-Json).Reservations | ForEach-Object { $_.Instances })

        if ($instances.Count -ne $InstanceIds.Count) {
            throw "Expected $($InstanceIds.Count) EC2 performance nodes while stopping, but AWS returned $($instances.Count)."
        }

        $nonStoppedInstances = @($instances | Where-Object { $_.State.Name -ne "stopped" })
        if ($nonStoppedInstances.Count -eq 0) {
            Write-Host "All EC2 performance nodes are stopped."
            return
        }

        $runningInstanceIds = @($nonStoppedInstances | Where-Object { $_.State.Name -eq "running" } | ForEach-Object { $_.InstanceId })
        if ($runningInstanceIds.Count -ne 0) {
            Invoke-Aws -Arguments (@("ec2", "stop-instances", "--instance-ids") + $runningInstanceIds) | Out-Null
            continue
        }

        $pendingInstanceIds = @($nonStoppedInstances | Where-Object { $_.State.Name -eq "pending" } | ForEach-Object { $_.InstanceId })
        if ($pendingInstanceIds.Count -ne 0) {
            Invoke-Aws -Arguments (@("ec2", "wait", "instance-running", "--instance-ids") + $pendingInstanceIds) | Out-Null
            continue
        }

        $stoppingInstanceIds = @($nonStoppedInstances | Where-Object { $_.State.Name -eq "stopping" } | ForEach-Object { $_.InstanceId })
        if ($stoppingInstanceIds.Count -ne 0) {
            Invoke-Aws -Arguments (@("ec2", "wait", "instance-stopped", "--instance-ids") + $stoppingInstanceIds) | Out-Null
            continue
        }

        $stateSummary = $nonStoppedInstances | ForEach-Object { "$($_.InstanceId)=$($_.State.Name)" }
        throw "Cannot stop EC2 performance nodes in unexpected states: $($stateSummary -join ', ')."
    }

    throw "Timed out while waiting for the requested EC2 performance nodes to stop."
}

function Initialize-OperatorPrerequisites {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    Import-PulseFlowDotEnv -Path (Join-Path $repositoryRoot ".env")

    $script:awsRegion = Get-RequiredEnvironmentVariable -Name "AWS_REGION"
    $awsDefaultRegion = Get-RequiredEnvironmentVariable -Name "AWS_DEFAULT_REGION"
    if ($awsDefaultRegion -ne $script:awsRegion) {
        throw "AWS_DEFAULT_REGION must equal AWS_REGION for this lifecycle."
    }

    if (
        -not [string]::IsNullOrWhiteSpace($script:plannedAwsRegion) `
        -and $script:awsRegion -cne $script:plannedAwsRegion
    ) {
        throw "Saved plan targets AWS region '$($script:plannedAwsRegion)', but AWS_REGION resolves to '$script:awsRegion'."
    }

    $script:terraformExecutable = (Resolve-PulseFlowTerraformExecutable -PreferredVersion $script:requiredSavedPlanTerraformVersion).Path
    $script:awsExecutable = (Resolve-PulseFlowAwsCliExecutable).Path

    $callerIdentity = (Invoke-Aws -Arguments @("sts", "get-caller-identity", "--output", "json")) | ConvertFrom-Json
    $script:awsAccountId = [string]$callerIdentity.Account
    if ([string]::IsNullOrWhiteSpace($script:awsAccountId)) {
        throw "AWS STS did not return an account ID."
    }

    if (
        -not [string]::IsNullOrWhiteSpace($script:plannedAwsAccountId) `
        -and $script:awsAccountId -cne $script:plannedAwsAccountId
    ) {
        throw "Saved plan targets AWS account '$($script:plannedAwsAccountId)', but the active AWS CLI/Terraform credential context resolves to '$script:awsAccountId'."
    }
}

function Invoke-Plan {
    $ghcrUsername = Get-RequiredEnvironmentVariable -Name "GHCR_USERNAME"
    $ghcrToken = Get-RequiredEnvironmentVariable -Name "GHCR_TOKEN"
    $pulseFlowImage = Get-RequiredEnvironmentVariable -Name "PULSEFLOW_IMAGE"
    $amiId = Get-OptionalEnvironmentVariable -Name "PULSEFLOW_EC2_AMI_ID"
    Invoke-GhcrImageVerification -Image $pulseFlowImage -Username $ghcrUsername -Token $ghcrToken
    $ghcrSecretArn = Set-GhcrRegistryCredentialSecret -Username $ghcrUsername -Token $ghcrToken

    $planPath = Join-Path $TerraformDirectory "pulseflow-ec2-performance.tfplan"
    $planArguments = [System.Collections.Generic.List[string]]::new()
    $planArguments.Add("plan")
    $planArguments.Add("-out=$planPath")
    $planArguments.Add("-var")
    $planArguments.Add("aws_region=$script:awsRegion")
    $planArguments.Add("-var")
    $planArguments.Add("pulseflow_image=$pulseFlowImage")
    $planArguments.Add("-var")
    $planArguments.Add("ghcr_registry_credentials_secret_arn=$ghcrSecretArn")

    if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
        $planArguments.Add("-var")
        $planArguments.Add("aws_profile=$script:effectiveAwsProfile")
    }

    if ($null -ne $amiId) {
        $planArguments.Add("-var")
        $planArguments.Add("ami_id=$amiId")
    }

    Push-Location $TerraformDirectory
    try {
        & $script:terraformExecutable fmt -check -recursive
        if ($LASTEXITCODE -ne 0) {
            throw "terraform fmt -check -recursive failed."
        }

        & $script:terraformExecutable init -input=false
        if ($LASTEXITCODE -ne 0) {
            throw "terraform init failed."
        }

        & $script:terraformExecutable validate
        if ($LASTEXITCODE -ne 0) {
            throw "terraform validate failed."
        }

        & $script:terraformExecutable @planArguments
        if ($LASTEXITCODE -ne 0) {
            throw "terraform plan failed."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Saved EC2 performance plan to '$planPath'. Review its paid resources before -Apply."
}

function Invoke-Apply {
    $pulseFlowImage = Get-RequiredEnvironmentVariable -Name "PULSEFLOW_IMAGE"
    $ghcrSecretArn = Get-GhcrRegistryCredentialSecretArn
    $planPath = Join-Path $TerraformDirectory "pulseflow-ec2-performance.tfplan"

    if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
        throw "Saved plan '$planPath' was not found. Run this script without a lifecycle switch, review the plan, then run -Apply."
    }

    $nodeIds = $null

    Push-Location $TerraformDirectory
    try {
        & $script:terraformExecutable apply $planPath
        if ($LASTEXITCODE -ne 0) {
            throw "terraform apply failed."
        }

        $nodeIds = Get-NodeIds
        Wait-ForNodesBootstrap -NodeIds $nodeIds
        Stop-Nodes -NodeIds $nodeIds
    }
    catch {
        Write-Warning "Apply or bootstrap failed. Attempting an emergency stop before rethrowing the failure."
        try {
            if ($null -ne $nodeIds) {
                Stop-Nodes -NodeIds $nodeIds
            }
            else {
                Stop-ExistingEnvironmentNodes
            }
        }
        catch {
            Write-Warning "Automatic EC2 stop after apply/bootstrap failure also failed: $($_.Exception.Message)"
        }

        throw
    }
    finally {
        Pop-Location
    }

    Write-Host "Infrastructure bootstrap completed and all nodes were stopped. Use -Deploy for the migration-first runtime proof."
}

function Invoke-Deploy {
    $nodeIds = $null

    Push-Location $TerraformDirectory
    try {
        $nodeIds = Get-NodeIds
        Start-Nodes -NodeIds $nodeIds

        Invoke-SsmShellCommand -InstanceId $nodeIds.postgres -Command "/opt/pulseflow/start-postgres.sh"
        Invoke-SsmShellCommand -InstanceId $nodeIds.rabbitmq -Command "/opt/pulseflow/start-rabbitmq.sh"
        Invoke-SsmShellCommand -InstanceId $nodeIds.redis -Command "/opt/pulseflow/start-redis.sh"
        Invoke-SsmShellCommand -InstanceId $nodeIds.app -Command "/opt/pulseflow/start-app.sh"
        Invoke-SsmShellCommand -InstanceId $nodeIds.loadgen -Command "/opt/pulseflow/wait-for-ready.sh"

        $smokeEventId = [Guid]::NewGuid().ToString()
        Invoke-SsmShellCommand -InstanceId $nodeIds.loadgen -Command "/opt/pulseflow/run-smoke.sh $smokeEventId"
        Invoke-SsmShellCommand -InstanceId $nodeIds.postgres -Command "/opt/pulseflow/wait-for-smoke-event.sh $smokeEventId"
        Invoke-SsmShellCommand -InstanceId $nodeIds.postgres -Command "/opt/pulseflow/cleanup-smoke-event.sh $smokeEventId"

        Write-Host "EC2 smoke event '$smokeEventId' was accepted, persisted, and removed."
    }
    catch {
        if ($null -ne $nodeIds) {
            Write-Warning "Deployment proof failed. Attempting to stop all EC2 performance nodes before rethrowing the failure."
            try {
                Stop-Nodes -NodeIds $nodeIds
            }
            catch {
                Write-Warning "Automatic EC2 stop after deployment failure also failed: $($_.Exception.Message)"
            }
        }

        throw
    }
    finally {
        Pop-Location
    }

    Write-Host "EC2 performance runtime proof completed. Nodes remain running until the weekday stop schedule or an explicit -Stop."
}

function Invoke-Baseline {
    Push-Location $TerraformDirectory
    try {
        $nodeIds = Get-NodeIds
        Start-Nodes -NodeIds $nodeIds
        Invoke-SsmShellCommand -InstanceId $nodeIds.loadgen -Command "/opt/pulseflow/wait-for-ready.sh"
        Invoke-SsmShellCommand -InstanceId $nodeIds.loadgen -Command "/opt/pulseflow/run-baseline.sh $VirtualUsers"
    }
    finally {
        Pop-Location
    }
}

function Invoke-Destroy {
    $pulseFlowImage = Get-RequiredEnvironmentVariable -Name "PULSEFLOW_IMAGE"
    $amiId = Get-OptionalEnvironmentVariable -Name "PULSEFLOW_EC2_AMI_ID"
    $ghcrSecretArn = Get-GhcrRegistryCredentialSecretArn
    $destroyArguments = [System.Collections.Generic.List[string]]::new()
    $destroyArguments.Add("destroy")
    $destroyArguments.Add("-auto-approve")
    $destroyArguments.Add("-var")
    $destroyArguments.Add("aws_region=$script:awsRegion")
    $destroyArguments.Add("-var")
    $destroyArguments.Add("pulseflow_image=$pulseFlowImage")
    $destroyArguments.Add("-var")
    $destroyArguments.Add("ghcr_registry_credentials_secret_arn=$ghcrSecretArn")

    if (-not [string]::IsNullOrWhiteSpace($script:effectiveAwsProfile)) {
        $destroyArguments.Add("-var")
        $destroyArguments.Add("aws_profile=$script:effectiveAwsProfile")
    }

    if ($null -ne $amiId) {
        $destroyArguments.Add("-var")
        $destroyArguments.Add("ami_id=$amiId")
    }

    Push-Location $TerraformDirectory
    try {
        & $script:terraformExecutable @destroyArguments
        if ($LASTEXITCODE -ne 0) {
            throw "terraform destroy failed."
        }

        $stateLines = @(& $script:terraformExecutable state list | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($LASTEXITCODE -ne 0) {
            throw "terraform state list failed after destroy."
        }

        if ($stateLines.Count -ne 0) {
            throw "Terraform destroy completed but state still contains: $($stateLines -join ', ')"
        }
    }
    finally {
        Pop-Location
    }

    if ($DeleteBootstrapSecret) {
        Write-Warning "Deleting the shared GHCR bootstrap secret also prevents the managed AWS staging environment from pulling private images until it is recreated."
        Invoke-Aws -Arguments @(
            "secretsmanager",
            "delete-secret",
            "--secret-id",
            $script:ghcrBootstrapSecretName,
            "--force-delete-without-recovery"
        ) | Out-Null
    }
}

if ($PSCmdlet.ParameterSetName -eq "Apply") {
    Set-ApplyCredentialContextFromSavedPlan
}

Initialize-OperatorPrerequisites

if ($PSCmdlet.ParameterSetName -in @("Deploy", "Baseline", "Stop", "Destroy")) {
    Assert-TerraformStateAccountMatchesCurrent
}

switch ($PSCmdlet.ParameterSetName) {
    "Plan" {
        Invoke-Plan
    }
    "Apply" {
        Invoke-Apply
    }
    "Deploy" {
        Invoke-Deploy
    }
    "Baseline" {
        Invoke-Baseline
    }
    "Stop" {
        Push-Location $TerraformDirectory
        try {
            Stop-Nodes -NodeIds (Get-NodeIds)
        }
        finally {
            Pop-Location
        }
    }
    "Destroy" {
        Invoke-Destroy
    }
    default {
        throw "Unsupported lifecycle mode '$($PSCmdlet.ParameterSetName)'."
    }
}
