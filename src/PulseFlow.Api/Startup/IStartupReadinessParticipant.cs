namespace PulseFlow.Api.Startup;

public interface IStartupReadinessParticipant
{
    Task WaitUntilStartedAsync(CancellationToken ct);
}
