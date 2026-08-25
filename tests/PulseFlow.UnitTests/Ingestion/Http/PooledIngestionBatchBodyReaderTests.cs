using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Http;
using System.Buffers;

namespace PulseFlow.UnitTests.Ingestion.Http;

public sealed class PooledIngestionBatchBodyReaderTests
{
    [Fact]
    public async Task ReadAsync_ContentLengthExceedsLimit_ReturnsTooLargeWithoutReadingOrRentingBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var reader = CreateReader(maxBatchBytes: 4, pool);
        var body = new ThrowOnReadStream();

        // Act
        using var result = await reader.ReadAsync(body, contentLength: 5, CancellationToken.None);

        // Assert
        Assert.True(result.IsTooLarge);
        Assert.Equal(0, body.ReadAttemptCount);
        Assert.Empty(pool.RentedBuffers);
    }

    [Fact]
    public async Task ReadAsync_PayloadSmallerThanLimit_ReturnsUnchangedPayload()
    {
        // Arrange
        byte[] expectedPayload = CreatePayload(3);
        var reader = CreateReader(maxBatchBytes: 4, new TrackingArrayPool());

        // Act
        using var result = await reader.ReadAsync(
            new ByteSequenceStream(expectedPayload, maximumBytesPerRead: 1),
            contentLength: null,
            CancellationToken.None);

        // Assert
        Assert.False(result.IsTooLarge);
        Assert.Equal(expectedPayload, result.Payload.ToArray());
    }

    [Fact]
    public async Task ReadAsync_PayloadAtLimit_ReturnsUnchangedPayload()
    {
        // Arrange
        byte[] expectedPayload = CreatePayload(4);
        var reader = CreateReader(maxBatchBytes: expectedPayload.Length, new TrackingArrayPool());

        // Act
        using var result = await reader.ReadAsync(
            new ByteSequenceStream(expectedPayload, maximumBytesPerRead: 1),
            contentLength: expectedPayload.Length,
            CancellationToken.None);

        // Assert
        Assert.False(result.IsTooLarge);
        Assert.Equal(expectedPayload, result.Payload.ToArray());
    }

    [Fact]
    public async Task ReadAsync_PayloadExceedsLimitByOneByte_ReturnsTooLarge()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var body = new ByteSequenceStream(CreatePayload(5), maximumBytesPerRead: 1);
        var reader = CreateReader(maxBatchBytes: 4, pool);

        // Act
        using var result = await reader.ReadAsync(body, contentLength: null, CancellationToken.None);

        // Assert
        Assert.True(result.IsTooLarge);
        Assert.Equal(5, body.BytesRead);
        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task ReadAsync_UnknownLengthPayloadExceedsLimit_StopsAfterFirstExcessByte()
    {
        // Arrange
        var body = new ByteSequenceStream(CreatePayload(8), maximumBytesPerRead: 1);
        var reader = CreateReader(maxBatchBytes: 4, new TrackingArrayPool());

        // Act
        using var result = await reader.ReadAsync(body, contentLength: null, CancellationToken.None);

        // Assert
        Assert.True(result.IsTooLarge);
        Assert.Equal(5, body.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_ContentLengthAtLimitButPayloadExceedsLimit_ReturnsTooLarge()
    {
        // Arrange
        var body = new ByteSequenceStream(CreatePayload(5), maximumBytesPerRead: 1);
        var reader = CreateReader(maxBatchBytes: 4, new TrackingArrayPool());

        // Act
        using var result = await reader.ReadAsync(body, contentLength: 4, CancellationToken.None);

        // Assert
        Assert.True(result.IsTooLarge);
        Assert.Equal(5, body.BytesRead);
    }

    [Fact]
    public async Task ReadAsync_RequestIsCanceled_PropagatesCancellationAndReturnsBuffer()
    {
        // Arrange
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var pool = new TrackingArrayPool();
        var reader = CreateReader(maxBatchBytes: 4, pool);

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(new CancellationStream(), contentLength: null, cancellationTokenSource.Token));

        // Assert
        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task ReadAsync_PayloadExceedsInitialCapacity_ExpandsAndReturnsUnchangedPayload()
    {
        // Arrange
        byte[] expectedPayload = CreatePayload((16 * 1024) + 1);
        var reader = CreateReader(maxBatchBytes: expectedPayload.Length, new TrackingArrayPool());

        // Act
        using var result = await reader.ReadAsync(
            new ByteSequenceStream(expectedPayload, maximumBytesPerRead: expectedPayload.Length),
            contentLength: null,
            CancellationToken.None);

        // Assert
        Assert.False(result.IsTooLarge);
        Assert.Equal(expectedPayload, result.Payload.ToArray());
    }

    [Fact]
    public async Task ReadAsync_ExpandsBuffer_ReturnsPreviousRentedBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        byte[] payload = CreatePayload((16 * 1024) + 1);
        var reader = CreateReader(maxBatchBytes: payload.Length, pool);

        // Act
        using var result = await reader.ReadAsync(
            new ByteSequenceStream(payload, maximumBytesPerRead: payload.Length),
            contentLength: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, pool.RentedBuffers.Count);
        Assert.Single(pool.ReturnedBuffers);
        Assert.Same(pool.RentedBuffers[0], pool.ReturnedBuffers[0]);
    }

    [Fact]
    public async Task Dispose_SuccessfulResult_ReturnsFinalRentedBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var reader = CreateReader(maxBatchBytes: 4, pool);
        var result = await reader.ReadAsync(
            new ByteSequenceStream(CreatePayload(3), maximumBytesPerRead: 3),
            contentLength: null,
            CancellationToken.None);

        // Act
        result.Dispose();

        // Assert
        Assert.Single(pool.ReturnedBuffers);
        Assert.Same(pool.RentedBuffers[0], pool.ReturnedBuffers[0]);
    }

    [Fact]
    public async Task Dispose_CalledTwice_ReturnsFinalRentedBufferOnce()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var reader = CreateReader(maxBatchBytes: 4, pool);
        var result = await reader.ReadAsync(
            new ByteSequenceStream(CreatePayload(3), maximumBytesPerRead: 3),
            contentLength: null,
            CancellationToken.None);

        // Act
        result.Dispose();
        result.Dispose();

        // Assert
        Assert.Single(pool.ReturnedBuffers);
    }

    [Fact]
    public async Task ReadAsync_StreamThrows_ReturnsRentedBuffer()
    {
        // Arrange
        var pool = new TrackingArrayPool();
        var reader = CreateReader(maxBatchBytes: 4, pool);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.ReadAsync(new ThrowOnReadStream(), contentLength: null, CancellationToken.None));

        // Assert
        Assert.Single(pool.ReturnedBuffers);
    }

    #region Test helpers

    private static PooledIngestionBatchBodyReader CreateReader(long maxBatchBytes, ArrayPool<byte> pool)
    {
        return new PooledIngestionBatchBodyReader(
            Options.Create(new IngestionOptions { MaxBatchBytes = maxBatchBytes }),
            pool);
    }

    private static byte[] CreatePayload(int length)
    {
        return Enumerable.Range(0, length).Select(index => (byte) index).ToArray();
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        public List<byte[]> RentedBuffers { get; } = [];

        public List<byte[]> ReturnedBuffers { get; } = [];

        public override byte[] Rent(int minimumLength)
        {
            byte[] buffer = new byte[minimumLength];
            RentedBuffers.Add(buffer);

            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnedBuffers.Add(array);
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

    private sealed class CancellationStream : Stream
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
        private readonly int _maximumBytesPerRead;
        private readonly byte[] _payload;
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int remainingBytes = _payload.Length - _position;
            int bytesToRead = Math.Min(Math.Min(buffer.Length, _maximumBytesPerRead), remainingBytes);
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
