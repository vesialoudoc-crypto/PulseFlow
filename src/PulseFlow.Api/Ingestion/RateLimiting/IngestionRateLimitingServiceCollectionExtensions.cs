using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PulseFlow.Api.Startup;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public static class IngestionRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        string? redisConnectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Connection string 'Redis' is required.");
        }

        services
            .AddOptions<IngestionRateLimitOptions>()
            .Bind(configuration.GetRequiredSection(IngestionRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.WindowDuration > TimeSpan.Zero,
                "IngestionRateLimit:WindowDuration must be greater than zero."
            )
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);

            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<RedisConnectionState>();
        services.AddSingleton<IIngestionRateLimiter, RedisIngestionRateLimiter>();

        if (!environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<RedisStartupInitializer>();
            services.AddSingleton<IStartupInitializer>(serviceProvider =>
                serviceProvider.GetRequiredService<RedisStartupInitializer>()
            );
            services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        }

        return services;
    }
}
