namespace PulseFlow.Api.Ingestion.RateLimiting;

public interface IIngestionRateLimiter
{
    Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct);
}
