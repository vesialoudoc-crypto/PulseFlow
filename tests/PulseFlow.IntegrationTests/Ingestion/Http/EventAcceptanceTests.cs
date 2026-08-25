using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion.Http;

public sealed class EventAcceptanceTests
{
    [Fact]
    public async Task PostEvents_RawNdjsonBody_PassesUnchangedBytesToPublisher()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher);
        var expectedBatch = Encoding.UTF8.GetBytes(
            "{\"type\":\"first\"}\r\n{ not-json }\n");
        using var content = CreateNdjsonContent(expectedBatch);

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(expectedBatch, publisher.PublishedBatch);
    }

    [Fact]
    public async Task PostEvents_PayloadSmallerThanLimit_ReturnsAccepted()
    {
        // Arrange
        var expectedBatch = CreatePayload(7);
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher, maxBatchBytes: 8);
        using var content = CreateNdjsonContent(expectedBatch);

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(expectedBatch, publisher.PublishedBatch);
    }

    [Fact]
    public async Task PostEvents_PayloadAtLimit_ReturnsAccepted()
    {
        // Arrange
        var expectedBatch = CreatePayload(8);
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher, maxBatchBytes: expectedBatch.Length);
        using var content = CreateNdjsonContent(expectedBatch);

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(expectedBatch, publisher.PublishedBatch);
    }

    [Fact]
    public async Task PostEvents_ContentLengthExceedsLimit_ReturnsPayloadTooLargeProblemDetails()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher, maxBatchBytes: 8);
        using var content = CreateNdjsonContent(CreatePayload(9));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        AssertPayloadTooLargeProblem(response, problemDetails);
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task PostEvents_UnknownLengthPayloadExceedsLimit_ReturnsPayloadTooLargeProblemDetails()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher, maxBatchBytes: 8);
        using var request = CreateChunkedNdjsonRequest(CreatePayload(9));

        // Act
        using var response = await host.Client.SendAsync(request);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        AssertPayloadTooLargeProblem(response, problemDetails);
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task PostEvents_PublisherSucceeds_ReturnsAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher);
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(responseBody);
    }

    [Fact]
    public async Task PostEvents_RateLimitExceeded_ReturnsTooManyRequestsAndDoesNotPublish()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        var retryAfter = TimeSpan.FromMilliseconds(1500);
        await using var host = await CreateHostAsync(
            publisher,
            new FixedIngestionRateLimiter(
                new IngestionRateLimitResult(IngestionRateLimitStatus.Exceeded, retryAfter)));
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("2", response.Headers.RetryAfter?.ToString());
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task PostEvents_RateLimitUnavailable_ReturnsServiceUnavailableAndDoesNotPublish()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(
            publisher,
            new FixedIngestionRateLimiter(new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable)));
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task PostEvents_RateLimitAllowed_PublishesBatchAndReturnsAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(
            publisher,
            new FixedIngestionRateLimiter(new IngestionRateLimitResult(IngestionRateLimitStatus.Allowed)));
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, publisher.PublishCallCount);
    }

    [Fact]
    public async Task PostEvents_MalformedNdjsonAndPublisherSucceeds_ReturnsAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        await using var host = await CreateHostAsync(publisher);
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{ this-is-not-valid-json\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostEvents_PublisherFails_ReturnsSafeInternalServerError()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher(
            new InvalidOperationException("Publisher test failure."));
        await using var host = await CreateHostAsync(publisher);
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await host.Client.PostAsync("/api/events", content);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(problemDetails);
        Assert.Equal("An unexpected error occurred while processing the request.", problemDetails.Detail);
        Assert.DoesNotContain("Publisher test failure.", problemDetails.Detail);
    }

    #region Test helpers

    private static Task<PulseFlowComponentTestHost> CreateHostAsync(
        IIngestionBatchPublisher publisher,
        IIngestionRateLimiter? rateLimiter = null,
        long maxBatchBytes = IngestionOptions.DefaultMaxBatchBytes)
    {
        return PulseFlowComponentTestHost.StartAsync(services =>
        {
            services.AddSingleton(publisher);
            services.AddSingleton(
                rateLimiter
                ?? new FixedIngestionRateLimiter(
                    new IngestionRateLimitResult(IngestionRateLimitStatus.Allowed)));
        }, maxBatchBytes);
    }

    private static ByteArrayContent CreateNdjsonContent(byte[] batch)
    {
        var content = new ByteArrayContent(batch);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-ndjson");

        return content;
    }

    private static HttpRequestMessage CreateChunkedNdjsonRequest(byte[] batch)
    {
        var content = new UnknownLengthNdjsonContent(batch);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/events") { Content = content };
        request.Headers.TransferEncodingChunked = true;

        return request;
    }

    private static void AssertPayloadTooLargeProblem(HttpResponseMessage response, ProblemDetails? problemDetails)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problemDetails.Status);
        Assert.Equal("Payload Too Large", problemDetails.Title);
        Assert.Equal("The request body exceeds the maximum allowed batch size.", problemDetails.Detail);
        Assert.True(problemDetails.Extensions.ContainsKey("traceId"));
    }

    private static byte[] CreatePayload(int length)
    {
        return Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
    }

    private sealed class RecordingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        private readonly Exception? _exception;

        public RecordingIngestionBatchPublisher(Exception? exception = null)
        {
            _exception = exception;
        }

        public byte[]? PublishedBatch { get; private set; }

        public int PublishCallCount { get; private set; }

        public Task PublishAsync(
            ReadOnlyMemory<byte> rawBatch,
            CancellationToken cancellationToken)
        {
            PublishCallCount++;
            PublishedBatch = rawBatch.ToArray();

            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }

    private sealed class FixedIngestionRateLimiter : IIngestionRateLimiter
    {
        private readonly IngestionRateLimitResult _result;

        public FixedIngestionRateLimiter(IngestionRateLimitResult result)
        {
            _result = result;
        }

        public Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class UnknownLengthNdjsonContent : HttpContent
    {
        private readonly byte[] _batch;

        public UnknownLengthNdjsonContent(byte[] batch)
        {
            _batch = batch;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-ndjson");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(_batch, CancellationToken.None).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }
    }

    #endregion
}
