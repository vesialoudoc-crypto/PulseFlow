Set-StrictMode -Version Latest

function Resolve-PulseFlowExecutable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$CommandName,
        [Parameter(Mandatory)]
        [string]$DisplayName,
        [string[]]$CandidatePaths = @(),
        [string]$PreferredVersion,
        [scriptblock]$VersionResolver,
        [Parameter(Mandatory)]
        [string]$InstallationInstructions
    )

    $checkedLocations = [System.Collections.Generic.List[string]]::new()
    $possiblePaths = [System.Collections.Generic.List[string]]::new()
    $knownPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    $checkedLocations.Add("PATH via Get-Command $CommandName")
    $command = Get-Command -Name $CommandName -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Path)) {
        [void]$possiblePaths.Add($command.Path)
    }

    foreach ($candidatePath in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        $checkedLocations.Add($candidatePath)
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            [void]$possiblePaths.Add($candidatePath)
        }
    }

    $resolutions = [System.Collections.Generic.List[object]]::new()
    foreach ($possiblePath in $possiblePaths) {
        $resolvedPath = (Resolve-Path -LiteralPath $possiblePath).Path
        if (-not $knownPaths.Add($resolvedPath)) {
            continue
        }

        $version = $null
        if ($null -ne $VersionResolver) {
            $version = & $VersionResolver $resolvedPath
        }

        [void]$resolutions.Add([pscustomobject]@{
                Path    = $resolvedPath
                Version = $version
            })
    }

    if ($resolutions.Count -eq 0) {
        $checkedLocationText = ($checkedLocations | ForEach-Object { "- $_" }) -join [Environment]::NewLine
        throw "$DisplayName was not found. Checked locations:`n$checkedLocationText`nInstall it with: $InstallationInstructions"
    }

    if (-not [string]::IsNullOrWhiteSpace($PreferredVersion)) {
        $preferredResolution = $resolutions |
            Where-Object { $_.Version -eq $PreferredVersion } |
            Select-Object -First 1
        if ($null -ne $preferredResolution) {
            return $preferredResolution
        }
    }

    return $resolutions[0]
}

function Get-WinGetTerraformCandidatePaths {
    $localApplicationData = [Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
        return @()
    }

    $winGetPackagesDirectory = Join-Path $localApplicationData "Microsoft\WinGet\Packages"
    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    [void]$candidatePaths.Add((Join-Path $winGetPackagesDirectory "Hashicorp.Terraform_Microsoft.Winget.Source_8wekyb3d8bbwe\terraform.exe"))

    if (Test-Path -LiteralPath $winGetPackagesDirectory -PathType Container) {
        foreach ($packageDirectory in Get-ChildItem -LiteralPath $winGetPackagesDirectory -Directory -Filter "Hashicorp.Terraform_*" -ErrorAction SilentlyContinue) {
            [void]$candidatePaths.Add((Join-Path $packageDirectory.FullName "terraform.exe"))
        }
    }

    return $candidatePaths.ToArray()
}

function Get-TerraformExecutableVersion {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    try {
        $versionJson = & $ExecutablePath version -json
        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        $version = ($versionJson | ConvertFrom-Json).terraform_version
        if ([string]::IsNullOrWhiteSpace($version)) {
            return $null
        }

        return [string]$version
    }
    catch {
        return $null
    }
}

function Resolve-PulseFlowTerraformExecutable {
    param(
        [Parameter(Mandatory)]
        [string]$PreferredVersion
    )

    return Resolve-PulseFlowExecutable `
        -CommandName "terraform" `
        -DisplayName "Terraform" `
        -CandidatePaths (Get-WinGetTerraformCandidatePaths) `
        -PreferredVersion $PreferredVersion `
        -VersionResolver ${function:Get-TerraformExecutableVersion} `
        -InstallationInstructions "winget install Hashicorp.Terraform"
}

function Get-AwsCliCandidatePaths {
    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    foreach ($programFilesDirectory in @(
            $env:ProgramFiles,
            $env:ProgramW6432,
            ${env:ProgramFiles(x86)}
        )) {
        if ([string]::IsNullOrWhiteSpace($programFilesDirectory)) {
            continue
        }

        [void]$candidatePaths.Add((Join-Path $programFilesDirectory "Amazon\AWSCLIV2\aws.exe"))
    }

    return $candidatePaths.ToArray()
}

function Get-AwsCliVersion {
    param(
        [Parameter(Mandatory)]
        [string]$ExecutablePath
    )

    $versionOutput = (& $ExecutablePath --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine the AWS CLI version from '$ExecutablePath'."
    }

    if ($versionOutput -notmatch "^aws-cli/2\.") {
        throw "AWS CLI v2 is required, but '$ExecutablePath' reported '$versionOutput'."
    }

    return $versionOutput
}

function Resolve-PulseFlowAwsCliExecutable {
    $resolution = Resolve-PulseFlowExecutable `
        -CommandName "aws" `
        -DisplayName "AWS CLI v2" `
        -CandidatePaths (Get-AwsCliCandidatePaths) `
        -InstallationInstructions "winget install Amazon.AWSCLI"
    $resolution.Version = Get-AwsCliVersion -ExecutablePath $resolution.Path

    return $resolution
}
