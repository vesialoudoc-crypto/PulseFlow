using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PulseFlow.Api.Startup;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Health;

public sealed class StartupReadinessEndpointTests
{
    [Fact]
    public async Task GetHealthEndpoints_StartupIsRunning_ReturnsLiveOkAndReadyServiceUnavailable()
    {
        // Arrange
        var initializer = new BlockingStartupInitializer();
        await using var host = await CreateHostAsync(initializer);
        await initializer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        using var liveResponse = await host.Client.GetAsync("/health/live");
        using var readyResponse = await host.Client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
    }

    [Fact]
    public async Task GetReady_StartupCompletedSuccessfully_ReturnsOk()
    {
        // Arrange
        var initializer = new BlockingStartupInitializer();
        await using var host = await CreateHostAsync(initializer);
        await initializer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        initializer.Complete();
        var readinessState = host.Services.GetRequiredService<StartupReadinessState>();
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await readinessState.WaitUntilReadyAsync(cancellationSource.Token);
        using var response = await host.Client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartupInitializationService_MandatoryInitializerFails_MarksFailedAndStopsHost()
    {
        // Arrange
        var initializer = new FailingStartupInitializer();
        await using var host = await CreateHostAsync(initializer);
        await initializer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var readinessState = host.Services.GetRequiredService<StartupReadinessState>();
        var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // Act
        initializer.Fail();
        await Task.WhenAny(
            Task.Delay(Timeout.InfiniteTimeSpan, applicationLifetime.ApplicationStopping),
            Task.Delay(TimeSpan.FromSeconds(5))
        );

        // Assert
        Assert.Equal(StartupReadinessStatus.Failed, readinessState.Status);
        Assert.True(applicationLifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task GetReady_RuntimeDependencyFailsAfterStartup_ReturnsServiceUnavailableWithoutRerunningInitializers()
    {
        // Arrange
        var initializer = new SuccessfulStartupInitializer();
        var runtimeDependency = new ToggleableRuntimeHealthCheck();
        await using var host = await CreateHostAsync(initializer, runtimeDependency);
        var readinessState = host.Services.GetRequiredService<StartupReadinessState>();
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await readinessState.WaitUntilReadyAsync(cancellationSource.Token);
        runtimeDependency.IsHealthy = false;

        // Act
        using var response = await host.Client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StartupReadinessStatus.Ready, readinessState.Status);
        Assert.Equal(1, initializer.InitializeCallCount);
    }

    #region Test helpers

    private static Task<PulseFlowComponentTestHost> CreateHostAsync(
        IStartupInitializer initializer,
        ToggleableRuntimeHealthCheck? runtimeDependency = null)
    {
        return PulseFlowComponentTestHost.StartAsync(services =>
        {
            services.AddSingleton(initializer);

            if (runtimeDependency is not null)
            {
                services.AddSingleton(runtimeDependency);
                services.AddHealthChecks().AddCheck<ToggleableRuntimeHealthCheck>(
                    "test-runtime-dependency",
                    tags: ["ready"]);
            }
        });
    }

    private sealed class BlockingStartupInitializer : IStartupInitializer
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete()
        {
            _completion.TrySetResult();
        }
    }

    private sealed class FailingStartupInitializer : IStartupInitializer
    {
        private readonly TaskCompletionSource _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _failure.Task.WaitAsync(cancellationToken);
        }

        public void Fail()
        {
            _failure.TrySetException(new InvalidOperationException("Startup initializer test failure."));
        }
    }

    private sealed class SuccessfulStartupInitializer : IStartupInitializer
    {
        public int InitializeCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            InitializeCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ToggleableRuntimeHealthCheck : IHealthCheck
    {
        public bool IsHealthy { get; set; } = true;

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken ct = default)
        {
            var result = IsHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
            return Task.FromResult(result);
        }
    }

    #endregion
}