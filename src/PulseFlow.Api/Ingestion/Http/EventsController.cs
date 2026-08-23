using Microsoft.AspNetCore.Mvc;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;

namespace PulseFlow.Api.Ingestion.Http;

[ApiController]
[Route("api/events")]
[Consumes("application/x-ndjson")]
public sealed class EventsController : ControllerBase
{
    private readonly IIngestionBatchPublisher _publisher;
    private readonly IIngestionRateLimiter _rateLimiter;

    public EventsController(IIngestionBatchPublisher publisher, IIngestionRateLimiter rateLimiter)
    {
        _publisher = publisher;
        _rateLimiter = rateLimiter;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> IngestAsync(CancellationToken ct)
    {
        var rateLimiterResult = await _rateLimiter.TryAllowAsync(ct);
        if (rateLimiterResult.Status == IngestionRateLimitStatus.Exceeded)
        {
            int retryAfter = (int)Math.Ceiling(rateLimiterResult.RetryAfter.TotalSeconds);
            Response.Headers.RetryAfter = retryAfter.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (rateLimiterResult.Status != IngestionRateLimitStatus.Allowed)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        await using var batchStream = new MemoryStream();
        await Request.Body.CopyToAsync(batchStream, ct);

        await _publisher.PublishAsync(batchStream.ToArray(), ct);

        return Accepted();
    }
}
