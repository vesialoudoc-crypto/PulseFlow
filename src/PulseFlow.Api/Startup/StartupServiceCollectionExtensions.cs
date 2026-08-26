namespace PulseFlow.Api.Startup;

public static class StartupServiceCollectionExtensions
{
    public static IServiceCollection AddStartupInitialization(this IServiceCollection services)
    {
        services.AddOptions<StartupOptions>();
        return AddStartupInitializationCore(services);
    }

    public static IServiceCollection AddStartupInitialization(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<StartupOptions>()
            .Bind(configuration.GetRequiredSection(StartupOptions.SectionName))
            .Validate(
                options => options.InitializationTimeout > TimeSpan.Zero,
                "Startup:InitializationTimeout must be greater than zero."
            )
            .Validate(
                options => options.InitializationTimeout.TotalMilliseconds <= int.MaxValue,
                $"Startup:InitializationTimeout must be no more than {int.MaxValue} milliseconds."
            )
            .ValidateOnStart();
        return AddStartupInitializationCore(services);
    }

    private static IServiceCollection AddStartupInitializationCore(IServiceCollection services)
    {
        services.AddSingleton<StartupReadinessState>();
        services.AddHostedService<StartupInitializationService>();

        return services;
    }
}
