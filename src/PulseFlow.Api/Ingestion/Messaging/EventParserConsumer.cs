using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Ndjson;
using System.Runtime.ExceptionServices;

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
        using var workerLifetimeSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var workers = Enumerable
            .Range(0, ConsumerCount)
            .Select(_ => RunConsumerAsync(workerLifetimeSource.Token))
            .ToArray();

        var completedWorker = await Task.WhenAny(workers);

        if (stoppingToken.IsCancellationRequested)
        {
            await Task.WhenAll(workers);
            return;
        }

        Exception initiatingException;

        try
        {
            await completedWorker;
            initiatingException = new InvalidOperationException(
                "An event parser consumer worker stopped unexpectedly.");
        }
        catch (Exception exception)
        {
            initiatingException = exception;
        }

        workerLifetimeSource.Cancel();

        try
        {
            await Task.WhenAll(workers);
        }
        catch
        {
            // Keep the worker failure that started shutdown.
        }

        ExceptionDispatchInfo.Capture(initiatingException).Throw();
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        IIngestionBatchConsumer? consumer = null;
        ExceptionDispatchInfo? workerException = null;

        try
        {
            consumer = await _consumerFactory.CreateAsync(stoppingToken);

            await foreach (var delivery in consumer.ReadAllAsync(stoppingToken))
            {
                await ProcessDeliveryAsync(delivery, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            workerException = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            if (consumer is not null)
            {
                await consumer.DisposeAsync();
            }
        }
        catch when (workerException is not null)
        {
            // Keep the first worker error.
        }

        workerException?.Throw();
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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Event parser consumer processing failed; terminal rejection is being attempted.");
            await delivery.RejectAsync(cancellationToken);
            return;
        }

        // RabbitMQ can forget this batch only after processing and scoped services finish.
        await delivery.AcknowledgeAsync(cancellationToken);
    }
}
