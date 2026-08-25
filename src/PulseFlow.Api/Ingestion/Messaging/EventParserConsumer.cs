using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class EventParserConsumer : BackgroundService, IStartupReadinessParticipant
{
    private readonly IIngestionBatchConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NdjsonRecordReader _recordReader;
    private readonly ILogger<EventParserConsumer> _logger;
    private readonly int _consumerCount;
    private readonly StartupReadinessState _readinessState;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EventParserConsumer(
        IIngestionBatchConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        NdjsonRecordReader recordReader,
        ILogger<EventParserConsumer> logger,
        int consumerCount,
        StartupReadinessState readinessState
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(consumerCount, 1);

        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _recordReader = recordReader;
        _logger = logger;
        _consumerCount = consumerCount;
        _readinessState = readinessState;
    }

    public Task WaitUntilStartedAsync(CancellationToken cancellationToken)
    {
        return _started.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _readinessState.WaitUntilInitializationCompletedAsync(stoppingToken);
        var workerStarted = Enumerable
            .Range(0, _consumerCount)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var workers = workerStarted.Select(started => RunConsumerAsync(stoppingToken, started)).ToArray();

        try
        {
            await Task.WhenAll(workerStarted.Select(started => started.Task));
            _started.TrySetResult();
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
        }

        await Task.WhenAll(workers);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken, TaskCompletionSource started)
    {
        try
        {
            await using var consumer = await _consumerFactory.CreateAsync(stoppingToken);
            var consumeTask = consumer.ConsumeAsync(ProcessDeliveryAsync, stoppingToken);

            await consumer.WaitUntilStartedAsync(stoppingToken);
            started.TrySetResult();
            await consumeTask;
        }
        catch (Exception exception)
        {
            started.TrySetException(exception);
            throw;
        }
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
