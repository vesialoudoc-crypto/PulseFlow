using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;

namespace PulseFlow.Api.Ingestion.Messaging;

public static class IngestionMessagingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Keep RabbitMQ configuration inside messaging setup, not in application code.
        var rabbitMqConnectionString =
            configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException(
                "Connection string 'RabbitMq' is required.");

        // Fail at startup instead of finding a bad queue setting on the first request.
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetRequiredSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One manager is shared so publisher and workers use one broker connection.
        services.AddSingleton<RabbitMqConnectionManager>(serviceProvider =>
            new RabbitMqConnectionManager(
                rabbitMqConnectionString,
                serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));

        // The app sees only its publisher interface, not the RabbitMQ implementation.
        services.AddSingleton<RabbitMqIngestionBatchPublisher>(serviceProvider =>
            new RabbitMqIngestionBatchPublisher(
                serviceProvider.GetRequiredService<RabbitMqConnectionManager>(),
                serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<IIngestionBatchPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RabbitMqIngestionBatchPublisher>());

        // A factory makes a fresh consumer channel for every parser worker.
        services.AddSingleton<IIngestionBatchConsumerFactory>(serviceProvider =>
            new RabbitMqIngestionBatchConsumerFactory(
                serviceProvider.GetRequiredService<RabbitMqConnectionManager>(),
                serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));

        // Queue setup must finish before parser workers begin reading deliveries.
        services.AddSingleton<RabbitMqMessagingInitializer>();
        services.AddSingleton<EventParserConsumer>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

            return new EventParserConsumer(
                serviceProvider.GetRequiredService<IIngestionBatchConsumerFactory>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Ndjson.NdjsonRecordReader>(),
                serviceProvider.GetRequiredService<ILogger<EventParserConsumer>>(),
                options.ConsumerCount);
        });

        // HTTP tests replace the publisher and do not need a real broker or workers.
        services.AddSingleton<IHostedService>(serviceProvider =>
            IsTestingEnvironment(serviceProvider)
                ? new TestingMessagingHostedService()
                : serviceProvider.GetRequiredService<RabbitMqMessagingInitializer>());

        // This service is registered after setup, so normal host startup begins workers later.
        services.AddSingleton<IHostedService>(serviceProvider =>
            IsTestingEnvironment(serviceProvider)
                ? new TestingMessagingHostedService()
                : serviceProvider.GetRequiredService<EventParserConsumer>());

        return services;
    }

    private static bool IsTestingEnvironment(IServiceProvider serviceProvider)
    {
        return serviceProvider
            .GetRequiredService<IHostEnvironment>()
            .IsEnvironment("Testing");
    }
}
