using PulseFlow.Api.Startup;

namespace PulseFlow.UnitTests.Startup;

public sealed class StartupReadinessStateTests
{
    [Fact]
    public async Task WaitUntilReadyAsync_StateIsStarting_DoesNotCompleteUntilReady()
    {
        // Arrange
        var state = new StartupReadinessState();
        var waitTask = state.WaitUntilReadyAsync(CancellationToken.None);

        // Act
        var completedBeforeReady = waitTask.IsCompleted;
        state.MarkReady();
        await waitTask;

        // Assert
        Assert.False(completedBeforeReady);
        Assert.Equal(StartupReadinessStatus.Ready, state.Status);
    }

    [Fact]
    public async Task WaitUntilReadyAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var state = new StartupReadinessState();
        using var cancellationSource = new CancellationTokenSource();
        var waitTask = state.WaitUntilReadyAsync(cancellationSource.Token);

        // Act
        cancellationSource.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }
}
