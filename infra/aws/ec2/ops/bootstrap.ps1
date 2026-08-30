[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet("app", "postgres", "rabbitmq", "redis", "all")]
    [string]$Role
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "instances.ps1")

function Get-SelectedRoles {
    param(
        [Parameter(Mandatory)]
        [string]$RequestedRole
    )

    if ($RequestedRole -eq "all") {
        return $script:PulseFlowEc2Roles
    }

    return @($RequestedRole)
}

$installerPath = Join-Path $PSScriptRoot "install-docker.sh"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Docker installer was not found: $installerPath"
}

$installerBytes = [System.IO.File]::ReadAllBytes($installerPath)
$installerBase64 = [Convert]::ToBase64String($installerBytes)
$commands = @(
    "set -euo pipefail",
    'bootstrap_path="$(mktemp /tmp/pulseflow-install-docker.XXXXXX)"',
    'trap ''rm -f "$bootstrap_path"'' EXIT',
    "printf '%s' '$installerBase64' | base64 --decode > `"`$bootstrap_path`"",
    'chmod 700 "$bootstrap_path"',
    'bash "$bootstrap_path"'
)

$failedRoles = [System.Collections.Generic.List[string]]::new()
foreach ($selectedRole in Get-SelectedRoles -RequestedRole $Role) {
    try {
        $instance = Resolve-PulseFlowEc2Instance -Role $selectedRole
        Write-Host "Bootstrapping Docker on $($instance.Role) ($($instance.InstanceId), $($instance.PrivateIpAddress))."
        $invocation = Invoke-PulseFlowSsmRunCommand `
            -InstanceId $instance.InstanceId `
            -Commands $commands `
            -Comment "Install Docker for PulseFlow $($instance.Role) host"

        Write-Host "Docker bootstrap succeeded on $($instance.Role)."
        $output = ([string]$invocation.StandardOutputContent).Trim()
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            Write-Host $output
        }
    }
    catch {
        $failedRoles.Add($selectedRole)
        Write-Warning "Docker bootstrap failed for role '$selectedRole': $($_.Exception.Message)"
    }
}

if ($failedRoles.Count -gt 0) {
    throw "Docker bootstrap failed for: $($failedRoles -join ', ')."
}
