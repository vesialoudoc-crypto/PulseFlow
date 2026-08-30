[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet("app", "rabbitmq", "redis", "postgres")]
    [string]$Role
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

$awsExecutable = Get-RequiredCommand -Name "aws" -InstallHint "Install AWS CLI v2 and configure an AWS credential context."
[void](Get-RequiredCommand -Name "session-manager-plugin" -InstallHint "Install the AWS Session Manager plugin for the AWS CLI.")

$nameTag = "pulseflow-$Role"
$describeOutput = & $awsExecutable ec2 describe-instances `
    --filters "Name=tag:Name,Values=$nameTag" "Name=instance-state-name,Values=running" `
    --query "Reservations[].Instances[].InstanceId" `
    --output json

if ($LASTEXITCODE -ne 0) {
    throw "AWS CLI could not find active instance information for role '$Role'. Verify the AWS region and credential context."
}

$instanceIds = @($describeOutput | ConvertFrom-Json)
if ($instanceIds.Count -eq 0) {
    throw "No active EC2 instance with Name tag '$nameTag' was found."
}

if ($instanceIds.Count -ne 1) {
    throw "Expected exactly one active EC2 instance with Name tag '$nameTag', but found $($instanceIds.Count)."
}

& $awsExecutable ssm start-session --target $instanceIds[0]
if ($LASTEXITCODE -ne 0) {
    throw "AWS Systems Manager Session Manager could not open a shell for '$nameTag'."
}
