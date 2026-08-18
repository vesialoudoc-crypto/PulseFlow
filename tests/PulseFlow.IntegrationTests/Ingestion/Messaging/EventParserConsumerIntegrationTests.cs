using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion.Messaging;

public sealed class EventParserConsumerIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public EventParserConsumerIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EventParserConsumer_ValidRawBatch_PersistsEventsThroughPostgreSql()
    {
        // Arrange
        var consumerChannel = new TestRabbitMqConsumerChannel();
        await using var serviceProvider = CreateServiceProvider();
        await EnsureDatabaseMigratedAsync(serviceProvider);
        var eventParserConsumer = new EventParserConsumer(
            consumerChannel,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<NdjsonRecordReader>(),
            NullLogger<EventParserConsumer>.Instance);
        using var stoppingSource = new CancellationTokenSource();
        var rawBatch = string.Join(
            '\n',
            CreateRecordJson("consumer.integration.first"),
            CreateRecordJson("consumer.integration.second"),
            string.Empty);

        // Act
        await eventParserConsumer.StartAsync(stoppingSource.Token);
        await consumerChannel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumerChannel.DeliverAsync(
            new RabbitMqDelivery(1, Encoding.UTF8.GetBytes(rawBatch)),
            stoppingSource.Token);

        // Assert
        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();
        var storedTypes = await dbContext.EventRecords
            .AsNoTracking()
            .Where(eventRecord => eventRecord.Type.StartsWith("consumer.integration."))
            .OrderBy(eventRecord => eventRecord.Type)
            .Select(eventRecord => eventRecord.Type)
            .ToArrayAsync();
        Assert.Equal(
            new[] { "consumer.integration.first", "consumer.integration.second" },
            storedTypes);

        stoppingSource.Cancel();
        await eventParserConsumer.StopAsync(CancellationToken.None);
    }

    #region Test helpers

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PulseFlowDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton<NdjsonRecordReader>();
        services.AddSingleton<EventEnvelopeValidator>();
        services.AddScoped<IEventChunkStore, EfCoreEventChunkStore>();
        services.AddScoped<IngestEventsHandler>(serviceProvider => new IngestEventsHandler(
            serviceProvider.GetRequiredService<EventEnvelopeValidator>(),
            serviceProvider.GetRequiredService<IEventChunkStore>(),
            chunkCapacity: 2));

        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseMigratedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static string CreateRecordJson(string type)
    {
        return JsonSerializer.Serialize(new
        {
            type,
            source = "integration-test-source",
            occurredAt = "2026-08-18T10:00:00Z",
            payload = new { sequence = type }
        });
    }

    private sealed class TestRabbitMqConsumerChannel : IRabbitMqConsumerChannel
    {
        private Func<RabbitMqDelivery, CancellationToken, Task>? _deliveryHandler;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartConsumingAsync(
            Func<RabbitMqDelivery, CancellationToken, Task> deliveryHandler,
            CancellationToken cancellationToken)
        {
            _deliveryHandler = deliveryHandler;
            Started.SetResult();
            return Task.CompletedTask;
        }

        public Task AcknowledgeAsync(ulong deliveryTag, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopConsumingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task DeliverAsync(RabbitMqDelivery delivery, CancellationToken cancellationToken)
        {
            return (_deliveryHandler ?? throw new InvalidOperationException("Consumer has not started."))(
                delivery,
                cancellationToken);
        }
    }

    #endregion
}
