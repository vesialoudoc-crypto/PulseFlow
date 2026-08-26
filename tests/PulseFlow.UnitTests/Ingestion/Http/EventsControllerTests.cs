using System.Buffers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PulseFlow.Api.Ingestion.Http;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.RateLimiting;

namespace PulseFlow.UnitTests.Ingestion.Http;

public sealed class EventsControllerTests
{
    [Fact]
    public void EventsController_DisableRequestSizeLimitAttribute_DisablesServerRequestSizeLimit()
    {
        // Arrange
        var controllerType = typeof(EventsController);

        // Act
        var attribute = controllerType
            .GetCustomAttributes(typeof(DisableRequestSizeLimitAttribute), inherit: true)
            .SingleOrDefault();

        // Assert
        Assert.IsType<DisableRequestSizeLimitAttribute>(attribute);
    }

    [Fact]
    public async Task IngestAsync_BodyReaderReturnsTooLarge_ReturnsPayloadTooLargeAndDoesNotPublish()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(IngestionBatchBodyReadResult.TooLarge())));

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        var problemDetails = AssertPayloadTooLargeProblem(result, controller.HttpContext.TraceIdentifier);
        Assert.Equal(0, publisher.PublishCallCount);
        Assert.Equal("The request body exceeds the maximum allowed batch size.", problemDetails.Detail);
    }

    [Fact]
    public async Task IngestAsync_BodyReaderReturnsPayload_PublishesExactBytes()
    {
        // Arrange
        var expectedPayload = CreatePayload(4);
        var pool = new TrackingArrayPool();
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(pool, expectedPayload))));

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(expectedPayload, publisher.PublishedPayload);
    }

    [Fact]
    public async Task IngestAsync_PublisherCompletes_ReturnsReaderBufferAfterPublication()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var publisher = new BlockingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(pool, CreatePayload(4)))));

        // Act
        var ingestionTask = controller.IngestAsync(CancellationToken.None);
        await publisher.PublishStarted.Task;

        // Assert
        Assert.Empty(pool.ReturnedBuffers);

        publisher.CompletePublication();
        await ingestionTask;

        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task IngestAsync_PublisherSucceeds_ReturnsReaderBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(pool, CreatePayload(4)))));

        // Act
        await controller.IngestAsync(CancellationToken.None);

        // Assert
        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task IngestAsync_PublisherThrows_ReturnsReaderBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var publisher = new RecordingIngestionBatchPublisher(new InvalidOperationException("Publisher failure."));
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(pool, CreatePayload(4)))));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.IngestAsync(CancellationToken.None));

        // Assert
        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task IngestAsync_PublisherTimesOut_DoesNotReturnAccepted()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher(
            new TimeoutException("RabbitMQ publish/confirmation timed out."));
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(new TrackingArrayPool(), CreatePayload(4)))));

        // Act
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            controller.IngestAsync(CancellationToken.None));

        // Assert
        Assert.Equal("RabbitMQ publish/confirmation timed out.", exception.Message);
        Assert.Equal(1, publisher.PublishCallCount);
    }

    [Fact]
    public async Task IngestAsync_RedisTimeoutReturnsUnavailable_ReturnsServiceUnavailableAndDoesNotPublish()
    {
        // Arrange
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ => Task.FromResult(CreateSuccessfulResult(new TrackingArrayPool(), CreatePayload(4)))),
            new UnavailableIngestionRateLimiter());

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCodeResult.StatusCode);
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task IngestAsync_BodyReaderIsCanceled_PropagatesCancellation()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(
            publisher,
            new StubIngestionBatchBodyReader(_ =>
                Task.FromCanceled<IngestionBatchBodyReadResult>(cancellationTokenSource.Token)));

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.IngestAsync(cancellationTokenSource.Token));

        // Assert
        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
    }

    #region Test helpers

    private static EventsController CreateController(
        IIngestionBatchPublisher publisher,
        IIngestionBatchBodyReader bodyReader,
        IIngestionRateLimiter? rateLimiter = null
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "test-trace-id";

        return new EventsController(publisher, rateLimiter ?? new AllowedIngestionRateLimiter(), bodyReader)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static IngestionBatchBodyReadResult CreateSuccessfulResult(
        TrackingArrayPool pool,
        byte[] payload
    )
    {
        var buffer = pool.Rent(payload.Length);
        payload.CopyTo(buffer, 0);

        return new IngestionBatchBodyReadResult(buffer, payload.Length, pool);
    }

    private static ProblemDetails AssertPayloadTooLargeProblem(IActionResult result, string traceIdentifier)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, objectResult.StatusCode);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problemDetails.Status);
        Assert.Equal(traceIdentifier, problemDetails.Extensions["traceId"]);

        return problemDetails;
    }

    private static byte[] CreatePayload(int length)
    {
        return Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
    }

    private sealed class StubIngestionBatchBodyReader : IIngestionBatchBodyReader
    {
        private readonly Func<CancellationToken, Task<IngestionBatchBodyReadResult>> _readAsync;

        public StubIngestionBatchBodyReader(
            Func<CancellationToken, Task<IngestionBatchBodyReadResult>> readAsync
        )
        {
            _readAsync = readAsync;
        }

        public Task<IngestionBatchBodyReadResult> ReadAsync(
            Stream body,
            long? contentLength,
            CancellationToken cancellationToken
        )
        {
            return _readAsync(cancellationToken);
        }
    }

    private sealed class RecordingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        private readonly Exception? _exception;

        public RecordingIngestionBatchPublisher(Exception? exception = null)
        {
            _exception = exception;
        }

        public int PublishCallCount { get; private set; }

        public byte[]? PublishedPayload { get; private set; }

        public Task PublishAsync(ReadOnlyMemory<byte> rawBatch, CancellationToken ct)
        {
            PublishCallCount++;
            PublishedPayload = rawBatch.ToArray();

            return _exception is null ? Task.CompletedTask : Task.FromException(_exception);
        }
    }

    private sealed class BlockingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        private readonly TaskCompletionSource _publicationCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PublishStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync(ReadOnlyMemory<byte> rawBatch, CancellationToken ct)
        {
            PublishStarted.SetResult();
            await _publicationCompletion.Task.WaitAsync(ct);
        }

        public void CompletePublication()
        {
            _publicationCompletion.SetResult();
        }
    }

    private sealed class AllowedIngestionRateLimiter : IIngestionRateLimiter
    {
        public Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct)
        {
            return Task.FromResult(new IngestionRateLimitResult(IngestionRateLimitStatus.Allowed));
        }
    }

    private sealed class UnavailableIngestionRateLimiter : IIngestionRateLimiter
    {
        public Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct)
        {
            return Task.FromResult(new IngestionRateLimitResult(IngestionRateLimitStatus.Unavailable));
        }
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        public List<byte[]> ReturnedBuffers { get; } = [];

        public override byte[] Rent(int minimumLength)
        {
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnedBuffers.Add(array);
        }
    }

    #endregion
}
