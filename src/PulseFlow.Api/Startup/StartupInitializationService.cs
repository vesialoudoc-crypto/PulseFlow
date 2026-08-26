using Microsoft.Extensions.Options;

namespace PulseFlow.Api.Startup;

public sealed class StartupInitializationService : BackgroundService
{
    private readonly IReadOnlyList<IStartupInitializer> _initializers;
    private readonly IReadOnlyList<IStartupReadinessParticipant> _readinessParticipants;
    private readonly StartupReadinessState _readinessState;
    private readonly StartupOptions _options;
    private readonly ILogger<StartupInitializationService> _logger;
    private string? _activeComponentType;

    public StartupInitializationService(
        IEnumerable<IStartupInitializer> initializers,
        IEnumerable<IStartupReadinessParticipant> readinessParticipants,
        StartupReadinessState readinessState,
        IOptions<StartupOptions> options,
        ILogger<StartupInitializationService> logger
    )
    {
        _initializers = initializers.ToArray();
        _readinessParticipants = readinessParticipants.ToArray();
        _readinessState = readinessState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_options.InitializationTimeout);

        try
        {
            await RunInitializationAsync(timeoutSource.Token);

            _readinessState.MarkReady();
            _logger.LogInformation("Mandatory startup initialization completed successfully.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Startup initialization was cancelled because the host is stopping.");
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            var timeoutException = new TimeoutException(
                $"Startup initialization exceeded its configured timeout of {_options.InitializationTimeout}."
            );
            _readinessState.MarkFailed(timeoutException);
            _logger.LogError(
                timeoutException,
                "Mandatory startup initialization timed out in {StartupComponentType} after {ConfiguredTimeout}.",
                _activeComponentType ?? typeof(StartupInitializationService).FullName,
                _options.InitializationTimeout
            );
            throw timeoutException;
        }
        catch (Exception exception)
        {
            _readinessState.MarkFailed(exception);
            _logger.LogError(
                exception,
                "Mandatory startup initialization failed in {StartupComponentType}.",
                _activeComponentType ?? exception.GetType().FullName ?? exception.GetType().Name
            );
            throw;
        }
    }

    private async Task RunInitializationAsync(CancellationToken ct)
    {
        foreach (var initializer in _initializers)
        {
            _activeComponentType = initializer.GetType().FullName;
            _logger.LogInformation("Running startup initializer {StartupInitializerType}.", _activeComponentType);
            await initializer.InitializeAsync(ct);
        }

        _readinessState.MarkInitializationCompleted();

        foreach (var readinessParticipant in _readinessParticipants)
        {
            _activeComponentType = readinessParticipant.GetType().FullName;
            _logger.LogInformation(
                "Waiting for startup readiness participant {StartupReadinessParticipantType}.",
                _activeComponentType
            );
            await readinessParticipant.WaitUntilStartedAsync(ct);
        }
    }
}
