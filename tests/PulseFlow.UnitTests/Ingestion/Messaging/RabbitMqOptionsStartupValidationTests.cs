using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;

namespace PulseFlow.UnitTests.Ingestion.Messaging;

public sealed class RabbitMqOptionsStartupValidationTests
{
    public static IEnumerable<object[]> NonPositiveTimeouts()
    {
        yield return ["TopologyDeclarationTimeout", "00:00:00"];
        yield return ["PublisherChannelTimeout", "-00:00:01"];
    }

    [Theory]
    [MemberData(nameof(NonPositiveTimeouts))]
    public void GetValue_TimeoutIsZeroOrNegative_ThrowsOptionsValidationException(
        string timeoutName,
        string timeoutValue)
    {
        // Arrange
        using var host = CreateHost(new Dictionary<string, string?> { [$"RabbitMq:{timeoutName}"] = timeoutValue });

        // Act
        Action action = () => _ = host.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        // Assert
        Assert.Throws<OptionsValidationException>(action);
    }

    [Fact]
    public void GetValue_TimerBasedTimeoutExceedsSafeMaximum_ThrowsOptionsValidationException()
    {
        // Arrange
        var tooLargeTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1).ToString();
        using var host = CreateHost(
            new Dictionary<string, string?> { ["RabbitMq:PublishConfirmationTimeout"] = tooLargeTimeout });

        // Act
        Action action = () => _ = host.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        // Assert
        Assert.Throws<OptionsValidationException>(action);
    }

    [Fact]
    public void Build_ValidConfiguration_ProvidesValidatedOptions()
    {
        // Arrange
        using var host = CreateHost();

        // Act
        var options = host.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(3), options.TopologyDeclarationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), options.PublisherChannelTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PublishConfirmationTimeout);
    }

    [Fact]
    public void CreateConnectionManager_ValidConfiguration_AppliesProviderNativeTimeouts()
    {
        // Arrange
        using var host = CreateHost();
        var options = host.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        var manager = host.Services.GetRequiredService<RabbitMqConnectionManager>();

        // Act
        var connectionFactory = (RabbitMQ.Client.ConnectionFactory)typeof(RabbitMqConnectionManager)
            .GetField("_connectionFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;

        // Assert
        Assert.Equal(options.ConnectionTimeout, connectionFactory.RequestedConnectionTimeout);
        Assert.Equal(options.HandshakeTimeout, connectionFactory.HandshakeContinuationTimeout);
        Assert.Equal(options.ContinuationTimeout, connectionFactory.ContinuationTimeout);
    }

    #region Test helpers

    private static IHost CreateHost(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@localhost:5672/",
            ["RabbitMq:QueueName"] = "pulseflow.options-test",
            ["RabbitMq:ConnectionTimeout"] = "00:00:01",
            ["RabbitMq:HandshakeTimeout"] = "00:00:02",
            ["RabbitMq:ContinuationTimeout"] = "00:00:03",
            ["RabbitMq:TopologyDeclarationTimeout"] = "00:00:03",
            ["RabbitMq:PublisherChannelTimeout"] = "00:00:04",
            ["RabbitMq:PublishConfirmationTimeout"] = "00:00:05",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddIngestionMessaging(builder.Configuration, TimeSpan.FromSeconds(1));

        return builder.Build();
    }

    #endregion
}
