using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public static class IngestionRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration
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

        return services;
    }
}
