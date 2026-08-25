using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Http;
using PulseFlow.Api.Ingestion.Http;

namespace PulseFlow.IntegrationTests.Infrastructure;

internal sealed class PulseFlowComponentTestHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private PulseFlowComponentTestHost(WebApplication application)
    {
        _application = application;
        Client = application.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _application.Services;

    public static async Task<PulseFlowComponentTestHost> StartAsync(
        Action<IServiceCollection> configureServices)
    {
        ArgumentNullException.ThrowIfNull(configureServices);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(EventsController).Assembly.GetName().Name }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddPulseFlowHttpApplication();
        configureServices(builder.Services);

        var application = builder.Build();
        application.UsePulseFlowHttpApplication();
        await application.StartAsync();

        return new PulseFlowComponentTestHost(application);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}