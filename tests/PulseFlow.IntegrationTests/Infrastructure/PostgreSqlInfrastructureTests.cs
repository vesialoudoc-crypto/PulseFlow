using System.Data;
using Npgsql;

namespace PulseFlow.IntegrationTests.Infrastructure;

public sealed class PostgreSqlInfrastructureTests(PostgreSqlFixture fixture)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Container_accepts_a_database_connection()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
    }
}
