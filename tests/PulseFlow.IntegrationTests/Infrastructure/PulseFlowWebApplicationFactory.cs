using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PulseFlow.IntegrationTests.Infrastructure
{
    internal sealed class PulseFlowWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
        where TProgram : class
    {
        private readonly string _connectionString;
        private readonly string _rabbitMqConnectionString;
        private readonly string _queueName;
        private readonly int _consumerCount;
        private readonly bool _useRealRabbitMq;
        private readonly Action<IServiceCollection>? _configureTestServices;

        public PulseFlowWebApplicationFactory(string connectionString)
            : this(
                connectionString,
                "amqp://guest:guest@localhost:5672/",
                "pulseflow.integration-tests",
                consumerCount: 1,
                useRealRabbitMq: false)
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
                configureTestServices)
        {
        }

        private PulseFlowWebApplicationFactory(
            string connectionString,
            string rabbitMqConnectionString,
            string queueName,
            int consumerCount,
            bool useRealRabbitMq,
            Action<IServiceCollection>? configureTestServices = null)
        {
            _connectionString = connectionString;
            _rabbitMqConnectionString = rabbitMqConnectionString;
            _queueName = queueName;
            _consumerCount = consumerCount;
            _useRealRabbitMq = useRealRabbitMq;
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
            // Existing HTTP tests stay isolated from RabbitMQ unless they request a real broker.
            builder.UseEnvironment(_useRealRabbitMq ? "Development" : "Testing");

            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PulseFlow"] = _connectionString,
                        ["ConnectionStrings:RabbitMq"] = _rabbitMqConnectionString,
                        ["RabbitMq:QueueName"] = _queueName,
                        ["RabbitMq:ConsumerCount"] = _consumerCount.ToString(),
                        ["Ingestion:ChunkCapacity"] = "2"
                    });
            });

            return base.CreateHost(builder);
        }
    }
}
