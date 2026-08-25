namespace PulseFlow.Api.Startup;

public static class StartupServiceCollectionExtensions
{
    public static IServiceCollection AddStartupInitialization(this IServiceCollection services)
    {
        services.AddSingleton<StartupReadinessState>();
        services.AddHostedService<StartupInitializationService>();

        return services;
    }
}
