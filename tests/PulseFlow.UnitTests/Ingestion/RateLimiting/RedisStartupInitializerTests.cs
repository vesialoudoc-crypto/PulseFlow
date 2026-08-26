using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.RateLimiting;
using StackExchange.Redis;

namespace PulseFlow.UnitTests.Ingestion.RateLimiting;

public sealed class RedisStartupInitializerTests
{
    [Fact]
    public async Task InitializeAsync_StartupCancellationDuringConnection_DisposesLateConnectionWithoutDelayingCancellation()
    {
        // Arrange
        var connectionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCompletion = new TaskCompletionSource<IConnectionMultiplexer>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionMultiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionMultiplexer>();
        var connectionProxy = (RedisConnectionMultiplexer)(object)connectionMultiplexer;
        var connectionFactory = new RedisConnectionMultiplexerFactory(() =>
        {
            connectionStarted.TrySetResult();
            return connectionCompletion.Task;
        });
        var connectionState = new RedisConnectionState();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var initializer = new RedisStartupInitializer(
            serviceProvider,
            connectionFactory,
            connectionState,
            Options.Create(new RedisOptions()),
            NullLogger<RedisStartupInitializer>.Instance);
        using var cancellationSource = new CancellationTokenSource();
        var initialization = initializer.InitializeAsync(cancellationSource.Token);
        await connectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellationSource.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization)
            .WaitAsync(TimeSpan.FromSeconds(1));
        connectionCompletion.SetResult(connectionMultiplexer);
        await connectionProxy.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Null(connectionState.ConnectionMultiplexer);
    }

    #region Test helpers

    private class RedisConnectionMultiplexer : DispatchProxy
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "Dispose" => Dispose(),
                _ => throw new NotSupportedException($"Unexpected Redis call: {targetMethod?.Name}."),
            };
        }

        private object? Dispose()
        {
            Disposed.TrySetResult();
            return null;
        }
    }

    #endregion
}
