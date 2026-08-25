using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;
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
    public async Task IngestAsync_ContentLengthExceedsLimit_DoesNotReadRequestBodyOrPublish()
    {
        // Arrange
        const long maxBatchBytes = 4;
        var requestBody = new ThrowOnReadStream();
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(publisher, requestBody, maxBatchBytes, maxBatchBytes + 1);

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        var problemDetails = AssertPayloadTooLargeProblem(result, controller.HttpContext.TraceIdentifier);
        Assert.Equal(0, requestBody.ReadAttemptCount);
        Assert.Equal(0, publisher.PublishCallCount);
        Assert.Equal("The request body exceeds the maximum allowed batch size.", problemDetails.Detail);
    }

    [Fact]
    public async Task IngestAsync_UnknownLengthBodyExceedsLimit_StopsAfterFirstExcessByteAndDoesNotPublish()
    {
        // Arrange
        const long maxBatchBytes = 4;
        var requestBody = new ByteSequenceStream(CreatePayload((int)maxBatchBytes + 1), maximumBytesPerRead: 1);
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(publisher, requestBody, maxBatchBytes, contentLength: null);

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        var problemDetails = AssertPayloadTooLargeProblem(result, controller.HttpContext.TraceIdentifier);
        Assert.Equal(maxBatchBytes + 1, requestBody.BytesRead);
        Assert.Equal(0, publisher.PublishCallCount);
        Assert.Equal("Payload Too Large", problemDetails.Title);
    }

    [Fact]
    public async Task IngestAsync_ContentLengthAtLimitButBodyExceedsLimit_ReturnsPayloadTooLarge()
    {
        // Arrange
        const long maxBatchBytes = 4;
        var requestBody = new ByteSequenceStream(CreatePayload((int)maxBatchBytes + 1), maximumBytesPerRead: 1);
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(publisher, requestBody, maxBatchBytes, contentLength: maxBatchBytes);

        // Act
        var result = await controller.IngestAsync(CancellationToken.None);

        // Assert
        AssertPayloadTooLargeProblem(result, controller.HttpContext.TraceIdentifier);
        Assert.Equal(maxBatchBytes + 1, requestBody.BytesRead);
        Assert.Equal(0, publisher.PublishCallCount);
    }

    [Fact]
    public async Task IngestAsync_RequestAbortedDuringRead_PropagatesCancellation()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        var requestBody = new ThrowOnCancellationStream();
        var publisher = new RecordingIngestionBatchPublisher();
        var controller = CreateController(publisher, requestBody, maxBatchBytes: 4, contentLength: null);
        controller.HttpContext.RequestAborted = cancellationTokenSource.Token;
        cancellationTokenSource.Cancel();

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.IngestAsync(controller.HttpContext.RequestAborted));

        // Assert
        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
    }

    #region Test helpers

    private static EventsController CreateController(
        IIngestionBatchPublisher publisher,
        Stream requestBody,
        long maxBatchBytes,
        long? contentLength
    )
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "test-trace-id";
        httpContext.Request.Body = requestBody;
        httpContext.Request.ContentLength = contentLength;

        return new EventsController(
            publisher,
            new AllowedIngestionRateLimiter(),
            Options.Create(new IngestionOptions { ChunkCapacity = 1, MaxBatchBytes = maxBatchBytes })
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
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

    private sealed class RecordingIngestionBatchPublisher : IIngestionBatchPublisher
    {
        public int PublishCallCount { get; private set; }

        public Task PublishAsync(ReadOnlyMemory<byte> rawBatch, CancellationToken ct)
        {
            PublishCallCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class AllowedIngestionRateLimiter : IIngestionRateLimiter
    {
        public Task<IngestionRateLimitResult> TryAllowAsync(CancellationToken ct)
        {
            return Task.FromResult(new IngestionRateLimitResult(IngestionRateLimitStatus.Allowed));
        }
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public int ReadAttemptCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttemptCount++;
            throw new InvalidOperationException("Request body must not be read.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadAttemptCount++;
            throw new InvalidOperationException("Request body must not be read.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowOnCancellationStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ByteSequenceStream : Stream
    {
        private readonly byte[] _payload;
        private readonly int _maximumBytesPerRead;
        private int _position;

        public ByteSequenceStream(byte[] payload, int maximumBytesPerRead)
        {
            _payload = payload;
            _maximumBytesPerRead = maximumBytesPerRead;
        }

        public long BytesRead => _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingBytes = _payload.Length - _position;
            var bytesToRead = Math.Min(Math.Min(buffer.Length, _maximumBytesPerRead), remainingBytes);
            _payload.AsSpan(_position, bytesToRead).CopyTo(buffer.Span);
            _position += bytesToRead;

            return ValueTask.FromResult(bytesToRead);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    #endregion
}
