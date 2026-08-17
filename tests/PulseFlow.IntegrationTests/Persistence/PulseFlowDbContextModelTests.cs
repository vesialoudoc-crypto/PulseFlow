using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;

namespace PulseFlow.IntegrationTests.Persistence;

public sealed class PulseFlowDbContextModelTests
{
    [Fact]
    public void GetTableName_EventRecordIsMapped_ReturnsEventsTableName()
    {
        // Arrange
        using var dbContext = CreateDbContext();

        // Act
        var tableName = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .GetTableName();

        // Assert
        Assert.Equal("events", tableName);
    }

    [Fact]
    public void FindPrimaryKey_EventRecordIsMapped_ReturnsIdProperty()
    {
        // Arrange
        using var dbContext = CreateDbContext();

        // Act
        var primaryKeyProperty = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .FindPrimaryKey()?
            .Properties
            .SingleOrDefault()?
            .Name;

        // Assert
        Assert.Equal(nameof(EventRecord.Id), primaryKeyProperty);
    }

    [Fact]
    public void FindProperty_IdIsMapped_ReturnsValueGeneratedNever()
    {
        // Arrange
        using var dbContext = CreateDbContext();

        // Act
        var valueGenerated = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .FindProperty(nameof(EventRecord.Id))?
            .ValueGenerated;

        // Assert
        Assert.Equal(ValueGenerated.Never, valueGenerated);
    }

    [Theory]
    [InlineData(nameof(EventRecord.Id), "id")]
    [InlineData(nameof(EventRecord.Type), "type")]
    [InlineData(nameof(EventRecord.Source), "source")]
    [InlineData(nameof(EventRecord.OccurredAt), "occurred_at")]
    [InlineData(nameof(EventRecord.ReceivedAt), "received_at")]
    [InlineData(nameof(EventRecord.PayloadJson), "payload")]
    public void GetColumnName_EventRecordPropertyIsMapped_ReturnsExpectedColumnName(
        string propertyName,
        string expectedColumnName)
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var table = StoreObjectIdentifier.Table("events");

        // Act
        var columnName = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .FindProperty(propertyName)?
            .GetColumnName(table);

        // Assert
        Assert.Equal(expectedColumnName, columnName);
    }

    [Theory]
    [InlineData(nameof(EventRecord.Id), "uuid")]
    [InlineData(nameof(EventRecord.Type), "text")]
    [InlineData(nameof(EventRecord.Source), "text")]
    [InlineData(nameof(EventRecord.OccurredAt), "timestamp with time zone")]
    [InlineData(nameof(EventRecord.ReceivedAt), "timestamp with time zone")]
    [InlineData(nameof(EventRecord.PayloadJson), "jsonb")]
    public void GetColumnType_EventRecordPropertyIsMapped_ReturnsExpectedColumnType(
        string propertyName,
        string expectedColumnType)
    {
        // Arrange
        using var dbContext = CreateDbContext();

        // Act
        var columnType = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .FindProperty(propertyName)?
            .GetColumnType();

        // Assert
        Assert.Equal(expectedColumnType, columnType);
    }

    [Theory]
    [InlineData(nameof(EventRecord.Id))]
    [InlineData(nameof(EventRecord.Type))]
    [InlineData(nameof(EventRecord.Source))]
    [InlineData(nameof(EventRecord.OccurredAt))]
    [InlineData(nameof(EventRecord.ReceivedAt))]
    [InlineData(nameof(EventRecord.PayloadJson))]
    public void FindProperty_EventRecordPropertyIsMapped_ReturnsNonNullableProperty(
        string propertyName)
    {
        // Arrange
        using var dbContext = CreateDbContext();

        // Act
        var isNullable = dbContext.Model
            .FindEntityType(typeof(EventRecord))?
            .FindProperty(propertyName)?
            .IsNullable;

        // Assert
        Assert.False(isNullable);
    }

    #region Test helpers

    private static PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql("Host=localhost;Database=pulseflow;Username=pulseflow;Password=pulseflow")
            .Options;

        return new PulseFlowDbContext(options);
    }

    #endregion
}
