using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;

namespace PulseFlow.UnitTests.Ingestion;

public sealed class IngestionOptionsStartupValidationTests
{
    public static IEnumerable<object?[]> InvalidMaxBatchByteValues()
    {
        yield return [null];
        yield return ["0"];
        yield return ["-1"];
        yield return [(IngestionOptions.MaximumMaxBatchBytes + 1).ToString()];
    }

    [Theory]
    [MemberData(nameof(InvalidMaxBatchByteValues))]
    public async Task StartAsync_InvalidMaxBatchBytes_ThrowsOptionsValidationException(string? maxBatchBytes)
    {
        // Arrange
        using var host = CreateHost(maxBatchBytes);

        // Act
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        Assert.Contains(nameof(IngestionOptions.MaxBatchBytes), exception.Message);
    }

    #region Test helpers

    private static IHost CreateHost(string? maxBatchBytes)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var values = new Dictionary<string, string?> { ["Ingestion:ChunkCapacity"] = "1" };

        if (maxBatchBytes is not null)
        {
            values["Ingestion:MaxBatchBytes"] = maxBatchBytes;
        }

        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddIngestionOptions(builder.Configuration);

        return builder.Build();
    }

    #endregion
}
