using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.RateLimiting;
using StackExchange.Redis;

namespace PulseFlow.UnitTests.Ingestion.RateLimiting;

public sealed class RedisIngestionRateLimiterTimeoutTests
{
    [Fact]
    public async Task TryAllowAsync_ProviderTimesOutScriptEvaluation_ReturnsUnavailable()
    {
        // Arrange
        var database = DispatchProxy.Create<IDatabase, TimeoutRedisDatabase>();
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

    [Fact]
    public async Task TryAllowAsync_CallerCancelsWhileScriptIsRunning_PropagatesCancellation()
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
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            limiter.TryAllowAsync(cancellationSource.Token));

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
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

    private class TimeoutRedisDatabase : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "ScriptEvaluateAsync" => Task.FromException<RedisResult>(
                    new RedisTimeoutException(
                        CommandFlags.None,
                        "Redis provider timeout.",
                        CommandStatus.Unknown)),
                _ => throw new NotSupportedException($"Unexpected database call: {targetMethod?.Name}."),
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
