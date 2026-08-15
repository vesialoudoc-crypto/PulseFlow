using System.Data;
using Npgsql;

namespace PulseFlow.IntegrationTests.Infrastructure;

public sealed class PostgreSqlInfrastructureTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInfrastructureTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Container_accepts_a_database_connection()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
    }
}
