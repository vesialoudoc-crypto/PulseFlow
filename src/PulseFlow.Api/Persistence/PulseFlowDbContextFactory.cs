using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PulseFlow.Api.Persistence;

public sealed class PulseFlowDbContextFactory : IDesignTimeDbContextFactory<PulseFlowDbContext>
{
    public PulseFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>().UseNpgsql().Options;

        return new PulseFlowDbContext(options);
    }
}
