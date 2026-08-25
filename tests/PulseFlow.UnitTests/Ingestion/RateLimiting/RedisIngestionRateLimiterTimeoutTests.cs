using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.RateLimiting;
using StackExchange.Redis;

namespace PulseFlow.UnitTests.Ingestion.RateLimiting;

public sealed class RedisIngestionRateLimiterTimeoutTests
{
    [Fact]
    public async Task TryAllowAsync_ScriptEvaluationExceedsAsyncTimeout_ReturnsUnavailable()
    {
        // Arrange
        var database = DispatchProxy.Create<IDatabase, BlockingRedisDatabase>();
        var connectionMultiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionMultiplexer>();
        ((RedisConnectionMultiplexer)(object)connectionMultiplexer).Database = database;
        var limiter = new RedisIngestionRateLimiter(
            connectionMultiplexer,
            Options.Create(
                new IngestionRateLimitOptions { RequestLimit = 1, WindowDuration = TimeSpan.FromMinutes(1) }),
            Options.Create(new RedisOptions { AsyncTimeout = TimeSpan.FromMilliseconds(25) }),
            NullLogger<RedisIngestionRateLimiter>.Instance);

        // Act
        var result = await limiter.TryAllowAsync(CancellationToken.None);

        // Assert
        Assert.Equal(IngestionRateLimitStatus.Unavailable, result.Status);
    }

    #region Test helpers

    private class RedisConnectionMultiplexer : DispatchProxy
    {
        public IDatabase? Database { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "GetDatabase" => Database,
                _ => throw new NotSupportedException($"Unexpected multiplexer call: {targetMethod?.Name}."),
            };
        }
    }

    private class BlockingRedisDatabase : DispatchProxy
    {
        private readonly TaskCompletionSource<RedisResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "ScriptEvaluateAsync" => _completion.Task,
                _ => throw new NotSupportedException($"Unexpected database call: {targetMethod?.Name}."),
            };
        }
    }

    #endregion
}
