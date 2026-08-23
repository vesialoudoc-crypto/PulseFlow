namespace PulseFlow.Api.Ingestion.Messaging;

internal sealed class TestingMessagingHostedService : IHostedService
{
    // Unit-style HTTP tests choose their own publisher and need no background work.
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
