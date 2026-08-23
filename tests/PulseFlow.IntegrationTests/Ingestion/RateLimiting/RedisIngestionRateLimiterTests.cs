using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        if(_connectionMultiplexer is not null)
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

    #region Test helpers

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
                    WindowDuration = windowDuration
                }),
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

    #endregion
}
