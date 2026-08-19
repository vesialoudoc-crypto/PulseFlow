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

    public EventParserConsumer(
        IIngestionBatchConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        NdjsonRecordReader recordReader,
        ILogger<EventParserConsumer> logger,
        int consumerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consumerCount, 1);

        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _recordReader = recordReader;
        _logger = logger;
        ConsumerCount = consumerCount;
    }

    internal IIngestionBatchConsumerFactory ConsumerFactory => _consumerFactory;

    internal int ConsumerCount { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One hosted service owns all workers in this application process.
        var workers = Enumerable
            .Range(0, ConsumerCount)
            .Select(_ => RunConsumerAsync(stoppingToken));

        await Task.WhenAll(workers);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var consumer = await _consumerFactory.CreateAsync(stoppingToken);

            await foreach (var delivery in consumer.ReadAllAsync(stoppingToken))
            {
                await ProcessDeliveryAsync(delivery, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessDeliveryAsync(
        IngestionBatchDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            // Each message needs its own scoped database services.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IngestEventsHandler>();
            await using var stream = new MemoryStream(delivery.Body.ToArray(), writable: false);
            var records = _recordReader.ReadAsync(stream, cancellationToken);

            // Keep the already tested parsing and storage rules in one place.
            await handler.HandleAsync(records, cancellationToken);

            // RabbitMQ can forget this batch only after storage succeeds.
            await delivery.AcknowledgeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not choose retry behavior here before that policy is defined.
            _logger.LogError(
                exception,
                "Event parser consumer did not acknowledge an ingestion batch delivery.");
        }
    }
}
