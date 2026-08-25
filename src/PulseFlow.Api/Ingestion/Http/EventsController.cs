using Microsoft.AspNetCore.Mvc;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;

namespace PulseFlow.Api.Ingestion.Http;

[ApiController]
[Route("api/events")]
[Consumes("application/x-ndjson")]
[DisableRequestSizeLimit]
public sealed class EventsController : ControllerBase
{
    private readonly IIngestionBatchBodyReader _bodyReader;
    private readonly IIngestionBatchPublisher _publisher;
    private readonly IIngestionRateLimiter _rateLimiter;

    public EventsController(
        IIngestionBatchPublisher publisher,
        IIngestionRateLimiter rateLimiter,
        IIngestionBatchBodyReader bodyReader
    )
    {
        _publisher = publisher;
        _rateLimiter = rateLimiter;
        _bodyReader = bodyReader;
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

        var readResult = await _bodyReader.ReadAsync(Request.Body, Request.ContentLength, ct);
        using (readResult)
        {
            if (readResult.IsTooLarge)
            {
                return CreatePayloadTooLargeProblem();
            }

            await _publisher.PublishAsync(readResult.Payload, ct);
        }

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
