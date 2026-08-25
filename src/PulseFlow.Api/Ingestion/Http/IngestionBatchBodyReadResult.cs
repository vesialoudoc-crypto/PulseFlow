using System.Buffers;

namespace PulseFlow.Api.Ingestion.Http;

public sealed class IngestionBatchBodyReadResult : IDisposable
{
    private readonly ArrayPool<byte>? _arrayPool;
    private readonly int _payloadLength;
    private byte[]? _buffer;

    internal IngestionBatchBodyReadResult(byte[] buffer, int payloadLength, ArrayPool<byte> arrayPool)
    {
        _buffer = buffer;
        _payloadLength = payloadLength;
        _arrayPool = arrayPool;
    }

    private IngestionBatchBodyReadResult()
    {
        IsTooLarge = true;
    }

    public bool IsTooLarge { get; }

    public ReadOnlyMemory<byte> Payload
    {
        get
        {
            if (IsTooLarge)
            {
                throw new InvalidOperationException("An oversized body does not have a payload.");
            }

            byte[] buffer = _buffer ?? throw new ObjectDisposedException(nameof(IngestionBatchBodyReadResult));

            return buffer.AsMemory(0, _payloadLength);
        }
    }

    internal static IngestionBatchBodyReadResult TooLarge()
    {
        return new IngestionBatchBodyReadResult();
    }

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            _arrayPool!.Return(buffer);
        }
    }
}
