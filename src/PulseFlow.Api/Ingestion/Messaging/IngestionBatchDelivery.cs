namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class IngestionBatchDelivery
{
    // The delivery keeps broker details away from the parser worker.
    private readonly Func<CancellationToken, Task> _acknowledge;

    public IngestionBatchDelivery(
        ReadOnlyMemory<byte> body,
        Func<CancellationToken, Task> acknowledge)
    {
        ArgumentNullException.ThrowIfNull(acknowledge);

        Body = body;
        _acknowledge = acknowledge;
    }

    public ReadOnlyMemory<byte> Body { get; }

    public Task AcknowledgeAsync(CancellationToken cancellationToken)
    {
        return _acknowledge(cancellationToken);
    }
}
