$scriptPath = Join-Path $PSScriptRoot "..\\..\\scripts\\Get-PulseFlowActiveTaggedEc2InstanceIds.ps1"
. $scriptPath

Describe "Get-PulseFlowActiveTaggedEc2InstanceIds" {
    It "Get-PulseFlowActiveTaggedEc2InstanceIds_MixedInstanceStates_ReturnsOnlyActiveInstanceIds" {
        # Arrange
        $describeInstancesJson = @{
            Reservations = @(
                @{
                    Instances = @(
                        @{ InstanceId = "i-pending"; State = @{ Name = "pending" } },
                        @{ InstanceId = "i-running"; State = @{ Name = "running" } },
                        @{ InstanceId = "i-stopped"; State = @{ Name = "stopped" } },
                        @{ InstanceId = "i-terminated"; State = @{ Name = "terminated" } }
                    )
                }
            )
        } | ConvertTo-Json -Depth 5

        # Act
        $instanceIds = @(Get-PulseFlowActiveTaggedEc2InstanceIds -DescribeInstancesJson $describeInstancesJson)

        # Assert
        $instanceIds | Should Be @("i-pending", "i-running", "i-stopped")
    }

    It "Get-PulseFlowActiveTaggedEc2InstanceIds_EmptyReservations_ReturnsNoInstanceIds" {
        # Arrange
        $describeInstancesJson = @{ Reservations = @() } | ConvertTo-Json -Depth 5

        # Act
        $instanceIds = @(Get-PulseFlowActiveTaggedEc2InstanceIds -DescribeInstancesJson $describeInstancesJson)

        # Assert
        $instanceIds.Count | Should Be 0
    }
}
