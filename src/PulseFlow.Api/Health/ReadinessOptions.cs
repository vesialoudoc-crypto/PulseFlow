namespace PulseFlow.Api.Health;

public sealed class ReadinessOptions
{
    public const string SectionName = "HealthChecks";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(2);
}
