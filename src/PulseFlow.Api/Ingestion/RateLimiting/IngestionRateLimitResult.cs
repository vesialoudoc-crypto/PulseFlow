namespace PulseFlow.Api.Ingestion.RateLimiting;

public class IngestionRateLimitResult
{
    public IngestionRateLimitStatus Status { get; }
    public TimeSpan RetryAfter { get; }

    public IngestionRateLimitResult(IngestionRateLimitStatus status, TimeSpan? retryAfter = default)
    {
        Status = status;
        RetryAfter = retryAfter ?? TimeSpan.Zero;
    }
}
