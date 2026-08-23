namespace PulseFlow.Api.Ingestion.Messaging;

public delegate Task IngestionBatchHandler(IngestionBatchDelivery delivery, CancellationToken ct);
