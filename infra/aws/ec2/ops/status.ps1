[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "instances.ps1")

function Get-SsmPingStatus {
    param(
        [Parameter(Mandatory)]
        [string]$AwsExecutable,
        [Parameter(Mandatory)]
        [string]$InstanceId
    )

    $statusOutput = @(
        & $AwsExecutable ssm describe-instance-information `
            --region $script:PulseFlowAwsRegion `
            --filters "Key=InstanceIds,Values=$InstanceId" `
            --query "InstanceInformationList[0].PingStatus" `
            --output text
    )

    if ($LASTEXITCODE -ne 0) {
        throw "AWS Systems Manager could not inspect instance '$InstanceId'."
    }

    $status = if ($statusOutput.Count -gt 0) { ([string]$statusOutput[0]).Trim() } else { "" }
    if ([string]::IsNullOrWhiteSpace($status) -or $status -eq "None") {
        return "Unmanaged"
    }

    return $status
}

function Get-RemoteHostStatus {
    param(
        [Parameter(Mandatory)]
        [string]$InstanceId,
        [Parameter(Mandatory)]
        [ValidateSet("app", "postgres", "rabbitmq", "redis")]
        [string]$Role
    )

    $remoteDirectory = "/opt/pulseflow/runtime/$Role"
    $commands = @(
        "set -euo pipefail",
        'if command -v docker >/dev/null 2>&1; then',
        '  if systemctl is-active --quiet docker; then echo "Docker=running"; else echo "Docker=installed-not-running"; fi',
        'else',
        '  echo "Docker=not-installed"',
        'fi',
        "if test -d `"$remoteDirectory`"; then echo `"RuntimeDirectory=present`"; else echo `"RuntimeDirectory=missing`"; fi"
    )

    $invocation = Invoke-PulseFlowSsmRunCommand `
        -InstanceId $InstanceId `
        -Commands $commands `
        -Comment "Inspect PulseFlow $Role host status"
    $values = @{}

    foreach ($line in ([string]$invocation.StandardOutputContent -split "`r?`n")) {
        $parts = $line -split "=", 2
        if ($parts.Count -eq 2) {
            $values[$parts[0]] = $parts[1]
        }
    }

    return [pscustomobject]@{
        Docker           = if ($values.ContainsKey("Docker")) { $values["Docker"] } else { "unknown" }
        RuntimeDirectory = if ($values.ContainsKey("RuntimeDirectory")) { $values["RuntimeDirectory"] } else { "unknown" }
    }
}

$awsExecutable = Get-PulseFlowAwsCli
$results = [System.Collections.Generic.List[object]]::new()
$inspectionFailed = $false

foreach ($role in $script:PulseFlowEc2Roles) {
    try {
        $instance = Resolve-PulseFlowEc2Instance -Role $role
        $ssmStatus = Get-SsmPingStatus -AwsExecutable $awsExecutable -InstanceId $instance.InstanceId
        $dockerStatus = "not-checked"
        $runtimeStatus = "not-checked"

        if ($ssmStatus -eq "Online") {
            try {
                $hostStatus = Get-RemoteHostStatus -InstanceId $instance.InstanceId -Role $role
                $dockerStatus = $hostStatus.Docker
                $runtimeStatus = $hostStatus.RuntimeDirectory
            }
            catch {
                $inspectionFailed = $true
                $dockerStatus = "inspection-failed"
                $runtimeStatus = "inspection-failed"
                Write-Warning "Could not inspect $role through SSM: $($_.Exception.Message)"
            }
        }

        $results.Add([pscustomobject]@{
                Role             = $role
                InstanceId       = $instance.InstanceId
                PrivateIp        = $instance.PrivateIpAddress
                Ssm              = $ssmStatus
                Docker           = $dockerStatus
                RuntimeDirectory = $runtimeStatus
            })
    }
    catch {
        $inspectionFailed = $true
        $results.Add([pscustomobject]@{
                Role             = $role
                InstanceId       = "discovery-failed"
                PrivateIp        = "-"
                Ssm              = "-"
                Docker           = "-"
                RuntimeDirectory = "-"
            })
        Write-Warning "Could not discover ${role}: $($_.Exception.Message)"
    }
}

$results | Format-Table -AutoSize

if ($inspectionFailed) {
    throw "One or more EC2 hosts could not be fully inspected. See the warnings above."
}
