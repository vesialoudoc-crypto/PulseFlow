using System.Text.Json;

namespace PulseFlow.Api.Ingestion.Ndjson;

public sealed class NdjsonRecordResult
{
    internal NdjsonRecordResult(long recordNumber, JsonElement? parsedJson)
    {
        RecordNumber = recordNumber;
        ParsedJson = parsedJson;
    }

    public long RecordNumber { get; }

    public bool IsMalformed => ParsedJson is null;

    public JsonElement? ParsedJson { get; }
}
