using Microsoft.Extensions.Diagnostics.HealthChecks;
using PulseFlow.Api.Startup;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public static class IngestionRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        TimeSpan readinessCheckTimeout
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
        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetRequiredSection(RedisOptions.SectionName))
            .Validate(
                options =>
                    options.ConnectTimeout > TimeSpan.Zero && options.ConnectTimeout.TotalMilliseconds <= int.MaxValue,
                $"Redis:ConnectTimeout must be greater than zero and no more than {int.MaxValue} milliseconds."
            )
            .Validate(
                options =>
                    options.AsyncTimeout > TimeSpan.Zero && options.AsyncTimeout.TotalMilliseconds <= int.MaxValue,
                $"Redis:AsyncTimeout must be greater than zero and no more than {int.MaxValue} milliseconds."
            )
            .ValidateOnStart();

        var timeoutOptions =
            configuration.GetRequiredSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = timeoutOptions.ConnectTimeoutMilliseconds;
        redisOptions.AsyncTimeout = timeoutOptions.AsyncTimeoutMilliseconds;

        services.AddSingleton<RedisConnectionState>();
        services.AddSingleton(new RedisConnectionMultiplexerFactory(redisOptions));
        services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
            serviceProvider.GetRequiredService<RedisConnectionState>().GetRequiredConnectionMultiplexer()
        );
        services.AddSingleton<IIngestionRateLimiter, RedisIngestionRateLimiter>();

        services.AddSingleton<RedisStartupInitializer>();
        services.AddSingleton<IStartupInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<RedisStartupInitializer>()
        );
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"], timeout: readinessCheckTimeout);

        return services;
    }
}
