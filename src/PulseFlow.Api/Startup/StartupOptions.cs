namespace PulseFlow.Api.Startup;

public sealed class StartupOptions
{
    public const string SectionName = "Startup";

    public TimeSpan InitializationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
