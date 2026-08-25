using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PulseFlow.IntegrationTests.Infrastructure;

internal sealed class PulseFlowSystemWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _postgreSqlConnectionString;
    private readonly string _rabbitMqConnectionString;
    private readonly string _redisConnectionString;
    private readonly string _queueName;
    private readonly int _consumerCount;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public PulseFlowSystemWebApplicationFactory(
        string postgreSqlConnectionString,
        string rabbitMqConnectionString,
        string redisConnectionString,
        string queueName,
        int consumerCount,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _postgreSqlConnectionString = postgreSqlConnectionString;
        _rabbitMqConnectionString = rabbitMqConnectionString;
        _redisConnectionString = redisConnectionString;
        _queueName = queueName;
        _consumerCount = consumerCount;
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PulseFlow"] = _postgreSqlConnectionString,
                    ["ConnectionStrings:RabbitMq"] = _rabbitMqConnectionString,
                    ["ConnectionStrings:Redis"] = _redisConnectionString,
                    ["RabbitMq:QueueName"] = _queueName,
                    ["RabbitMq:ConsumerCount"] = _consumerCount.ToString(),
                    ["RabbitMq:PublisherChannelCount"] = "4",
                    ["Ingestion:ChunkCapacity"] = "2",
                    ["Ingestion:MaxBatchBytes"] = "10485760",
                    ["IngestionRateLimit:RequestLimit"] = "100",
                    ["IngestionRateLimit:WindowDuration"] = TimeSpan.FromMinutes(1).ToString(),
                }
            );
        });

        return base.CreateHost(builder);
    }
}
