using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Health;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddPulseFlowHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var readinessOptions =
            configuration.GetRequiredSection(ReadinessOptions.SectionName).Get<ReadinessOptions>()
            ?? new ReadinessOptions();
        services
            .AddOptions<ReadinessOptions>()
            .Bind(configuration.GetRequiredSection(ReadinessOptions.SectionName))
            .Validate(options => options.Timeout > TimeSpan.Zero, "HealthChecks:Timeout must be greater than zero.")
            .Validate(
                options => options.Timeout.TotalMilliseconds <= int.MaxValue,
                $"HealthChecks:Timeout must be no more than {int.MaxValue} milliseconds."
            )
            .ValidateOnStart();
        services
            .AddHealthChecks()
            .AddCheck<StartupReadinessHealthCheck>(
                "startup-readiness",
                tags: ["ready"],
                timeout: readinessOptions.Timeout
            );

        return services;
    }

    public static TimeSpan GetReadinessTimeout(IConfiguration configuration)
    {
        return configuration.GetRequiredSection(ReadinessOptions.SectionName).Get<ReadinessOptions>()?.Timeout
            ?? new ReadinessOptions().Timeout;
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
