Set-StrictMode -Version Latest

function Get-PulseFlowRemainingTerraformStateResources {
    param(
        [AllowNull()]
        [AllowEmptyCollection()]
        [string[]]$StateListOutput = @()
    )

    return @(
        $StateListOutput | Where-Object {
            -not [string]::IsNullOrWhiteSpace($_)
        }
    )
}
