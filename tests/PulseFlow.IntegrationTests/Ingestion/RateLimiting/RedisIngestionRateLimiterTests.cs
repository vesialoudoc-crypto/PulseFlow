using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;
using PulseFlow.IntegrationTests.Infrastructure;
using StackExchange.Redis;

namespace PulseFlow.IntegrationTests.Ingestion.RateLimiting;

public sealed class RedisIngestionRateLimiterTests : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string GlobalKey = "pulseflow:rate-limit:ingestion:global";
    private readonly RedisFixture _fixture;
    private IConnectionMultiplexer? _connectionMultiplexer;

    public RedisIngestionRateLimiterTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        await _connectionMultiplexer.GetDatabase().KeyDeleteAsync(GlobalKey);
    }

    public async Task DisposeAsync()
    {
        if (_connectionMultiplexer is not null)
        {
            await _connectionMultiplexer.GetDatabase().KeyDeleteAsync(GlobalKey);
            _connectionMultiplexer.Dispose();
        }
    }

    [Fact]
    public async Task TryAllowAsync_RequestCountIsBelowLimit_ReturnsAllowed()
    {
        // Arrange
        var windowDuration = TimeSpan.FromSeconds(10);
        var limiter = CreateLimiter(requestLimit: 2, windowDuration);
        await limiter.TryAllowAsync(CancellationToken.None);
        var initialTtl = await GetDatabase().KeyTimeToLiveAsync(GlobalKey);

        // Act
        var result = await limiter.TryAllowAsync(CancellationToken.None);

        // Assert
        Assert.Equal(IngestionRateLimitStatus.Allowed, result.Status);
        var remainingTtl = await GetDatabase().KeyTimeToLiveAsync(GlobalKey);
        Assert.NotNull(initialTtl);
        Assert.NotNull(remainingTtl);
        Assert.InRange(remainingTtl.Value, TimeSpan.FromMilliseconds(1), windowDuration);
        Assert.True(remainingTtl.Value <= initialTtl.Value);
    }

    [Fact]
    public async Task TryAllowAsync_RequestLimitReached_ReturnsExceededWithRetryAfter()
    {
        // Arrange
        var windowDuration = TimeSpan.FromSeconds(10);
        var limiter = CreateLimiter(requestLimit: 1, windowDuration);
        await limiter.TryAllowAsync(CancellationToken.None);

        // Act
        var result = await limiter.TryAllowAsync(CancellationToken.None);

        // Assert
        Assert.Equal(IngestionRateLimitStatus.Exceeded, result.Status);
        Assert.InRange(result.RetryAfter, TimeSpan.FromMilliseconds(1), windowDuration);
    }

    [Fact]
    public async Task TryAllowAsync_RedisOperationFails_ReturnsUnavailable()
    {
        // Arrange
        var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var limiter = CreateLimiter(
            connectionMultiplexer,
            requestLimit: 1,
            TimeSpan.FromSeconds(10));
        connectionMultiplexer.Dispose();

        // Act
        var result = await limiter.TryAllowAsync(CancellationToken.None);

        // Assert
        Assert.Equal(IngestionRateLimitStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task PostEvents_TwoApiHostsShareRedisQuota_RejectsFifthRequest()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var apiAHost = await CreateApiHostAsync(publisher);
        await using var apiBHost = await CreateApiHostAsync(publisher);

        // Act
        using var firstResponse = await PostEventsAsync(apiAHost.Client);
        using var secondResponse = await PostEventsAsync(apiBHost.Client);
        using var thirdResponse = await PostEventsAsync(apiAHost.Client);
        using var fourthResponse = await PostEventsAsync(apiBHost.Client);
        using var fifthResponse = await PostEventsAsync(apiAHost.Client);

        // Assert
        Assert.All(
            new[] { firstResponse, secondResponse, thirdResponse, fourthResponse },
            response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, fifthResponse.StatusCode);
        Assert.NotNull(fifthResponse.Headers.RetryAfter);
        Assert.Equal(4, publisher.PublishCallCount);
    }

    #region Test helpers

    private Task<PulseFlowComponentTestHost> CreateApiHostAsync(IIngestionBatchPublisher publisher)
    {
        return PulseFlowComponentTestHost.StartAsync(services =>
        {
            // Each host owns its own multiplexer, matching independent API process lifetimes.
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(_fixture.ConnectionString));
            services.AddSingleton<IOptions<IngestionRateLimitOptions>>(
                Options.Create(
                    new IngestionRateLimitOptions
                    {
                        RequestLimit = 4,
                        WindowDuration = TimeSpan.FromMinutes(1),
                    }
                )
            );
            services.AddSingleton<IIngestionRateLimiter, RedisIngestionRateLimiter>();
            services.AddSingleton(publisher);
        });
    }

    private static async Task<HttpResponseMessage> PostEventsAsync(HttpClient client)
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-ndjson");

        return await client.PostAsync("/api/events", content);
    }

    private RedisIngestionRateLimiter CreateLimiter(int requestLimit, TimeSpan windowDuration)
    {
        return CreateLimiter(GetConnectionMultiplexer(), requestLimit, windowDuration);
    }

    private static RedisIngestionRateLimiter CreateLimiter(
        IConnectionMultiplexer connectionMultiplexer,
        int requestLimit,
        TimeSpan windowDuration)
    {
        return new RedisIngestionRateLimiter(
            connectionMultiplexer,
            Options.Create(
                new IngestionRateLimitOptions
                {
                    RequestLimit = requestLimit,
                    WindowDuration = windowDuration,
                }
            ),
            NullLogger<RedisIngestionRateLimiter>.Instance
        );
    }

    private IDatabase GetDatabase()
    {
        return GetConnectionMultiplexer().GetDatabase();
    }

    private IConnectionMultiplexer GetConnectionMultiplexer()
    {
        return _connectionMultiplexer
            ?? throw new InvalidOperationException("The Redis test connection has not been initialized.");
    }

    private sealed class RecordingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        public int PublishCallCount { get; private set; }

        public Task PublishAsync(
            ReadOnlyMemory<byte> rawBatch,
            CancellationToken cancellationToken)
        {
            PublishCallCount++;

            return Task.CompletedTask;
        }
    }

    #endregion
}