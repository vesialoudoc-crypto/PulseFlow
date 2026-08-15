using Microsoft.EntityFrameworkCore;
using PulseFlow.Api.Persistence.Events;

namespace PulseFlow.Api.Persistence;

public sealed class PulseFlowDbContext(DbContextOptions<PulseFlowDbContext> options)
    : DbContext(options)
{
    public DbSet<EventRecord> EventRecords => Set<EventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EventRecordConfiguration());
    }
}
