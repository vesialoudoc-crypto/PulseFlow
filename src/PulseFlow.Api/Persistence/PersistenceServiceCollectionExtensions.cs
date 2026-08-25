using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPulseFlowPersistence(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        TimeSpan readinessCheckTimeout
    )
    {
        services
            .AddOptions<PostgreSqlOptions>()
            .Bind(configuration.GetRequiredSection(PostgreSqlOptions.SectionName))
            .Validate(
                options =>
                    options.ConnectionTimeout > TimeSpan.Zero && options.ConnectionTimeout.TotalSeconds <= int.MaxValue,
                $"PostgreSql:ConnectionTimeout must be greater than zero and no more than {int.MaxValue} seconds."
            )
            .Validate(
                options =>
                    options.CommandTimeout > TimeSpan.Zero && options.CommandTimeout.TotalSeconds <= int.MaxValue,
                $"PostgreSql:CommandTimeout must be greater than zero and no more than {int.MaxValue} seconds."
            )
            .ValidateOnStart();
        var timeoutOptions =
            configuration.GetRequiredSection(PostgreSqlOptions.SectionName).Get<PostgreSqlOptions>()
            ?? new PostgreSqlOptions();
        var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = timeoutOptions.ConnectionTimeoutSeconds,
            CommandTimeout = timeoutOptions.CommandTimeoutSeconds,
        };

        services.AddDbContext<PulseFlowDbContext>(options =>
            options.UseNpgsql(
                connectionStringBuilder.ConnectionString,
                npgsqlOptions => npgsqlOptions.CommandTimeout(timeoutOptions.CommandTimeoutSeconds)
            )
        );

        services.AddSingleton<PostgreSqlStartupInitializer>();
        services.AddSingleton<IStartupInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<PostgreSqlStartupInitializer>()
        );
        services
            .AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"], timeout: readinessCheckTimeout);

        return services;
    }
}
