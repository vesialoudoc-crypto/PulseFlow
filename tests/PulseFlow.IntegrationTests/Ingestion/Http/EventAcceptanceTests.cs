using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion.Http;

public sealed class EventAcceptanceTests
{
    [Fact]
    public async Task PostEvents_RawNdjsonBody_PassesUnchangedBytesToPublisher()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        using var factory = CreateFactory(publisher);
        using var client = factory.CreateClient();
        var expectedBatch = Encoding.UTF8.GetBytes(
            "{\"type\":\"first\"}\r\n{ not-json }\n");
        using var content = CreateNdjsonContent(expectedBatch);

        // Act
        using var response = await client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(expectedBatch, publisher.PublishedBatch);
    }

    [Fact]
    public async Task PostEvents_PublisherSucceeds_ReturnsAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        using var factory = CreateFactory(publisher);
        using var client = factory.CreateClient();
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await client.PostAsync("/api/events", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(responseBody);
    }

    [Fact]
    public async Task PostEvents_MalformedNdjsonAndPublisherSucceeds_ReturnsAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        using var factory = CreateFactory(publisher);
        using var client = factory.CreateClient();
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{ this-is-not-valid-json\n"));

        // Act
        using var response = await client.PostAsync("/api/events", content);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostEvents_PublisherFails_ReturnsSafeInternalServerError()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher(
            new InvalidOperationException("Publisher test failure."));
        using var factory = CreateFactory(publisher);
        using var client = factory.CreateClient();
        using var content = CreateNdjsonContent(
            Encoding.UTF8.GetBytes("{\"type\":\"event\"}\n"));

        // Act
        using var response = await client.PostAsync("/api/events", content);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(problemDetails);
        Assert.Equal("An unexpected error occurred while processing the request.", problemDetails.Detail);
        Assert.DoesNotContain("Publisher test failure.", problemDetails.Detail);
    }

    #region Test helpers

    private static WebApplicationFactory<Program> CreateFactory(
        IIngestionBatchPublisher publisher)
    {
        var factory = new PulseFlowWebApplicationFactory<Program>(
            "Host=localhost;Database=pulseflow_tests;Username=postgres;Password=postgres");

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIngestionBatchPublisher>();
                services.AddSingleton(publisher);
            });
        });
    }

    private static ByteArrayContent CreateNdjsonContent(byte[] batch)
    {
        var content = new ByteArrayContent(batch);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-ndjson");

        return content;
    }

    private sealed class RecordingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        private readonly Exception? _exception;

        public RecordingIngestionBatchPublisher(Exception? exception = null)
        {
            _exception = exception;
        }

        public byte[]? PublishedBatch { get; private set; }

        public Task PublishAsync(
            ReadOnlyMemory<byte> rawBatch,
            CancellationToken cancellationToken)
        {
            PublishedBatch = rawBatch.ToArray();

            return _exception is null
                ? Task.CompletedTask
                : Task.FromException(_exception);
        }
    }

    #endregion
}
