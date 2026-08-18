using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Persistence;
using PulseFlow.IntegrationTests.Infrastructure;
using RabbitMQ.Client;

namespace PulseFlow.IntegrationTests.Ingestion.Messaging;

public sealed class RabbitMqAsynchronousIngestionTests :
    IClassFixture<PostgreSqlFixture>,
    IClassFixture<RabbitMqFixture>
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly RabbitMqFixture _rabbitMqFixture;

    public RabbitMqAsynchronousIngestionTests(
        PostgreSqlFixture postgreSqlFixture,
        RabbitMqFixture rabbitMqFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _rabbitMqFixture = rabbitMqFixture;
    }

    [Fact]
    public async Task PostEvents_RealRabbitMqAndPostgreSql_PersistsValidEventsAsynchronously()
    {
        // Arrange
        var expectedTypes = new[]
        {
            "real-broker.integration.first",
            "real-broker.integration.second"
        };
        // A unique queue keeps another test run from taking this batch.
        using var factory = CreateFactory(consumerCount: 1, CreateQueueName());
        await EnsureDatabaseMigratedAsync(factory.Services);
        using var client = factory.CreateClient();
        using var content = CreateNdjsonContent(expectedTypes);

        // Act
        using var response = await client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // The HTTP response comes before parsing and database storage finish.
        var actualTypes = await WaitForPersistedTypesAsync(expectedTypes);
        Assert.Equal(expectedTypes, actualTypes);
    }

    [Fact]
    public async Task ConsumerCount_Two_RegistersCompetingConsumersWithSharedConnectionAndSeparateChannels()
    {
        // Arrange
        var queueName = CreateQueueName();
        using var factory = CreateFactory(consumerCount: 2, queueName);

        // This checks channel ownership, not which consumer gets a delivery.
        // Act
        var consumers = factory.Services
            .GetServices<IHostedService>()
            .OfType<EventParserConsumer>()
            .ToArray();
        var consumerChannels = consumers
            .Select(consumer => Assert.IsType<RabbitMqConsumerChannel>(consumer.ConsumerChannel))
            .ToArray();

        // Assert
        Assert.Equal(2, consumerChannels.Length);
        Assert.All(consumerChannels, channel => Assert.Equal(queueName, channel.QueueName));
        Assert.Same(consumerChannels[0].Connection, consumerChannels[1].Connection);
        Assert.NotNull(consumerChannels[0].Channel);
        Assert.NotNull(consumerChannels[1].Channel);
        Assert.NotSame(consumerChannels[0].Channel, consumerChannels[1].Channel);
    }

    #region Test helpers

    private PulseFlowWebApplicationFactory<Program> CreateFactory(
        int consumerCount,
        string queueName)
    {
        return new PulseFlowWebApplicationFactory<Program>(
            _postgreSqlFixture.ConnectionString,
            _rabbitMqFixture.ConnectionString,
            queueName,
            consumerCount);
    }

    private static ByteArrayContent CreateNdjsonContent(IEnumerable<string> types)
    {
        var batch = string.Join(
            '\n',
            types.Select(CreateRecordJson).Append(string.Empty));
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(batch));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-ndjson");

        return content;
    }

    private async Task EnsureDatabaseMigratedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private async Task<string[]> WaitForPersistedTypesAsync(IReadOnlyCollection<string> expectedTypes)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (!timeoutSource.IsCancellationRequested)
        {
            await using var dbContext = CreateDbContext();
            var actualTypes = await dbContext.EventRecords
                .AsNoTracking()
                .Where(eventRecord => expectedTypes.Contains(eventRecord.Type))
                .OrderBy(eventRecord => eventRecord.Type)
                .Select(eventRecord => eventRecord.Type)
                .ToArrayAsync(timeoutSource.Token);

            if (actualTypes.Length == expectedTypes.Count)
            {
                return actualTypes;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeoutSource.Token);
        }

        throw new TimeoutException("The expected events were not persisted before the timeout.");
    }

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_postgreSqlFixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private static string CreateRecordJson(string type)
    {
        return JsonSerializer.Serialize(new
        {
            type,
            source = "real-broker-integration-test",
            occurredAt = "2026-08-18T10:00:00Z",
            payload = new { sequence = type }
        });
    }

    private static string CreateQueueName()
    {
        return $"pulseflow.integration.{Guid.NewGuid():N}";
    }

    #endregion
}
