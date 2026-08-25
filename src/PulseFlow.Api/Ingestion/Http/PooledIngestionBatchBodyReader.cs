using System.Buffers;
using Microsoft.Extensions.Options;

namespace PulseFlow.Api.Ingestion.Http;

internal sealed class PooledIngestionBatchBodyReader : IIngestionBatchBodyReader
{
    private const int InitialBatchBufferBytes = 16 * 1024;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly long _maxBatchBytes;

    public PooledIngestionBatchBodyReader(IOptions<IngestionOptions> options)
        : this(options, ArrayPool<byte>.Shared) { }

    internal PooledIngestionBatchBodyReader(IOptions<IngestionOptions> options, ArrayPool<byte> arrayPool)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arrayPool);

        _maxBatchBytes = options.Value.MaxBatchBytes;
        _arrayPool = arrayPool;
    }

    public async Task<IngestionBatchBodyReadResult> ReadAsync(Stream body, long? contentLength, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (contentLength is long length && length > _maxBatchBytes)
        {
            return IngestionBatchBodyReadResult.TooLarge();
        }

        int maximumBatchBytes = checked((int)_maxBatchBytes);
        int batchCapacity = Math.Min(InitialBatchBufferBytes, maximumBatchBytes);
        byte[]? batch = _arrayPool.Rent(batchCapacity);
        int batchLength = 0;

        try
        {
            while (batchLength < maximumBatchBytes)
            {
                if (batchLength == batchCapacity)
                {
                    int expandedBatchCapacity = Math.Min(checked(batchCapacity * 2), maximumBatchBytes);
                    byte[] expandedBatch = _arrayPool.Rent(expandedBatchCapacity);

                    try
                    {
                        batch.AsSpan(0, batchLength).CopyTo(expandedBatch);
                    }
                    catch
                    {
                        _arrayPool.Return(expandedBatch);
                        throw;
                    }

                    _arrayPool.Return(batch);
                    batch = expandedBatch;
                    batchCapacity = expandedBatchCapacity;
                }

                int bytesToRead = Math.Min(batchCapacity - batchLength, maximumBatchBytes - batchLength);
                int read = await body.ReadAsync(batch.AsMemory(batchLength, bytesToRead), ct);
                if (read == 0)
                {
                    break;
                }

                batchLength += read;
            }

            if (batchLength == maximumBatchBytes)
            {
                byte[] probe = new byte[1];
                int read = await body.ReadAsync(probe, ct);

                if (read != 0)
                {
                    return IngestionBatchBodyReadResult.TooLarge();
                }
            }

            var result = new IngestionBatchBodyReadResult(batch, batchLength, _arrayPool);
            batch = null;

            return result;
        }
        finally
        {
            if (batch is not null)
            {
                _arrayPool.Return(batch);
            }
        }
    }
}
