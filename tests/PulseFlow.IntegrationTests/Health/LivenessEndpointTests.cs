using System.Net;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Health;

public sealed class LivenessEndpointTests
{
    [Fact]
    public async Task GetLiveness_ProcessIsRunning_ReturnsOk()
    {
        // Arrange
        using var factory = new PulseFlowWebApplicationFactory<Program>(
            "Host=localhost;Database=pulseflow_tests;Username=postgres;Password=postgres");
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
