using PulseFlow.Api.Persistence;

namespace PulseFlow.UnitTests.Persistence;

public sealed class PostgreSqlOptionsTests
{
    [Fact]
    public void CommandTimeoutSeconds_SubSecondFraction_RoundsUpToWholeSecond()
    {
        // Arrange
        var options = new PostgreSqlOptions { CommandTimeout = TimeSpan.FromMilliseconds(1500) };

        // Act
        var timeoutSeconds = options.CommandTimeoutSeconds;

        // Assert
        Assert.Equal(2, timeoutSeconds);
    }

    [Fact]
    public void ConnectionTimeoutSeconds_ExceedsProviderLimit_ThrowsOptionsValidationException()
    {
        // Arrange
        var options = new PostgreSqlOptions { ConnectionTimeout = TimeSpan.FromSeconds((double)int.MaxValue + 1) };

        // Act
        var action = () =>
        {
            _ = options.ConnectionTimeoutSeconds;
        };

        // Assert
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(action);
    }
}
