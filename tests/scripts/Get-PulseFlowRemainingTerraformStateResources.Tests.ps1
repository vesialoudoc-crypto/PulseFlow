$scriptPath = Join-Path $PSScriptRoot "..\..\scripts\Get-PulseFlowRemainingTerraformStateResources.ps1"
. $scriptPath

Describe "Get-PulseFlowRemainingTerraformStateResources" {
    It "Get-PulseFlowRemainingTerraformStateResources_EmptyStateList_ReturnsZeroResources" {
        # Arrange
        $stateListOutput = @()

        # Act
        $remainingResources = @(
            Get-PulseFlowRemainingTerraformStateResources -StateListOutput $stateListOutput
        )

        # Assert
        $remainingResources.Count | Should Be 0
    }

    It "Get-PulseFlowRemainingTerraformStateResources_SingleStateResource_ReturnsThatResource" {
        # Arrange
        $stateListOutput = @(
            "aws_ecs_cluster.pulseflow"
        )

        # Act
        $remainingResources = @(
            Get-PulseFlowRemainingTerraformStateResources -StateListOutput $stateListOutput
        )

        # Assert
        $remainingResources.Count | Should Be 1
        $remainingResources[0] | Should Be "aws_ecs_cluster.pulseflow"
    }

    It "Get-PulseFlowRemainingTerraformStateResources_MultipleStateResources_ReturnsAllResources" {
        # Arrange
        $stateListOutput = @(
            "aws_ecs_cluster.pulseflow",
            "aws_rds_cluster_instance.pulseflow"
        )

        # Act
        $remainingResources = @(
            Get-PulseFlowRemainingTerraformStateResources -StateListOutput $stateListOutput
        )

        # Assert
        $remainingResources.Count | Should Be 2
        $remainingResources[0] | Should Be "aws_ecs_cluster.pulseflow"
        $remainingResources[1] | Should Be "aws_rds_cluster_instance.pulseflow"
    }
}
