using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Health;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddPulseFlowHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<StartupReadinessHealthCheck>("startup-readiness", tags: ["ready"]);

        return services;
    }

    public static WebApplication MapPulseFlowHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") }
        );

        return app;
    }
}
