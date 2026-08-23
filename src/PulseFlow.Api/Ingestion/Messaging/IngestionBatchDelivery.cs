namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class IngestionBatchDelivery
{
    private readonly Func<CancellationToken, Task> _acknowledge;
    private readonly Func<CancellationToken, Task> _reject;

    public ReadOnlyMemory<byte> Body { get; }

    internal IngestionBatchDelivery(
        ReadOnlyMemory<byte> body,
        Func<CancellationToken, Task> acknowledge,
        Func<CancellationToken, Task> reject
    )
    {
        ArgumentNullException.ThrowIfNull(acknowledge);
        ArgumentNullException.ThrowIfNull(reject);

        Body = body;
        _acknowledge = acknowledge;
        _reject = reject;
    }

    public Task AcknowledgeAsync(CancellationToken ct)
    {
        return _acknowledge(ct);
    }

    public Task RejectAsync(CancellationToken ct)
    {
        return _reject(ct);
    }
}
