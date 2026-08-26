$scriptPath = Join-Path $PSScriptRoot "..\..\scripts\Resolve-PulseFlowRdsConnectionMetadata.ps1"
. $scriptPath

Describe "Resolve-PulseFlowRdsConnectionMetadata" {
    It "DescribeDbInstancesResponse_WithMatchingMasterSecret_ReturnsEndpointAndDatabase" {
        # Arrange
        $masterSecretArn = "arn:aws:secretsmanager:eu-central-1:123456789012:secret:rds-master"
        $describeDbInstancesResponse = @{
            DBInstances = @(
                @{
                    DBInstanceIdentifier = "pulseflow-staging-postgresql"
                    MasterUserSecret     = @{ SecretArn = $masterSecretArn }
                    Endpoint             = @{ Address = "pulseflow-staging-postgresql.example.rds.amazonaws.com"; Port = 5432 }
                    DBName               = "pulseflow"
                }
            )
        } | ConvertTo-Json -Depth 5

        # Act
        $metadata = Resolve-PulseFlowRdsConnectionMetadata `
            -MasterSecretArn $masterSecretArn `
            -DescribeDbInstancesJson $describeDbInstancesResponse

        # Assert
        $metadata.Host | Should Be "pulseflow-staging-postgresql.example.rds.amazonaws.com"
        $metadata.Port | Should Be 5432
        $metadata.Database | Should Be "pulseflow"
    }

    It "DescribeDbInstancesResponse_WithoutMatchingMasterSecret_ThrowsClearError" {
        # Arrange
        $masterSecretArn = "arn:aws:secretsmanager:eu-central-1:123456789012:secret:rds-master"
        $describeDbInstancesResponse = @{ DBInstances = @() } | ConvertTo-Json -Depth 5

        # Act
        $thrownError = $null
        try {
            Resolve-PulseFlowRdsConnectionMetadata `
                -MasterSecretArn $masterSecretArn `
                -DescribeDbInstancesJson $describeDbInstancesResponse
        }
        catch {
            $thrownError = $_
        }

        # Assert
        $thrownError.Exception.Message | Should Match "Expected exactly one RDS DB instance to reference the master-secret ARN, but found 0."
    }

    It "DescribeDbInstancesResponse_WithAmbiguousMasterSecret_ThrowsClearError" {
        # Arrange
        $masterSecretArn = "arn:aws:secretsmanager:eu-central-1:123456789012:secret:rds-master"
        $describeDbInstancesResponse = @{
            DBInstances = @(
                @{
                    DBInstanceIdentifier = "pulseflow-staging-postgresql-a"
                    MasterUserSecret     = @{ SecretArn = $masterSecretArn }
                },
                @{
                    DBInstanceIdentifier = "pulseflow-staging-postgresql-b"
                    MasterUserSecret     = @{ SecretArn = $masterSecretArn }
                }
            )
        } | ConvertTo-Json -Depth 5

        # Act
        $thrownError = $null
        try {
            Resolve-PulseFlowRdsConnectionMetadata `
                -MasterSecretArn $masterSecretArn `
                -DescribeDbInstancesJson $describeDbInstancesResponse
        }
        catch {
            $thrownError = $_
        }

        # Assert
        $thrownError.Exception.Message | Should Match "Expected exactly one RDS DB instance to reference the master-secret ARN, but found 2."
    }
}
