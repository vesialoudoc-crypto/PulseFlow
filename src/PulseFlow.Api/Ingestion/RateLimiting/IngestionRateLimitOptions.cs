using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class IngestionRateLimitOptions
{
    public const string SectionName = "IngestionRateLimit";

    [Range(1, int.MaxValue)]
    public int RequestLimit { get; init; }

    public TimeSpan WindowDuration { get; init; }
}
