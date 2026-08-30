[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PulseFlowAwsRegion = "eu-central-1"
$script:PulseFlowEc2Roles = @("app", "postgres", "rabbitmq", "redis")

function Get-RequiredCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$InstallHint
    )

    $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$Name is required. $InstallHint"
    }

    return $command.Source
}

function Get-PulseFlowAwsCli {
    $awsExecutable = Get-RequiredCommand -Name "aws" -InstallHint "Install AWS CLI v2 and configure an AWS credential context."
    $versionOutput = @(& $awsExecutable --version 2>&1)

    if ($LASTEXITCODE -ne 0 -or $versionOutput.Count -eq 0 -or $versionOutput[0] -notmatch "^aws-cli/2\.") {
        throw "AWS CLI v2 is required. Install AWS CLI v2 and configure an AWS credential context."
    }

    return $awsExecutable
}

function Resolve-PulseFlowEc2Instance {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("app", "postgres", "rabbitmq", "redis")]
        [string]$Role
    )

    $awsExecutable = Get-PulseFlowAwsCli
    $nameTag = "pulseflow-$Role"
    $query = 'Reservations[].Instances[].{InstanceId:InstanceId,PrivateIpAddress:PrivateIpAddress,Name:Tags[?Key==`Name`]|[0].Value}'
    $describeOutput = @(
        & $awsExecutable ec2 describe-instances `
            --region $script:PulseFlowAwsRegion `
            --filters "Name=tag:Name,Values=$nameTag" "Name=instance-state-name,Values=running" `
            --query $query `
            --output json
    )

    if ($LASTEXITCODE -ne 0) {
        throw "AWS CLI could not discover the running EC2 instance for role '$Role' in region '$script:PulseFlowAwsRegion'. Verify the AWS credential context and region access."
    }

    try {
        $instances = @($describeOutput | ConvertFrom-Json)
    }
    catch {
        throw "AWS CLI returned invalid instance data for role '$Role': $($_.Exception.Message)"
    }

    if ($instances.Count -eq 0) {
        throw "No running EC2 instance with Name tag '$nameTag' was found in region '$script:PulseFlowAwsRegion'."
    }

    if ($instances.Count -ne 1) {
        throw "Expected exactly one running EC2 instance with Name tag '$nameTag' in region '$script:PulseFlowAwsRegion', but found $($instances.Count)."
    }

    $instance = $instances[0]
    if ([string]::IsNullOrWhiteSpace($instance.PrivateIpAddress)) {
        throw "Running EC2 instance '$($instance.InstanceId)' for role '$Role' has no private IP address."
    }

    [pscustomobject]@{
        Role             = $Role
        InstanceId       = $instance.InstanceId
        PrivateIpAddress = $instance.PrivateIpAddress
        Name             = $instance.Name
    }
}

function Invoke-PulseFlowSsmRunCommand {
    param(
        [Parameter(Mandatory)]
        [string]$InstanceId,
        [Parameter(Mandatory)]
        [string[]]$Commands,
        [Parameter(Mandatory)]
        [string]$Comment
    )

    $awsExecutable = Get-PulseFlowAwsCli
    $parameters = @{ commands = @($Commands) } | ConvertTo-Json -Compress
    $commandIdOutput = @(
        & $awsExecutable ssm send-command `
            --region $script:PulseFlowAwsRegion `
            --document-name "AWS-RunShellScript" `
            --instance-ids $InstanceId `
            --parameters $parameters `
            --comment $Comment `
            --query "Command.CommandId" `
            --output text
    )

    if ($LASTEXITCODE -ne 0 -or $commandIdOutput.Count -eq 0) {
        throw "AWS Systems Manager could not send '$Comment' to instance '$InstanceId'."
    }

    $commandId = ([string]$commandIdOutput[0]).Trim()
    if ([string]::IsNullOrWhiteSpace($commandId) -or $commandId -eq "None") {
        throw "AWS Systems Manager did not return a command ID for '$Comment' on instance '$InstanceId'."
    }

    & $awsExecutable ssm wait command-executed `
        --region $script:PulseFlowAwsRegion `
        --command-id $commandId `
        --instance-id $InstanceId
    $waitExitCode = $LASTEXITCODE

    $invocationOutput = @(
        & $awsExecutable ssm get-command-invocation `
            --region $script:PulseFlowAwsRegion `
            --command-id $commandId `
            --instance-id $InstanceId `
            --output json
    )

    if ($LASTEXITCODE -ne 0) {
        throw "AWS Systems Manager could not read the result of command '$commandId' on instance '$InstanceId'."
    }

    try {
        $invocation = $invocationOutput | ConvertFrom-Json
    }
    catch {
        throw "AWS Systems Manager returned an invalid result for command '$commandId' on instance '$InstanceId': $($_.Exception.Message)"
    }

    if ($waitExitCode -ne 0 -or $invocation.Status -ne "Success") {
        $errorText = ([string]$invocation.StandardErrorContent).Trim()
        if ([string]::IsNullOrWhiteSpace($errorText)) {
            $errorText = "No remote error output was returned."
        }

        throw "AWS Systems Manager command '$commandId' on instance '$InstanceId' finished with status '$($invocation.Status)': $errorText"
    }

    return $invocation
}
