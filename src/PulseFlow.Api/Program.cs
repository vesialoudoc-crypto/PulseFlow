using Microsoft.Extensions.Options;
using PulseFlow.Api.Http;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.RateLimiting;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.Api.Startup;

var builder = WebApplication.CreateBuilder(args);

string? connectionString = builder.Configuration.GetConnectionString("PulseFlow");
if (connectionString is null)
{
    throw new InvalidOperationException("Connection string 'PulseFlow' is required.");
}

builder.Services.AddPulseFlowHttpApplication();
builder.Services.AddStartupInitialization();

builder.Services.AddIngestionOptions(builder.Configuration);

builder.Services.AddPulseFlowPersistence(connectionString);

builder.Services.AddSingleton<NdjsonRecordReader>();
builder.Services.AddSingleton<EventEnvelopeValidator>();

builder.Services.AddScoped<IEventChunkStore, EfCoreEventChunkStore>();
builder.Services.AddIngestionMessaging(builder.Configuration);
builder.Services.AddIngestionRateLimiting(builder.Configuration);

builder.Services.AddScoped<IngestEventsHandler>(services =>
{
    var options = services.GetRequiredService<IOptions<IngestionOptions>>().Value;

    return new IngestEventsHandler(
        services.GetRequiredService<EventEnvelopeValidator>(),
        services.GetRequiredService<IEventChunkStore>(),
        options.ChunkCapacity
    );
});

var app = builder.Build();

app.UsePulseFlowHttpApplication();

app.Run();
