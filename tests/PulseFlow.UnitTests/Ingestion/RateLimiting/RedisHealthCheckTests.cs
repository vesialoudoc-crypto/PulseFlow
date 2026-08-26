using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Ingestion.RateLimiting;
using StackExchange.Redis;

namespace PulseFlow.UnitTests.Ingestion.RateLimiting;

public sealed class RedisHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_PingSucceeds_ReturnsHealthy()
    {
        // Arrange
        var healthCheck = CreateHealthCheck(Task.FromResult(TimeSpan.Zero));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_PingFails_ReturnsUnhealthy()
    {
        // Arrange
        var healthCheck = CreateHealthCheck(Task.FromException<TimeSpan>(new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            CommandFlags.None,
            "Redis test failure.",
            null,
            CommandStatus.Unknown)));

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_CallerCancels_PropagatesOperationCanceledException()
    {
        // Arrange
        var completion = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthCheck = CreateHealthCheck(completion.Task);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token));

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    #region Test helpers

    private static RedisHealthCheck CreateHealthCheck(Task<TimeSpan> pingTask)
    {
        var database = DispatchProxy.Create<IDatabase, PingRedisDatabase>();
        ((PingRedisDatabase)(object)database).PingTask = pingTask;
        var connectionMultiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionMultiplexer>();
        ((RedisConnectionMultiplexer)(object)connectionMultiplexer).Database = database;
        var connectionState = new RedisConnectionState();
        connectionState.SetConnectionMultiplexer(connectionMultiplexer);

        return new RedisHealthCheck(connectionState, NullLogger<RedisHealthCheck>.Instance);
    }

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

    private class PingRedisDatabase : DispatchProxy
    {
        public Task<TimeSpan>? PingTask { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "PingAsync" => PingTask,
                _ => throw new NotSupportedException($"Unexpected database call: {targetMethod?.Name}."),
            };
        }
    }

    #endregion
}
