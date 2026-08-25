using Microsoft.EntityFrameworkCore;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Persistence;

public sealed class PostgreSqlStartupInitializer : IStartupInitializer
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PostgreSqlStartupInitializer(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();

        if (!await dbContext.Database.CanConnectAsync(ct))
        {
            throw new InvalidOperationException("PostgreSQL is not available during startup initialization.");
        }

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(ct);
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException(
                "PostgreSQL has pending migrations. Run the migrations service before starting the API."
            );
        }
    }
}
