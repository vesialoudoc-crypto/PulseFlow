namespace PulseFlow.Api.Startup;

public sealed class StartupReadinessState
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _initializationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private StartupReadinessStatus _status = StartupReadinessStatus.Starting;
    private Exception? _failure;

    public StartupReadinessStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public Exception? Failure
    {
        get
        {
            lock (_sync)
            {
                return _failure;
            }
        }
    }

    public Task WaitUntilInitializationCompletedAsync(CancellationToken ct)
    {
        return _initializationCompleted.Task.WaitAsync(ct);
    }

    public Task WaitUntilReadyAsync(CancellationToken ct)
    {
        return _ready.Task.WaitAsync(ct);
    }

    public void MarkInitializationCompleted()
    {
        lock (_sync)
        {
            if (_status != StartupReadinessStatus.Starting)
            {
                return;
            }

            _initializationCompleted.TrySetResult();
        }
    }

    public void MarkReady()
    {
        lock (_sync)
        {
            if (_status != StartupReadinessStatus.Starting)
            {
                return;
            }

            _initializationCompleted.TrySetResult();
            _status = StartupReadinessStatus.Ready;
            _ready.TrySetResult();
        }
    }

    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_sync)
        {
            if (_status != StartupReadinessStatus.Starting)
            {
                return;
            }

            _failure = exception;
            _status = StartupReadinessStatus.Failed;
        }
    }
}
