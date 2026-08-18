using Microsoft.AspNetCore.Mvc;
using PulseFlow.Api.Ingestion.Messaging;

namespace PulseFlow.Api.Ingestion.Http;

[ApiController]
[Route("api/events")]
[Consumes("application/x-ndjson")]
public sealed class EventsController : ControllerBase
{
    private readonly IIngestionBatchPublisher _publisher;

    public EventsController(IIngestionBatchPublisher publisher)
    {
        _publisher = publisher;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> IngestAsync(
        CancellationToken cancellationToken)
    {
        await using var batchStream = new MemoryStream();
        await Request.Body.CopyToAsync(batchStream, cancellationToken);

        await _publisher.PublishAsync(
            batchStream.ToArray(),
            cancellationToken);

        return Accepted();
    }
}
