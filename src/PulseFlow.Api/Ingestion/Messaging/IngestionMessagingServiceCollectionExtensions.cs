using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;
using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Ingestion.Messaging;

public static class IngestionMessagingServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionMessaging(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Keep RabbitMQ configuration inside messaging setup, not in application code.
        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq");
        if (rabbitMqConnectionString == null)
        {
            throw new InvalidOperationException("Connection string 'RabbitMq' is required.");
        }

        // Fail at startup instead of finding a bad queue setting on the first request.
        services
            .AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetRequiredSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One manager is shared so publisher and workers use one broker connection.
        services.AddSingleton<RabbitMqConnectionManager>(serviceProvider => new RabbitMqConnectionManager(
            rabbitMqConnectionString,
            serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value
        ));

        // The app sees only its publisher interface, not the RabbitMQ implementation.
        services.AddSingleton<RabbitMqIngestionBatchPublisher>(serviceProvider => new RabbitMqIngestionBatchPublisher(
            serviceProvider.GetRequiredService<RabbitMqConnectionManager>(),
            serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value
        ));
        services.AddSingleton<IIngestionBatchPublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RabbitMqIngestionBatchPublisher>()
        );

        // A factory makes a fresh consumer channel for every parser worker.
        services.AddSingleton<IIngestionBatchConsumerFactory>(
            serviceProvider => new RabbitMqIngestionBatchConsumerFactory(
                serviceProvider.GetRequiredService<RabbitMqConnectionManager>(),
                serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value
            )
        );

        // Queue setup participates in the process-wide startup initialization sequence.
        services.AddSingleton<RabbitMqMessagingInitializer>();
        services.AddSingleton<EventParserConsumer>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

            return new EventParserConsumer(
                serviceProvider.GetRequiredService<IIngestionBatchConsumerFactory>(),
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetRequiredService<Ndjson.NdjsonRecordReader>(),
                serviceProvider.GetRequiredService<ILogger<EventParserConsumer>>(),
                options.ConsumerCount,
                serviceProvider.GetRequiredService<StartupReadinessState>()
            );
        });

        services.AddSingleton<IStartupInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<RabbitMqMessagingInitializer>()
        );
        services.AddSingleton<IStartupReadinessParticipant>(serviceProvider =>
            serviceProvider.GetRequiredService<EventParserConsumer>()
        );
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<EventParserConsumer>()
        );

        return services;
    }
}
