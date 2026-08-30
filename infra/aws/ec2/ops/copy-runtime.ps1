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

function Get-RuntimeFileNames {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("app", "postgres", "rabbitmq", "redis")]
        [string]$RuntimeRole
    )

    if ($RuntimeRole -eq "app") {
        return @("compose.yaml", "haproxy.cfg")
    }

    return @("compose.yaml")
}

function Get-ComposeValidationEnvironment {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("app", "postgres", "rabbitmq", "redis")]
        [string]$RuntimeRole
    )

    switch ($RuntimeRole) {
        "app" {
            return "PULSEFLOW_IMAGE=ghcr.io/example/pulseflow-api:placeholder PULSEFLOW_POSTGRES_HOST=postgres.invalid PULSEFLOW_POSTGRES_DB=pulseflow PULSEFLOW_POSTGRES_USER=pulseflow PULSEFLOW_POSTGRES_PASSWORD=placeholder PULSEFLOW_RABBITMQ_HOST=rabbitmq.invalid PULSEFLOW_RABBITMQ_USER=pulseflow PULSEFLOW_RABBITMQ_PASSWORD=placeholder PULSEFLOW_REDIS_HOST=redis.invalid"
        }
        "postgres" {
            return "PULSEFLOW_POSTGRES_DB=pulseflow PULSEFLOW_POSTGRES_USER=pulseflow PULSEFLOW_POSTGRES_PASSWORD=placeholder"
        }
        "rabbitmq" {
            return "PULSEFLOW_RABBITMQ_USER=pulseflow PULSEFLOW_RABBITMQ_PASSWORD=placeholder"
        }
        "redis" {
            return ""
        }
    }
}

$runtimeRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..\\..\\runtime")).Path
$failedRoles = [System.Collections.Generic.List[string]]::new()

foreach ($selectedRole in Get-SelectedRoles -RequestedRole $Role) {
    try {
        $localRuntimeDirectory = Join-Path $runtimeRoot $selectedRole
        $fileNames = Get-RuntimeFileNames -RuntimeRole $selectedRole
        $filePayloads = [System.Collections.Generic.List[object]]::new()

        foreach ($fileName in $fileNames) {
            $sourcePath = Join-Path $localRuntimeDirectory $fileName
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "Expected runtime file was not found: $sourcePath"
            }

            $filePayloads.Add([pscustomobject]@{
                    Name   = $fileName
                    Base64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($sourcePath))
                })
        }

        $remoteDirectory = "/opt/pulseflow/runtime/$selectedRole"
        $commands = @(
            "set -euo pipefail",
            'stage_directory="$(mktemp -d /tmp/pulseflow-runtime.XXXXXX)"',
            'trap ''rm -rf "$stage_directory"'' EXIT',
            "install -d -m 0755 `"$remoteDirectory`""
        )

        foreach ($payload in $filePayloads) {
            $stagedPath = "`$stage_directory/$($payload.Name)"
            $destinationPath = "$remoteDirectory/$($payload.Name)"
            $commands += "printf '%s' '$($payload.Base64)' | base64 --decode > `"$stagedPath`""
            $commands += "install -m 0644 `"$stagedPath`" `"$destinationPath`""
            $commands += "test -f `"$destinationPath`" -a -r `"$destinationPath`""
        }

        $validationEnvironment = Get-ComposeValidationEnvironment -RuntimeRole $selectedRole
        $validationCommand = "docker compose -f $remoteDirectory/compose.yaml config >/dev/null"
        if (-not [string]::IsNullOrWhiteSpace($validationEnvironment)) {
            $validationCommand = "env $validationEnvironment $validationCommand"
        }

        $commands += $validationCommand
        $instance = Resolve-PulseFlowEc2Instance -Role $selectedRole
        Write-Host "Copying runtime files to $($instance.Role) ($($instance.InstanceId), $($instance.PrivateIpAddress))."
        [void](Invoke-PulseFlowSsmRunCommand `
                -InstanceId $instance.InstanceId `
                -Commands $commands `
                -Comment "Copy PulseFlow $selectedRole runtime definition")
        Write-Host "Runtime copy and Docker Compose validation succeeded on $($instance.Role)."
    }
    catch {
        $failedRoles.Add($selectedRole)
        Write-Warning "Runtime copy failed for role '$selectedRole': $($_.Exception.Message)"
    }
}

if ($failedRoles.Count -gt 0) {
    throw "Runtime copy failed for: $($failedRoles -join ', ')."
}
