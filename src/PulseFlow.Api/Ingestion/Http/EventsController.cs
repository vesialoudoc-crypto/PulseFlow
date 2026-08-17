using Microsoft.AspNetCore.Mvc;
using PulseFlow.Api.Ingestion.Ndjson;

namespace PulseFlow.Api.Ingestion.Http;

[ApiController]
[Route("api/events")]
[Consumes("application/x-ndjson")]
public sealed class EventsController : ControllerBase
{
    private readonly NdjsonRecordReader _reader;
    private readonly IngestEventsHandler _handler;

    public EventsController(
        NdjsonRecordReader reader,
        IngestEventsHandler handler)
    {
        _reader = reader;
        _handler = handler;
    }

    [HttpPost]
    [ProducesResponseType<IngestEventsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IngestEventsResult>> IngestAsync(
        CancellationToken cancellationToken)
    {
        var records = _reader.ReadAsync(
            Request.Body,
            cancellationToken);

        var result = await _handler.HandleAsync(
            records,
            cancellationToken);

        return Ok(result);
    }
}