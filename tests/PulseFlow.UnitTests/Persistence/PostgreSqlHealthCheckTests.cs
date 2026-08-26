using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Persistence;

namespace PulseFlow.UnitTests.Persistence;

public sealed class PostgreSqlHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ConnectionFails_ReturnsUnhealthy()
    {
        // Arrange
        await using var dbContext = CreateUnreachableDbContext();
        var healthCheck = new PostgreSqlHealthCheck(
            new FixedScopeFactory(dbContext),
            NullLogger<PostgreSqlHealthCheck>.Instance);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_CallerCancels_PropagatesOperationCanceledException()
    {
        // Arrange
        await using var dbContext = CreateUnreachableDbContext();
        var healthCheck = new PostgreSqlHealthCheck(
            new FixedScopeFactory(dbContext),
            NullLogger<PostgreSqlHealthCheck>.Instance);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            healthCheck.CheckHealthAsync(new HealthCheckContext(), cancellationSource.Token));

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    #region Test helpers

    private static PulseFlowDbContext CreateUnreachableDbContext()
    {
        return new PulseFlowDbContext(
            new DbContextOptionsBuilder<PulseFlowDbContext>()
                .UseNpgsql("Host=localhost;Port=1;Database=pulseflow;Username=test;Password=test;Timeout=1")
                .Options);
    }

    private sealed class FixedScopeFactory : IServiceScopeFactory
    {
        private readonly PulseFlowDbContext _dbContext;

        public FixedScopeFactory(PulseFlowDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IServiceScope CreateScope()
        {
            return new FixedScope(_dbContext);
        }
    }

    private sealed class FixedScope : IServiceScope
    {
        public FixedScope(PulseFlowDbContext dbContext)
        {
            ServiceProvider = new ServiceCollection()
                .AddSingleton(dbContext)
                .BuildServiceProvider();
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose() { }
    }

    #endregion
}
