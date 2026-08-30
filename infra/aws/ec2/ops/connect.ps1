[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet("app", "rabbitmq", "redis", "postgres")]
    [string]$Role
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$instancesScript = Join-Path $PSScriptRoot "instances.ps1"
. $instancesScript

$awsExecutable = Get-PulseFlowAwsCli
[void](Get-RequiredCommand -Name "session-manager-plugin" -InstallHint "Install the AWS Session Manager plugin for the AWS CLI.")

$instance = Resolve-PulseFlowEc2Instance -Role $Role

& $awsExecutable ssm start-session --region $script:PulseFlowAwsRegion --target $instance.InstanceId
if ($LASTEXITCODE -ne 0) {
    throw "AWS Systems Manager Session Manager could not open a shell for '$($instance.Name)'."
}
