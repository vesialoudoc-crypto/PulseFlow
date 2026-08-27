function Get-PulseFlowActiveTaggedEc2InstanceIds {
    param(
        [Parameter(Mandatory)]
        [string]$DescribeInstancesJson
    )

    $response = $DescribeInstancesJson | ConvertFrom-Json
    $activeStates = @("pending", "running", "stopping", "stopped")

    return @(
        @($response.Reservations) |
            ForEach-Object { @($_.Instances) } |
            Where-Object {
                $_.State.Name -in $activeStates -and
                -not [string]::IsNullOrWhiteSpace([string]$_.InstanceId)
            } |
            ForEach-Object { [string]$_.InstanceId }
    )
}
