namespace PulseFlow.Api.Startup;

public interface IStartupInitializer
{
    Task InitializeAsync(CancellationToken ct);
}
