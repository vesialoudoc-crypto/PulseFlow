using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Ndjson;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class EventParserConsumer : BackgroundService
{
    private readonly IIngestionBatchConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NdjsonRecordReader _recordReader;
    private readonly ILogger<EventParserConsumer> _logger;
    private readonly int _consumerCount;

    public EventParserConsumer(
        IIngestionBatchConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        NdjsonRecordReader recordReader,
        ILogger<EventParserConsumer> logger,
        int consumerCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consumerCount, 1);

        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _recordReader = recordReader;
        _logger = logger;
        _consumerCount = consumerCount;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, _consumerCount).Select(_ => RunConsumerAsync(stoppingToken)).ToArray();

        await Task.WhenAll(workers);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        await using var consumer = await _consumerFactory.CreateAsync(stoppingToken);

        await consumer.ConsumeAsync(ProcessDeliveryAsync, stoppingToken);
    }

    private async Task ProcessDeliveryAsync(IngestionBatchDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessBatchAsync(delivery.Body, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RejectAfterProcessingFailureAsync(delivery, exception, cancellationToken);
            return;
        }

        // RabbitMQ can forget this batch only after processing and scoped services finish.
        await delivery.AcknowledgeAsync(cancellationToken);
    }

    private async Task ProcessBatchAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        // Each delivery needs its own scoped database services and unread raw batch stream.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IngestEventsHandler>();
        await using var stream = new MemoryStream(body.ToArray(), writable: false);
        var records = _recordReader.ReadAsync(stream, ct);

        // Keep the already tested parsing and storage rules in one place.
        await handler.HandleAsync(records, ct);
    }

    private async Task RejectAfterProcessingFailureAsync(
        IngestionBatchDelivery delivery,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        _logger.LogError(exception, "Event parser consumer processing failed; terminal rejection is being attempted.");
        await delivery.RejectAsync(cancellationToken);
    }
}
