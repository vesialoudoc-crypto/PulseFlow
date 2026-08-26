using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.Api.Startup;
using PulseFlow.IntegrationTests.Infrastructure;
using RabbitMQ.Client;

namespace PulseFlow.IntegrationTests.Ingestion.Messaging;

public sealed class RabbitMqAsynchronousIngestionTests :
    IClassFixture<PostgreSqlFixture>,
    IClassFixture<RabbitMqFixture>,
    IClassFixture<RedisFixture>
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly RabbitMqFixture _rabbitMqFixture;
    private readonly RedisFixture _redisFixture;

    public RabbitMqAsynchronousIngestionTests(
        PostgreSqlFixture postgreSqlFixture,
        RabbitMqFixture rabbitMqFixture,
        RedisFixture redisFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _rabbitMqFixture = rabbitMqFixture;
        _redisFixture = redisFixture;
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
        await EnsureDatabaseMigratedAsync();
        using var factory = CreateFactory(consumerCount: 1, CreateQueueName());
        using var client = factory.CreateClient();
        await WaitUntilReadyAsync(factory);
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
    public async Task ConsumerCount_Two_RegistersTwoRabbitMqConsumers()
    {
        // Arrange
        var queueName = CreateQueueName();
        await EnsureDatabaseMigratedAsync();
        using var factory = CreateFactory(consumerCount: 2, queueName);
        using var client = factory.CreateClient();
        await WaitUntilReadyAsync(factory);

        // Act
        var queueInfo = await WaitForConsumerCountAsync(queueName, expectedCount: 2);

        // Assert
        Assert.Equal(2U, queueInfo.ConsumerCount);
    }

    [Fact]
    public async Task ProcessingFails_RejectsBatchToDeadLetterQueueAndContinuesWithHealthyBatch()
    {
        // Arrange
        const string failedType = "real-broker.integration.failed";
        var healthyType = "real-broker.integration.healthy";
        var queueName = CreateQueueName();
        await EnsureDatabaseMigratedAsync();
        using var factory = CreateFactory(
            consumerCount: 1,
            queueName,
            services => services.AddScoped<IEventChunkStore>(serviceProvider =>
                new FailingEventChunkStore(
                    serviceProvider.GetRequiredService<PulseFlowDbContext>(),
                    failedType)));
        var options = factory.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        using var client = factory.CreateClient();
        await WaitUntilReadyAsync(factory);
        var failedBatch = CreateRecordJson(failedType) + "\n";
        using var failedContent = CreateNdjsonContent(failedBatch);

        // Act
        using var failedResponse = await client.PostAsync("/api/events", failedContent);
        var deadLetteredBody = await WaitForDeadLetterBodyAsync(options.DeadLetterQueueName);
        using var healthyContent = CreateNdjsonContent(new[] { healthyType });
        using var healthyResponse = await client.PostAsync("/api/events", healthyContent);
        var persistedTypes = await WaitForPersistedTypesAsync(new[] { healthyType });
        var mainQueueInfo = await GetQueueInfoAsync(queueName);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, failedResponse.StatusCode);
        Assert.Equal(Encoding.UTF8.GetBytes(failedBatch), deadLetteredBody);
        Assert.Equal(HttpStatusCode.Accepted, healthyResponse.StatusCode);
        Assert.Equal(new[] { healthyType }, persistedTypes);
        Assert.Equal(0U, mainQueueInfo.MessageCount);
    }

    #region Test helpers

    private PulseFlowSystemWebApplicationFactory CreateFactory(
        int consumerCount,
        string queueName,
        Action<IServiceCollection>? configureTestServices = null)
    {
        return new PulseFlowSystemWebApplicationFactory(
            _postgreSqlFixture.ConnectionString,
            _rabbitMqFixture.ConnectionString,
            _redisFixture.ConnectionString,
            queueName,
            consumerCount,
            configureTestServices);
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

    private static ByteArrayContent CreateNdjsonContent(string batch)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(batch));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-ndjson");

        return content;
    }

    private async Task EnsureDatabaseMigratedAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task WaitUntilReadyAsync(PulseFlowSystemWebApplicationFactory factory)
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var readinessState = factory.Services.GetRequiredService<StartupReadinessState>();
        await readinessState.WaitUntilReadyAsync(cancellationSource.Token);
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

    private async Task<QueueDeclareOk> WaitForConsumerCountAsync(
        string queueName,
        uint expectedCount)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeoutSource.IsCancellationRequested)
        {
            var queueInfo = await GetQueueInfoAsync(queueName);

            if (queueInfo.ConsumerCount == expectedCount)
            {
                return queueInfo;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"Queue '{queueName}' did not reach {expectedCount} consumers before the timeout.");
    }

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_postgreSqlFixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private async Task<byte[]> WaitForDeadLetterBodyAsync(string deadLetterQueueName)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqFixture.ConnectionString)
        };
        await using var connection = await connectionFactory.CreateConnectionAsync(timeoutSource.Token);
        await using var channel = await connection.CreateChannelAsync(
            cancellationToken: timeoutSource.Token);

        while (!timeoutSource.IsCancellationRequested)
        {
            var delivery = await channel.BasicGetAsync(
                deadLetterQueueName,
                autoAck: true,
                timeoutSource.Token);

            if (delivery is not null)
            {
                return delivery.Body.ToArray();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeoutSource.Token);
        }

        throw new TimeoutException("The failed batch did not reach the dead-letter queue before the timeout.");
    }

    private async Task<QueueDeclareOk> GetQueueInfoAsync(string queueName)
    {
        var connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqFixture.ConnectionString)
        };
        await using var connection = await connectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        return await channel.QueueDeclarePassiveAsync(queueName);
    }

    private static string CreateRecordJson(string type)
    {
        return JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid(),
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

    private sealed class FailingEventChunkStore : IEventChunkStore
    {
        private readonly PulseFlowDbContext _dbContext;
        private readonly string _failedType;

        public FailingEventChunkStore(PulseFlowDbContext dbContext, string failedType)
        {
            _dbContext = dbContext;
            _failedType = failedType;
        }

        public Task StoreAsync(
            IReadOnlyCollection<PulseFlow.Api.Ingestion.Contracts.EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            if (events.Any(@event => @event.Type == _failedType))
            {
                throw new InvalidOperationException("The test store failed the batch.");
            }

            return new EfCoreEventChunkStore(
                _dbContext,
                Microsoft.Extensions.Options.Options.Create(new PostgreSqlOptions()))
                .StoreAsync(events, cancellationToken);
        }
    }

    #endregion
}
