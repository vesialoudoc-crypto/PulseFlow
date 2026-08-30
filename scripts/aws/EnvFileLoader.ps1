#PulseFlow/
#  .env
#  scripts/
#    aws/
#      EnvFileLoader.ps1

Set-StrictMode -Version Latest

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$envFilePath = Join-Path $repoRoot ".env"

if (-not (Test-Path -LiteralPath $envFilePath -PathType Leaf)) {
    throw ".env file was not found: $envFilePath"
}

function EnvFileLoader {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Dotenv file was not found: $Path"
    }

    $lineNumber = 0
    foreach ($rawLine in [System.IO.File]::ReadLines($Path)) {
        $lineNumber++
        $line = $rawLine.Trim()

        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }

        $match = [regex]::Match($line, "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)$")
        if (-not $match.Success) {
            throw "Invalid dotenv entry at $Path line $lineNumber. Expected KEY=value."
        }

        $name = $match.Groups["name"].Value
        $value = $match.Groups["value"].Value.Trim()

        if ($value.Length -ge 2) {
            $firstCharacter = $value[0]
            $lastCharacter = $value[$value.Length - 1]
            if (($firstCharacter -eq '"' -and $lastCharacter -eq '"') -or ($firstCharacter -eq "'" -and $lastCharacter -eq "'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        [Environment]::SetEnvironmentVariable($name, $value, [System.EnvironmentVariableTarget]::Process)
    }
}

EnvFileLoader -Path $envFilePath