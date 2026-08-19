namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class IngestionBatchDelivery
{
    private readonly Func<CancellationToken, Task> _acknowledge;
    private readonly Func<CancellationToken, Task> _reject;

    internal IngestionBatchDelivery(
        ReadOnlyMemory<byte> body,
        Func<CancellationToken, Task> acknowledge,
        Func<CancellationToken, Task> reject)
    {
        ArgumentNullException.ThrowIfNull(acknowledge);
        ArgumentNullException.ThrowIfNull(reject);

        Body = body;
        _acknowledge = acknowledge;
        _reject = reject;
    }

    public ReadOnlyMemory<byte> Body { get; }

    public Task AcknowledgeAsync(CancellationToken cancellationToken)
    {
        return _acknowledge(cancellationToken);
    }

    public Task RejectAsync(CancellationToken cancellationToken)
    {
        return _reject(cancellationToken);
    }
}
