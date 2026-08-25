using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPulseFlowPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PulseFlowDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<PostgreSqlStartupInitializer>();
        services.AddSingleton<IStartupInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<PostgreSqlStartupInitializer>()
        );
        services.AddHealthChecks().AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);

        return services;
    }
}
