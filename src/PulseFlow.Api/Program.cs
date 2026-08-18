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

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("PulseFlow")
    ?? throw new InvalidOperationException(
        "Connection string 'PulseFlow' is required.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMq")
    ?? throw new InvalidOperationException(
        "Connection string 'RabbitMq' is required.");

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddOptions<IngestionOptions>()
    .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<PulseFlowDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<NdjsonRecordReader>();
builder.Services.AddSingleton<EventEnvelopeValidator>();

builder.Services.AddScoped<IEventChunkStore, EfCoreEventChunkStore>();

builder.Services.AddSingleton<IRabbitMqPublisherChannelProvider>(services =>
    new RabbitMqPublisherChannelProvider(
        rabbitMqConnectionString,
        services.GetRequiredService<IOptions<RabbitMqOptions>>().Value));

builder.Services.AddSingleton<IIngestionBatchPublisher>(services =>
    new RabbitMqIngestionBatchPublisher(
        services.GetRequiredService<IRabbitMqPublisherChannelProvider>()));

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
