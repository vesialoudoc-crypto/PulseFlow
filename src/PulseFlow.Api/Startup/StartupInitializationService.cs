namespace PulseFlow.Api.Startup;

public sealed class StartupInitializationService : BackgroundService
{
    private readonly IReadOnlyList<IStartupInitializer> _initializers;
    private readonly IReadOnlyList<IStartupReadinessParticipant> _readinessParticipants;
    private readonly StartupReadinessState _readinessState;
    private readonly ILogger<StartupInitializationService> _logger;

    public StartupInitializationService(
        IEnumerable<IStartupInitializer> initializers,
        IEnumerable<IStartupReadinessParticipant> readinessParticipants,
        StartupReadinessState readinessState,
        ILogger<StartupInitializationService> logger
    )
    {
        _initializers = initializers.ToArray();
        _readinessParticipants = readinessParticipants.ToArray();
        _readinessState = readinessState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? activeComponentType = null;

        try
        {
            foreach (var initializer in _initializers)
            {
                activeComponentType = initializer.GetType().FullName;
                _logger.LogInformation("Running startup initializer {StartupInitializerType}.", activeComponentType);
                await initializer.InitializeAsync(ct);
            }

            _readinessState.MarkInitializationCompleted();

            foreach (var readinessParticipant in _readinessParticipants)
            {
                activeComponentType = readinessParticipant.GetType().FullName;
                _logger.LogInformation(
                    "Waiting for startup readiness participant {StartupReadinessParticipantType}.",
                    activeComponentType
                );
                await readinessParticipant.WaitUntilStartedAsync(ct);
            }

            _readinessState.MarkReady();
            _logger.LogInformation("Mandatory startup initialization completed successfully.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Startup initialization was cancelled because the host is stopping.");
        }
        catch (Exception exception)
        {
            _readinessState.MarkFailed(exception);
            _logger.LogError(
                exception,
                "Mandatory startup initialization failed in {StartupComponentType}.",
                activeComponentType ?? exception.GetType().FullName ?? exception.GetType().Name
            );
        }
    }
}
