using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;

namespace PulseFlow.IntegrationTests.Persistence;

public sealed class PulseFlowDbContextModelTests
{
    [Fact]
    public void Model_contains_the_explicit_event_record_mapping()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql("Host=localhost;Database=pulseflow;Username=pulseflow;Password=pulseflow")
            .Options;

        using var dbContext = new PulseFlowDbContext(options);

        var entityType = Assert.IsAssignableFrom<IEntityType>(
            dbContext.Model.FindEntityType(typeof(EventRecord)));
        var table = StoreObjectIdentifier.Table("events");

        Assert.Equal("events", entityType.GetTableName());
        Assert.Equal(nameof(EventRecord.Id), Assert.Single(entityType.FindPrimaryKey()!.Properties).Name);

        AssertProperty(entityType, table, nameof(EventRecord.Id), "id", "uuid");
        Assert.Equal(ValueGenerated.Never, entityType.FindProperty(nameof(EventRecord.Id))!.ValueGenerated);
        AssertProperty(entityType, table, nameof(EventRecord.Type), "type", "text");
        AssertProperty(entityType, table, nameof(EventRecord.Source), "source", "text");
        AssertProperty(
            entityType,
            table,
            nameof(EventRecord.OccurredAt),
            "occurred_at",
            "timestamp with time zone");
        AssertProperty(
            entityType,
            table,
            nameof(EventRecord.ReceivedAt),
            "received_at",
            "timestamp with time zone");
        AssertProperty(entityType, table, nameof(EventRecord.PayloadJson), "payload", "jsonb");
    }

    private static void AssertProperty(
        IEntityType entityType,
        StoreObjectIdentifier table,
        string propertyName,
        string columnName,
        string columnType)
    {
        var property = Assert.IsAssignableFrom<IProperty>(entityType.FindProperty(propertyName));

        Assert.Equal(columnName, property.GetColumnName(table));
        Assert.Equal(columnType, property.GetColumnType());
        Assert.False(property.IsNullable);
    }
}
