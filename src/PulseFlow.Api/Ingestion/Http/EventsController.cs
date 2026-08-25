using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;
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
    private readonly long _maxBatchBytes;

    public EventsController(
        IIngestionBatchPublisher publisher,
        IIngestionRateLimiter rateLimiter,
        IOptions<IngestionOptions> options
    )
    {
        _publisher = publisher;
        _rateLimiter = rateLimiter;
        _maxBatchBytes = options.Value.MaxBatchBytes;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
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

        if (Request.ContentLength is long contentLength && contentLength > _maxBatchBytes)
        {
            return CreatePayloadTooLargeProblem();
        }

        var batch = new byte[checked((int)_maxBatchBytes)];
        var batchLength = 0;

        while (batchLength < batch.Length)
        {
            var read = await Request.Body.ReadAsync(batch.AsMemory(batchLength), ct);
            if (read == 0)
            {
                break;
            }

            batchLength += read;
        }

        if (batchLength == batch.Length)
        {
            var probe = new byte[1];
            var read = await Request.Body.ReadAsync(probe, ct);

            if (read != 0)
            {
                return CreatePayloadTooLargeProblem();
            }
        }

        await _publisher.PublishAsync(batch.AsMemory(0, batchLength), ct);

        return Accepted();
    }

    private ObjectResult CreatePayloadTooLargeProblem()
    {
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "Payload Too Large",
            Detail = "The request body exceeds the maximum allowed batch size.",
        };
        problemDetails.Extensions["traceId"] = HttpContext.TraceIdentifier;

        var result = new ObjectResult(problemDetails) { StatusCode = StatusCodes.Status413PayloadTooLarge };
        result.ContentTypes.Add("application/problem+json");

        return result;
    }
}
