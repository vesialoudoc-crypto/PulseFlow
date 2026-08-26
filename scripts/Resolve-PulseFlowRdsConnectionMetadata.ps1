Set-StrictMode -Version Latest

function Get-PulseFlowRequiredStringProperty {
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$InputObject,
        [Parameter(Mandatory)]
        [string]$PropertyName,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($null -eq $InputObject) {
        throw "$Description is missing."
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "$Description is missing or empty."
    }

    return [string]$property.Value
}

function Resolve-PulseFlowRdsConnectionMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$MasterSecretArn,
        [Parameter(Mandatory)]
        [object]$DescribeDbInstancesJson
    )

    $describeDbInstancesText = $DescribeDbInstancesJson | Out-String
    if ([string]::IsNullOrWhiteSpace($describeDbInstancesText)) {
        throw "AWS RDS describe-db-instances returned an empty response."
    }

    try {
        $describeDbInstancesResponse = $describeDbInstancesText | ConvertFrom-Json
    }
    catch {
        throw "AWS RDS describe-db-instances returned invalid JSON."
    }

    $dbInstancesProperty = $describeDbInstancesResponse.PSObject.Properties["DBInstances"]
    if ($null -eq $dbInstancesProperty) {
        throw "AWS RDS describe-db-instances response is missing DBInstances."
    }

    $matchingDbInstances = @(
        $dbInstancesProperty.Value | Where-Object {
            $masterUserSecretProperty = $_.PSObject.Properties["MasterUserSecret"]
            if ($null -eq $masterUserSecretProperty -or $null -eq $masterUserSecretProperty.Value) {
                return $false
            }

            $secretArnProperty = $masterUserSecretProperty.Value.PSObject.Properties["SecretArn"]
            return $null -ne $secretArnProperty -and $secretArnProperty.Value -ceq $MasterSecretArn
        }
    )

    if ($matchingDbInstances.Count -ne 1) {
        throw "Expected exactly one RDS DB instance to reference the master-secret ARN, but found $($matchingDbInstances.Count)."
    }

    $dbInstance = $matchingDbInstances[0]
    $endpointProperty = $dbInstance.PSObject.Properties["Endpoint"]
    if ($null -eq $endpointProperty -or $null -eq $endpointProperty.Value) {
        throw "RDS DB instance endpoint is missing."
    }

    $endpointAddress = Get-PulseFlowRequiredStringProperty `
        -InputObject $endpointProperty.Value `
        -PropertyName "Address" `
        -Description "RDS DB instance endpoint address"
    $databaseName = Get-PulseFlowRequiredStringProperty `
        -InputObject $dbInstance `
        -PropertyName "DBName" `
        -Description "RDS DB instance DBName"

    $portProperty = $endpointProperty.Value.PSObject.Properties["Port"]
    $endpointPort = 0
    if (
        $null -eq $portProperty -or
        -not [int]::TryParse([string]$portProperty.Value, [ref]$endpointPort) -or
        $endpointPort -lt 1 -or
        $endpointPort -gt 65535
    ) {
        throw "RDS DB instance endpoint port is missing or invalid."
    }

    return [pscustomobject]@{
        Host     = $endpointAddress
        Port     = $endpointPort
        Database = $databaseName
    }
}
