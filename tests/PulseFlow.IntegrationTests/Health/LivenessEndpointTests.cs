using System.Net;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Health;

public sealed class LivenessEndpointTests
{
    [Fact]
    public async Task GetLiveness_ProcessIsRunning_ReturnsOk()
    {
        // Arrange
        await using var host = await PulseFlowComponentTestHost.StartAsync(_ => { });

        // Act
        using var response = await host.Client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}