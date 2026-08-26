using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Persistence;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Health;

public sealed class PostgreSqlHealthCheckTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlHealthCheckTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CheckHealthAsync_PostgreSqlIsAvailable_ReturnsHealthy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<PulseFlowDbContext>(options => options.UseNpgsql(_fixture.ConnectionString));
        await using var serviceProvider = services.BuildServiceProvider();
        var healthCheck = new PostgreSqlHealthCheck(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PostgreSqlHealthCheck>.Instance);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
