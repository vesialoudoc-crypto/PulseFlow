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
    public async Task OpenAsync_PostgreSqlContainerIsRunning_SetsConnectionStateToOpen()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);

        // Act
        await connection.OpenAsync();

        // Assert
        Assert.Equal(ConnectionState.Open, connection.State);
    }
}
