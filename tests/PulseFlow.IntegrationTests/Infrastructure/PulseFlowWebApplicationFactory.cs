using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PulseFlow.IntegrationTests.Infrastructure
{
    internal sealed class PulseFlowWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram>
        where TProgram : class
    {
        private readonly string _connectionString;

        public PulseFlowWebApplicationFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PulseFlow"] = _connectionString,
                        ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@localhost:5672/",
                        ["RabbitMq:QueueName"] = "pulseflow.integration-tests",
                        ["Ingestion:ChunkCapacity"] = "2"
                    });
            });

            return base.CreateHost(builder);
        }
    }
}
