$ErrorActionPreference = 'Stop'

try
{
    & docker info *> $null
    $dockerIsAvailable = $LASTEXITCODE -eq 0
}
catch
{
    $dockerIsAvailable = $false
}

if (-not $dockerIsAvailable)
{
    Write-Error @"
Docker Desktop / Docker Engine is not available. Testcontainers integration tests cannot run without a running Docker daemon.
Start Docker and run pwsh ./scripts/test.ps1 again.
"@
    exit 1
}

& dotnet test PulseFlow.slnx
exit $LASTEXITCODE
