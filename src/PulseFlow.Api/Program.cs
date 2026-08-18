using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PulseFlow.Api.Http;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("PulseFlow")
    ?? throw new InvalidOperationException(
        "Connection string 'PulseFlow' is required.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException(
        "Connection string 'RabbitMq' is required.");

var rabbitMqOptions =
    builder.Configuration
        .GetRequiredSection(RabbitMqOptions.SectionName)
        .Get<RabbitMqOptions>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{RabbitMqOptions.SectionName}' is required.");

Validator.ValidateObject(
    rabbitMqOptions,
    new ValidationContext(rabbitMqOptions),
    validateAllProperties: true);

var rabbitMqConnectionFactory = new ConnectionFactory
{
    Uri = new Uri(rabbitMqConnectionString)
};

IConnection? rabbitMqConnection = null;
IChannel? rabbitMqChannel = null;

if (!builder.Environment.IsEnvironment("Testing"))
{
    rabbitMqConnection = await rabbitMqConnectionFactory.CreateConnectionAsync();

    try
    {
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        rabbitMqChannel = await rabbitMqConnection.CreateChannelAsync(channelOptions);

        await rabbitMqChannel.QueueDeclareAsync(
            queue: rabbitMqOptions.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
    }
    catch
    {
        if (rabbitMqChannel is not null)
        {
            await rabbitMqChannel.DisposeAsync();
        }

        await rabbitMqConnection.DisposeAsync();
        throw;
    }
}

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<PulseFlowDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<NdjsonRecordReader>();
builder.Services.AddSingleton<EventEnvelopeValidator>();

builder.Services.AddScoped<IEventChunkStore, EfCoreEventChunkStore>();

if (rabbitMqChannel is not null)
{
    builder.Services.AddSingleton<IChannel>(rabbitMqChannel);
    builder.Services.AddSingleton<IConnection>(rabbitMqConnection!);

    builder.Services.AddSingleton<IIngestionBatchPublisher>(services =>
        new RabbitMqIngestionBatchPublisher(
            services.GetRequiredService<IChannel>(),
            rabbitMqOptions));

    // The consumer shares the connection but owns a different channel.
    builder.Services.AddSingleton<IRabbitMqConsumerChannel>(services =>
        new RabbitMqConsumerChannel(
            services.GetRequiredService<IConnection>(),
            rabbitMqOptions));

    builder.Services.AddHostedService<EventParserConsumer>();
}

builder.Services.AddScoped<IngestEventsHandler>(services =>
{
    var options = services
        .GetRequiredService<IOptions<IngestionOptions>>()
        .Value;

    return new IngestEventsHandler(
        services.GetRequiredService<EventEnvelopeValidator>(),
        services.GetRequiredService<IEventChunkStore>(),
        options.ChunkCapacity);
});

builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer((operation, context, _) =>
    {
        if (string.Equals(
                context.Description.HttpMethod,
                HttpMethods.Post,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                context.Description.RelativePath,
                "api/events",
                StringComparison.OrdinalIgnoreCase))
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = false,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/x-ndjson"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Description = "Raw NDJSON batch accepted for asynchronous processing."
                        }
                    }
                }
            };
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (rabbitMqChannel is not null && rabbitMqConnection is not null)
{
    app.Lifetime.ApplicationStopped.Register(() =>
    {
        try
        {
            rabbitMqChannel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            rabbitMqConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    });
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "PulseFlow v1");
    });
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
