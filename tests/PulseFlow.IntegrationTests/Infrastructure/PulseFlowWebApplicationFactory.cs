using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PulseFlow.IntegrationTests.Infrastructure;

internal sealed class PulseFlowWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly string _connectionString;
    private readonly string _rabbitMqConnectionString;
    private readonly string _redisConnectionString;
    private readonly string _queueName;
    private readonly int _consumerCount;
    private readonly int _requestLimit;
    private readonly TimeSpan _windowDuration;
    private readonly bool _useRealRabbitMq;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public PulseFlowWebApplicationFactory(
        string connectionString,
        string? redisConnectionString = null,
        int requestLimit = 100,
        TimeSpan? windowDuration = null)
        : this(
            connectionString,
            "amqp://guest:guest@localhost:5672/",
            "pulseflow.integration-tests",
            consumerCount: 1,
            useRealRabbitMq: false,
            configureTestServices: null,
            redisConnectionString: redisConnectionString,
            requestLimit: requestLimit,
            windowDuration: windowDuration ?? TimeSpan.FromMinutes(1))
    {
    }

    public PulseFlowWebApplicationFactory(
        string connectionString,
        string rabbitMqConnectionString,
        string queueName,
        int consumerCount,
        Action<IServiceCollection>? configureTestServices = null)
        : this(
            connectionString,
            rabbitMqConnectionString,
            queueName,
            consumerCount,
            useRealRabbitMq: true,
            configureTestServices: configureTestServices,
            redisConnectionString: null,
            requestLimit: 100,
            windowDuration: TimeSpan.FromMinutes(1))
    {
    }

    private PulseFlowWebApplicationFactory(
        string connectionString,
        string rabbitMqConnectionString,
        string queueName,
        int consumerCount,
        bool useRealRabbitMq,
        Action<IServiceCollection>? configureTestServices,
        string? redisConnectionString,
        int requestLimit,
        TimeSpan windowDuration)
    {
        _connectionString = connectionString;
        _rabbitMqConnectionString = rabbitMqConnectionString;
        _redisConnectionString = redisConnectionString ?? "localhost:6379,abortConnect=false";
        _queueName = queueName;
        _consumerCount = consumerCount;
        _requestLimit = requestLimit;
        _windowDuration = windowDuration;
        _useRealRabbitMq = useRealRabbitMq;
        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if(_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Existing HTTP tests stay isolated from RabbitMQ unless they request a real broker.
        builder.UseEnvironment(_useRealRabbitMq ? "Development" : "Testing");

        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PulseFlow"] = _connectionString,
                    ["ConnectionStrings:RabbitMq"] = _rabbitMqConnectionString,
                    ["ConnectionStrings:Redis"] = _redisConnectionString,
                    ["RabbitMq:QueueName"] = _queueName,
                    ["RabbitMq:ConsumerCount"] = _consumerCount.ToString(),
                    ["RabbitMq:PublisherChannelCount"] = "4",
                    ["Ingestion:ChunkCapacity"] = "2",
                    ["IngestionRateLimit:RequestLimit"] = _requestLimit.ToString(),
                    ["IngestionRateLimit:WindowDuration"] = _windowDuration.ToString()
                });
        });

        return base.CreateHost(builder);
    }
}
