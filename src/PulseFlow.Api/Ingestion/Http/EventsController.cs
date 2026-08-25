using System.Buffers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;

namespace PulseFlow.Api.Ingestion.Http;

[ApiController]
[Route("api/events")]
[Consumes("application/x-ndjson")]
[DisableRequestSizeLimit]
public sealed class EventsController : ControllerBase
{
    private const int InitialBatchBufferBytes = 16 * 1024;
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

        var maximumBatchBytes = checked((int)_maxBatchBytes);
        var batchCapacity = Math.Min(InitialBatchBufferBytes, maximumBatchBytes);
        var batch = ArrayPool<byte>.Shared.Rent(batchCapacity);
        var batchLength = 0;

        try
        {
            while (batchLength < maximumBatchBytes)
            {
                if (batchLength == batchCapacity)
                {
                    var expandedBatchCapacity = Math.Min(checked(batchCapacity * 2), maximumBatchBytes);
                    var expandedBatch = ArrayPool<byte>.Shared.Rent(expandedBatchCapacity);

                    batch.AsSpan(0, batchLength).CopyTo(expandedBatch);
                    ArrayPool<byte>.Shared.Return(batch);
                    batch = expandedBatch;
                    batchCapacity = expandedBatchCapacity;
                }

                var bytesToRead = Math.Min(batchCapacity - batchLength, maximumBatchBytes - batchLength);
                var read = await Request.Body.ReadAsync(batch.AsMemory(batchLength, bytesToRead), ct);
                if (read == 0)
                {
                    break;
                }

                batchLength += read;
            }

            if (batchLength == maximumBatchBytes)
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
        finally
        {
            ArrayPool<byte>.Shared.Return(batch);
        }
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
