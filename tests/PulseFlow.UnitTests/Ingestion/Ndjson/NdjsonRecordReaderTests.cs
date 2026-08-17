using System.Text;
using System.Text.Json;
using PulseFlow.Api.Ingestion.Ndjson;

namespace PulseFlow.UnitTests.Ingestion.Ndjson;

public sealed class NdjsonRecordReaderTests
{
    private readonly NdjsonRecordReader _reader = new();

    [Fact]
    public async Task ReadAsync_MultipleLfDelimitedRecords_YieldsParsedRecordsInOrder()
    {
        // Arrange
        var input = string.Join(
            '\n',
            CreateRecordJson(
                type: "payment.started",
                source: "billing-service",
                occurredAt: "2026-08-17T10:00:00Z",
                payload: new { sequence = 1 }),
            CreateRecordJson(
                type: "payment.authorized",
                source: "billing-service",
                occurredAt: "2026-08-17T10:01:00Z",
                payload: new { sequence = 2 }),
            CreateRecordJson(
                type: "payment.completed",
                source: "billing-service",
                occurredAt: "2026-08-17T10:02:00Z",
                payload: new { sequence = 3 }),
            string.Empty);
        var expected = new[]
        {
            (RecordNumber: 1L, Type: "payment.started", IsMalformed: false),
            (RecordNumber: 2L, Type: "payment.authorized", IsMalformed: false),
            (RecordNumber: 3L, Type: "payment.completed", IsMalformed: false)
        };

        // Act
        var results = await ReadAllAsync(input);

        // Assert
        var actual = results
            .Select(result =>
            {
                var parsedJson = Assert.IsType<JsonElement>(result.ParsedJson);
                var type = Assert.IsType<string>(
                    parsedJson.GetProperty("type").GetString());

                return (result.RecordNumber, type, result.IsMalformed);
            })
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAsync_CrlfDelimitedRecords_YieldsParsedRecordsInOrder()
    {
        // Arrange
        var input = string.Join(
            "\r\n",
            CreateRecordJson(
                type: "payment.started",
                source: "billing-service",
                occurredAt: "2026-08-17T10:00:00Z",
                payload: new { sequence = 1 }),
            CreateRecordJson(
                type: "payment.completed",
                source: "billing-service",
                occurredAt: "2026-08-17T10:01:00Z",
                payload: new { sequence = 2 }),
            string.Empty);
        var expected = new[]
        {
            (RecordNumber: 1L, Type: "payment.started"),
            (RecordNumber: 2L, Type: "payment.completed")
        };

        // Act
        var results = await ReadAllAsync(input);

        // Assert
        var actual = results
            .Select(result =>
            {
                var parsedJson = Assert.IsType<JsonElement>(result.ParsedJson);
                var type = Assert.IsType<string>(
                    parsedJson.GetProperty("type").GetString());

                return (result.RecordNumber, type);
            })
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAsync_MalformedRecordPrecedesValidRecord_YieldsBothOutcomesInOrder()
    {
        // Arrange
        var malformedRecord = CreateRecordJson(
            type: "payment.started",
            source: "billing-service",
            occurredAt: "2026-08-17T10:00:00Z",
            payload: new { sequence = 1 })[..^1];
        var validRecord = CreateRecordJson(
            type: "payment.completed",
            source: "billing-service",
            occurredAt: "2026-08-17T10:01:00Z",
            payload: new { sequence = 2 });
        var input = string.Join('\n', malformedRecord, validRecord, string.Empty);

        // Act
        var results = await ReadAllAsync(input);

        // Assert
        Assert.Equal(2, results.Length);

        var malformedResult = results[0];
        Assert.Equal(1, malformedResult.RecordNumber);
        Assert.True(malformedResult.IsMalformed);
        Assert.Null(malformedResult.ParsedJson);

        var validResult = results[1];
        Assert.Equal(2, validResult.RecordNumber);
        Assert.False(validResult.IsMalformed);

        var parsedJson = Assert.IsType<JsonElement>(validResult.ParsedJson);
        var type = Assert.IsType<string>(parsedJson.GetProperty("type").GetString());
        Assert.Equal("payment.completed", type);
    }

    [Fact]
    public async Task ReadAsync_RecordIsBlank_YieldsMalformedRecord()
    {
        // Arrange
        var input = string.Join('\n', string.Empty, string.Empty);
        var expected = new[]
        {
            (RecordNumber: 1L, IsMalformed: true, HasParsedJson: false)
        };

        // Act
        var results = await ReadAllAsync(input);
        var actual = results
            .Select(result => (
                result.RecordNumber,
                result.IsMalformed,
                result.ParsedJson.HasValue))
            .ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAsync_FinalRecordIsValidWithoutTrailingNewline_YieldsParsedRecord()
    {
        // Arrange
        var input = CreateRecordJson(
            type: "payment.completed",
            source: "billing-service",
            occurredAt: "2026-08-17T10:00:00Z",
            payload: new { paymentId = "pay-42" });
        var expected = (
            RecordNumber: 1L,
            Type: "payment.completed",
            IsMalformed: false);

        // Act
        var results = await ReadAllAsync(input);

        // Assert
        var actual = results
            .Select(result =>
            {
                var parsedJson = Assert.IsType<JsonElement>(result.ParsedJson);
                var type = Assert.IsType<string>(
                    parsedJson.GetProperty("type").GetString());

                return (result.RecordNumber, type, result.IsMalformed);
            })
            .ToArray();

        Assert.Equal(new[] { expected }, actual);
    }

    [Fact]
    public async Task ReadAsync_FinalRecordIsIncomplete_YieldsMalformedRecord()
    {
        // Arrange
        var input = CreateRecordJson(
            type: "payment.completed",
            source: "billing-service",
            occurredAt: "2026-08-17T10:00:00Z",
            payload: new { paymentId = "pay-42" })[..^1];
        var expected = (RecordNumber: 1L, IsMalformed: true, HasParsedJson: false);

        // Act
        var results = await ReadAllAsync(input);
        var actual = results
            .Select(result => (
                result.RecordNumber,
                result.IsMalformed,
                result.ParsedJson.HasValue))
            .ToArray();

        // Assert
        Assert.Equal(new[] { expected }, actual);
    }

    [Fact]
    public async Task ReadAsync_ParserDocumentIsDisposed_ParsedJsonRemainsUsable()
    {
        // Arrange
        var record = CreateRecordJson(
            type: "payment.completed",
            source: "billing-service",
            occurredAt: "2026-08-17T10:00:00Z",
            payload: new
            {
                nested = new
                {
                    values = new[] { 1, 2, 3 }
                }
            });
        var input = string.Join('\n', record, string.Empty);

        // Act
        var results = await ReadAllAsync(input);

        // Assert
        var result = Assert.Single(results);
        var parsedJson = Assert.IsType<JsonElement>(result.ParsedJson);
        var actual = parsedJson
            .GetProperty("payload")
            .GetProperty("nested")
            .GetProperty("values")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();

        Assert.Equal(new[] { 1, 2, 3 }, actual);
    }

    [Fact]
    public async Task ReadAsync_CancellationIsRequested_PropagatesOperationCanceledException()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in _reader.ReadAsync(
                Stream.Null,
                cancellationTokenSource.Token))
            {
            }
        });

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    #region Test helpers

    private async Task<NdjsonRecordResult[]> ReadAllAsync(string input)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var results = new List<NdjsonRecordResult>();

        await foreach (var result in _reader.ReadAsync(stream))
        {
            results.Add(result);
        }

        return results.ToArray();
    }

    private static string CreateRecordJson(
        string type,
        string source,
        string occurredAt,
        object payload)
    {
        return JsonSerializer.Serialize(new
        {
            type,
            source,
            occurredAt,
            payload
        });
    }

    #endregion
}
