using System.Text.Json;
using System.Text.Json.Nodes;
using PulseFlow.Api.Ingestion.Validation;

namespace PulseFlow.UnitTests.Ingestion.Validation;

// JSON syntax errors belong to the future NDJSON reader tests.
public sealed class EventEnvelopeValidatorTests
{
    private readonly EventEnvelopeValidator _validator = new();

    [Theory]
    [InlineData("[]")]
    [InlineData("\"event\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void Validate_RootIsNotObject_ReturnsRootNotObjectError(string json)
    {
        // Arrange
        using var document = JsonDocument.Parse(json);
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.RootNotObject, "$")
        };

        // Act
        var result = _validator.Validate(document.RootElement);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_TypeIsMissing_ReturnsTypeMissingError()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new
        {
            source = "billing-service",
            occurredAt = "2026-08-15T17:20:00Z",
            payload = new { }
        });
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.TypeMissing, "$.type")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("null", EventEnvelopeValidationErrorCode.TypeNull)]
    [InlineData("42", EventEnvelopeValidationErrorCode.TypeNotString)]
    [InlineData("{}", EventEnvelopeValidationErrorCode.TypeNotString)]
    [InlineData("[]", EventEnvelopeValidationErrorCode.TypeNotString)]
    [InlineData("true", EventEnvelopeValidationErrorCode.TypeNotString)]
    [InlineData("\"\"", EventEnvelopeValidationErrorCode.TypeEmpty)]
    public void Validate_TypeIsInvalid_ReturnsExpectedTypeError(
        string typeJson,
        EventEnvelopeValidationErrorCode expectedCode)
    {
        // Arrange
        var record = CreateRecord(type: ParseJsonValue(typeJson));
        var expectedErrors = new[] { (expectedCode, "$.type") };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_SourceIsMissing_ReturnsSourceMissingError()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new
        {
            type = "payment.completed",
            occurredAt = "2026-08-15T17:20:00Z",
            payload = new { }
        });
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.SourceMissing, "$.source")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("null", EventEnvelopeValidationErrorCode.SourceNull)]
    [InlineData("42", EventEnvelopeValidationErrorCode.SourceNotString)]
    [InlineData("{}", EventEnvelopeValidationErrorCode.SourceNotString)]
    [InlineData("[]", EventEnvelopeValidationErrorCode.SourceNotString)]
    [InlineData("false", EventEnvelopeValidationErrorCode.SourceNotString)]
    [InlineData("\"\"", EventEnvelopeValidationErrorCode.SourceEmpty)]
    public void Validate_SourceIsInvalid_ReturnsExpectedSourceError(
        string sourceJson,
        EventEnvelopeValidationErrorCode expectedCode)
    {
        // Arrange
        var record = CreateRecord(source: ParseJsonValue(sourceJson));
        var expectedErrors = new[] { (expectedCode, "$.source") };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_OccurredAtIsMissing_ReturnsOccurredAtMissingError()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new
        {
            type = "payment.completed",
            source = "billing-service",
            payload = new { }
        });
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.OccurredAtMissing, "$.occurredAt")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    public void Validate_OccurredAtIsNotString_ReturnsOccurredAtNotStringError(
        string occurredAtJson)
    {
        // Arrange
        var record = CreateRecord(occurredAt: ParseJsonValue(occurredAtJson));
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.OccurredAtNotString, "$.occurredAt")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("2026-08-15")]
    [InlineData("2026-02-30T17:20:00Z")]
    [InlineData("2026-08-15T17:20Z")]
    [InlineData("not-a-timestamp")]
    public void Validate_OccurredAtIsNotRfc3339_ReturnsOccurredAtInvalidRfc3339Error(
        string occurredAt)
    {
        // Arrange
        var record = CreateRecord(
            occurredAt: JsonSerializer.SerializeToElement(occurredAt));
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.OccurredAtInvalidRfc3339, "$.occurredAt")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("2026-08-15T17:20:00+00:00")]
    [InlineData("2026-08-15T20:20:00+03:00")]
    [InlineData("2026-08-15T17:20:00z")]
    public void Validate_OccurredAtDoesNotUseUppercaseZ_ReturnsOccurredAtNotUtcZError(
        string occurredAt)
    {
        // Arrange
        var record = CreateRecord(
            occurredAt: JsonSerializer.SerializeToElement(occurredAt));
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.OccurredAtNotUtcZ, "$.occurredAt")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_PayloadIsMissing_ReturnsPayloadMissingError()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new
        {
            type = "payment.completed",
            source = "billing-service",
            occurredAt = "2026-08-15T17:20:00Z"
        });
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.PayloadMissing, "$.payload")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_PayloadIsNull_ReturnsPayloadNullError()
    {
        // Arrange
        var record = CreateRecord(payload: ParseJsonValue("null"));
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.PayloadNull, "$.payload")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"payload\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void Validate_PayloadIsNotObject_ReturnsPayloadNotObjectError(
        string payloadJson)
    {
        // Arrange
        var record = CreateRecord(payload: ParseJsonValue(payloadJson));
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.PayloadNotObject, "$.payload")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    [Fact]
    public void Validate_RecordIsInvalid_SetsIsValidToFalse()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new { });

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RecordIsInvalid_ReturnsNoEnvelope()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new { });

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Null(result.Envelope);
    }

    [Fact]
    public void Validate_RecordIsValid_SetsIsValidToTrue()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsNoErrors()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsEnvelope()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.NotNull(result.Envelope);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsExpectedType()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal("payment.completed", result.Envelope?.Type);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsExpectedSource()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal("billing-service", result.Envelope?.Source);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsExpectedOccurredAt()
    {
        // Arrange
        var record = CreateValidRecord();

        // Act
        var result = _validator.Validate(record);
        var actualOccurredAt = result.Envelope?.OccurredAt.ToString("O");

        // Assert
        Assert.Equal("2026-08-15T17:20:00.1234567Z", actualOccurredAt);
    }

    [Fact]
    public void Validate_RecordIsValid_ReturnsExpectedPayload()
    {
        // Arrange
        var record = CreateValidRecord();
        var expectedPayload = JsonNode.Parse(CreateValidPayload().GetRawText());

        // Act
        var result = _validator.Validate(record);
        var actualPayload = JsonNode.Parse(result.Envelope?.Payload.GetRawText() ?? "null");

        // Assert
        Assert.True(JsonNode.DeepEquals(expectedPayload, actualPayload));
    }

    [Fact]
    public void Validate_InputDocumentIsDisposedAfterValidation_ReturnsValidPayload()
    {
        // Arrange
        var payload = new
        {
            nested = new
            {
                values = new[] { 1, 2, 3 }
            }
        };
        var expectedPayload = JsonSerializer.SerializeToNode(payload);
        var json = JsonSerializer.Serialize(new
        {
            type = "payment.completed",
            source = "billing-service",
            occurredAt = "2026-08-15T17:20:00Z",
            payload
        });
        EventEnvelopeValidationResult result;

        // Act
        using (var document = JsonDocument.Parse(json))
        {
            result = _validator.Validate(document.RootElement);
        }

        var actualPayload = JsonNode.Parse(result.Envelope?.Payload.GetRawText() ?? "null");

        // Assert
        Assert.True(JsonNode.DeepEquals(expectedPayload, actualPayload));
    }

    [Fact]
    public void Validate_RecordHasMultipleErrors_ReturnsAllContractErrors()
    {
        // Arrange
        var record = JsonSerializer.SerializeToElement(new { });
        var expectedErrors = new[]
        {
            (EventEnvelopeValidationErrorCode.TypeMissing, "$.type"),
            (EventEnvelopeValidationErrorCode.SourceMissing, "$.source"),
            (EventEnvelopeValidationErrorCode.OccurredAtMissing, "$.occurredAt"),
            (EventEnvelopeValidationErrorCode.PayloadMissing, "$.payload")
        };

        // Act
        var result = _validator.Validate(record);

        // Assert
        Assert.Equal(expectedErrors, GetErrors(result));
    }

    #region Test helpers

    private static JsonElement CreateRecord(
        JsonElement? type = null,
        JsonElement? source = null,
        JsonElement? occurredAt = null,
        JsonElement? payload = null)
    {
        var record = new Dictionary<string, JsonElement>
        {
            ["type"] = type ?? JsonSerializer.SerializeToElement("payment.completed"),
            ["source"] = source ?? JsonSerializer.SerializeToElement("billing-service"),
            ["occurredAt"] = occurredAt ??
                JsonSerializer.SerializeToElement("2026-08-15T17:20:00Z"),
            ["payload"] = payload ?? JsonSerializer.SerializeToElement(new { })
        };

        return JsonSerializer.SerializeToElement(record);
    }

    private static JsonElement CreateValidRecord()
    {
        return CreateRecord(
            occurredAt: JsonSerializer.SerializeToElement("2026-08-15T17:20:00.1234567Z"),
            payload: CreateValidPayload());
    }

    private static JsonElement CreateValidPayload()
    {
        return JsonSerializer.SerializeToElement(new
        {
            paymentId = "pay-123",
            amount = new
            {
                value = 42.50m,
                currency = "USD"
            },
            tags = new[] { "portfolio", "demo" },
            optional = (string?)null,
            active = true
        });
    }

    private static JsonElement ParseJsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static (EventEnvelopeValidationErrorCode Code, string JsonPath)[] GetErrors(
        EventEnvelopeValidationResult result)
    {
        return result.Errors
            .Select(error => (error.Code, error.JsonPath))
            .ToArray();
    }

    #endregion
}
