namespace PulseFlow.Api.Ingestion.RateLimiting;

public enum IngestionRateLimitStatus
{
    Allowed = 0,
    Exceeded,
    Unavailable,
}
