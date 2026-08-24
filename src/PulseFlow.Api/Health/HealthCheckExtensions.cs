using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Health;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddPulseFlowHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();

        return services;
    }

    public static WebApplication MapPulseFlowHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        return app;
    }
}
