using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Persistence;

namespace PulseFlow.Api.Persistence.Events;

public sealed class EfCoreEventChunkStore : IEventChunkStore
{
    private readonly PulseFlowDbContext _dbContext;

    public EfCoreEventChunkStore(PulseFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task StoreAsync(IReadOnlyCollection<EventEnvelope> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var databaseTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        await using var command = new NpgsqlCommand
        {
            Connection = connection,
            Transaction = databaseTransaction,
            CommandText = CreateInsertCommand(events),
        };

        var receivedAt = DateTime.UtcNow;
        int index = 0;

        foreach (var envelope in events)
        {
            command.Parameters.AddWithValue($"id{index}", NpgsqlDbType.Uuid, Guid.NewGuid());
            command.Parameters.AddWithValue($"eventId{index}", NpgsqlDbType.Uuid, envelope.EventId);
            command.Parameters.AddWithValue($"type{index}", NpgsqlDbType.Text, envelope.Type);
            command.Parameters.AddWithValue($"source{index}", NpgsqlDbType.Text, envelope.Source);
            command.Parameters.AddWithValue($"occurredAt{index}", NpgsqlDbType.TimestampTz, envelope.OccurredAt);
            command.Parameters.AddWithValue($"receivedAt{index}", NpgsqlDbType.TimestampTz, receivedAt);
            command.Parameters.AddWithValue($"payload{index}", NpgsqlDbType.Jsonb, envelope.Payload.GetRawText());
            index++;
        }

        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static string CreateInsertCommand(IReadOnlyCollection<EventEnvelope> events)
    {
        var values = new StringBuilder();

        for (int index = 0; index < events.Count; index++)
        {
            if (index > 0)
            {
                values.Append(", ");
            }

            values.Append(
                $"(@id{index}, @eventId{index}, @type{index}, @source{index}, "
                    + $"@occurredAt{index}, @receivedAt{index}, @payload{index})"
            );
        }

        return "INSERT INTO events (id, event_id, type, source, occurred_at, received_at, payload) "
            + $"VALUES {values} ON CONFLICT (source, event_id) DO NOTHING;";
    }
}
