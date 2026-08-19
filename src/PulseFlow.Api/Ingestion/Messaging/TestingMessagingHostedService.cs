using Microsoft.Extensions.Hosting;

namespace PulseFlow.Api.Ingestion.Messaging;

internal sealed class TestingMessagingHostedService : IHostedService
{
    // Unit-style HTTP tests choose their own publisher and need no background work.
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
