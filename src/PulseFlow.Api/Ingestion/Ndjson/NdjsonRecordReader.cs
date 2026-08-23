using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PulseFlow.Api.Ingestion.Ndjson;

public sealed class NdjsonRecordReader
{
    private const int ReadBufferSize = 4096;

    public async IAsyncEnumerable<NdjsonRecordResult> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Keep only a fixed-size input block and the bytes of the current record.
        byte[] readBuffer = new byte[ReadBufferSize];
        var currentRecord = new ArrayBufferWriter<byte>();
        long recordNumber = 0;

        while (true)
        {
            // Step 1: read the next block without waiting for the full upload.
            ct.ThrowIfCancellationRequested();

            int bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), ct);

            if (bytesRead == 0)
            {
                break;
            }

            // Step 2: append bytes to the current record until each LF boundary.
            int unprocessedStart = 0;

            while (unprocessedStart < bytesRead)
            {
                int lineFeedIndex = Array.IndexOf(
                    readBuffer,
                    (byte)'\n',
                    unprocessedStart,
                    bytesRead - unprocessedStart
                );

                int segmentEnd = lineFeedIndex < 0 ? bytesRead : lineFeedIndex;
                int segmentLength = segmentEnd - unprocessedStart;

                if (segmentLength > 0)
                {
                    readBuffer.AsSpan(unprocessedStart, segmentLength).CopyTo(currentRecord.GetSpan(segmentLength));
                    currentRecord.Advance(segmentLength);
                }

                if (lineFeedIndex < 0)
                {
                    break;
                }

                // Step 3: LF completes a record. Remove CR only when it is part of CRLF.
                var completedRecord = currentRecord.WrittenMemory;

                if (completedRecord.Length > 0 && completedRecord.Span[^1] == (byte)'\r')
                {
                    completedRecord = completedRecord[..^1];
                }

                recordNumber++;
                ct.ThrowIfCancellationRequested();

                // Step 4: parse this record once and yield parsed JSON or malformed input.
                yield return ParseRecord(completedRecord, recordNumber);

                currentRecord = new ArrayBufferWriter<byte>();
                unprocessedStart = lineFeedIndex + 1;
            }
        }

        // Step 5: clean end-of-stream also completes a non-empty final record.
        if (currentRecord.WrittenCount > 0)
        {
            recordNumber++;
            ct.ThrowIfCancellationRequested();

            yield return ParseRecord(currentRecord.WrittenMemory, recordNumber);
        }
    }

    private static NdjsonRecordResult ParseRecord(ReadOnlyMemory<byte> record, long recordNumber)
    {
        try
        {
            using var document = JsonDocument.Parse(record);

            return new NdjsonRecordResult(recordNumber, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return new NdjsonRecordResult(recordNumber, null);
        }
    }
}
