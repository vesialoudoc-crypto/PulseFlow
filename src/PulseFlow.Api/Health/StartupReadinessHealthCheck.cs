using Microsoft.Extensions.Diagnostics.HealthChecks;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Health;

public sealed class StartupReadinessHealthCheck : IHealthCheck
{
    private readonly StartupReadinessState _readinessState;

    public StartupReadinessHealthCheck(StartupReadinessState readinessState)
    {
        _readinessState = readinessState;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var result =
            _readinessState.Status == StartupReadinessStatus.Ready
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Mandatory startup initialization has not completed.");

        return Task.FromResult(result);
    }
}
