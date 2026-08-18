using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Ndjson;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class EventParserConsumer : BackgroundService
{
    private readonly IRabbitMqConsumerChannel _consumerChannel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NdjsonRecordReader _recordReader;
    private readonly ILogger<EventParserConsumer> _logger;

    public EventParserConsumer(
        IRabbitMqConsumerChannel consumerChannel,
        IServiceScopeFactory scopeFactory,
        NdjsonRecordReader recordReader,
        ILogger<EventParserConsumer> logger)
    {
        _consumerChannel = consumerChannel;
        _scopeFactory = scopeFactory;
        _recordReader = recordReader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _consumerChannel.StartConsumingAsync(ProcessDeliveryAsync, stoppingToken);

            // Keep the channel open until the host starts shutdown.
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _consumerChannel.StopConsumingAsync(CancellationToken.None);
            await _consumerChannel.DisposeAsync();
        }
    }

    private async Task ProcessDeliveryAsync(
        RabbitMqDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            // A background service must create a fresh scope for database work.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IngestEventsHandler>();
            await using var stream = new MemoryStream(delivery.Body.ToArray(), writable: false);
            var records = _recordReader.ReadAsync(stream, cancellationToken);

            // The existing handler keeps the known validation and chunk rules.
            await handler.HandleAsync(records, cancellationToken);

            // Mark the delivery done only after its valid events are stored.
            await _consumerChannel.AcknowledgeAsync(delivery.DeliveryTag, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Leave the message unacknowledged until failure policy is decided.
            _logger.LogError(
                exception,
                "Event parser consumer did not acknowledge RabbitMQ delivery {DeliveryTag}.",
                delivery.DeliveryTag);
        }
    }
}
