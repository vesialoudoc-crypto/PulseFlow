namespace PulseFlow.Api.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
