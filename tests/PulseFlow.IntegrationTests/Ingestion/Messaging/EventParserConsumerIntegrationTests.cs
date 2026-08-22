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
        var consumer = new TestIngestionBatchConsumer();
        await using var serviceProvider = CreateServiceProvider();
        await EnsureDatabaseMigratedAsync(serviceProvider);
        var eventParserConsumer = new EventParserConsumer(
            new TestIngestionBatchConsumerFactory(consumer),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<NdjsonRecordReader>(),
            NullLogger<EventParserConsumer>.Instance,
            consumerCount: 1);
        using var stoppingSource = new CancellationTokenSource();
        var rawBatch = string.Join(
            '\n',
            CreateRecordJson("consumer.integration.first"),
            CreateRecordJson("consumer.integration.second"),
            string.Empty);

        // Act
        await eventParserConsumer.StartAsync(stoppingSource.Token);
        await consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await consumer.DeliverAsync(
            Encoding.UTF8.GetBytes(rawBatch),
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
            eventId = Guid.NewGuid(),
            type,
            source = "integration-test-source",
            occurredAt = "2026-08-18T10:00:00Z",
            payload = new { sequence = type }
        });
    }

    private sealed class TestIngestionBatchConsumerFactory : IIngestionBatchConsumerFactory
    {
        private readonly TestIngestionBatchConsumer _consumer;

        public TestIngestionBatchConsumerFactory(TestIngestionBatchConsumer consumer)
        {
            _consumer = consumer;
        }

        public ValueTask<IIngestionBatchConsumer> CreateAsync(
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IIngestionBatchConsumer>(_consumer);
        }
    }

    private sealed class TestIngestionBatchConsumer : IIngestionBatchConsumer
    {
        private IngestionBatchHandler? _handler;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConsumeAsync(
            IngestionBatchHandler handler,
            CancellationToken cancellationToken)
        {
            _handler = handler;
            Started.SetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask DeliverAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var delivery = new IngestionBatchDelivery(
                body,
                acknowledgementCancellationToken => Task.CompletedTask,
                rejectionCancellationToken => Task.CompletedTask);

            var handler = _handler
                ?? throw new InvalidOperationException("The consumer has not started.");

            return new ValueTask(handler(delivery, cancellationToken));
        }
    }

    #endregion
}
