using LocalClip.Core.Audio;
namespace LocalClip.Core.Tests;
public sealed class AudioPreviewOperationTests
{
    [Fact] public async Task CancellationKeepsOwnershipUntilWorkerCleanupSettles()
    {
        var operation = new AudioPreviewOperation();
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = operation.StartAsync(async token => { began.SetResult(); using var registration = token.Register(() => cancelled.TrySetResult()); await cancelled.Task; await cleanup.Task; token.ThrowIfCancellationRequested(); return "unused"; }, default);
        await began.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await operation.CancelAndWaitAsync(TimeSpan.FromMilliseconds(20)));
        Assert.True(operation.IsBusy);
        Assert.Throws<InvalidOperationException>(() => { _ = operation.StartAsync(_ => Task.FromResult("second"), default); });
        cleanup.SetResult(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(await operation.CancelAndWaitAsync()); Assert.False(operation.IsBusy);
    }
    [Fact] public async Task FailedExitConfirmationNeverReleasesSafetyFence()
    {
        var operation = new AudioPreviewOperation();
        await Assert.ThrowsAsync<MediaWorkerCleanupException>(() => operation.StartAsync(_ => throw new MediaWorkerCleanupException(), default));
        Assert.True(operation.CleanupFailed); Assert.False(await operation.CancelAndWaitAsync());
        Assert.True(await operation.CancelAndWaitAsync(allowSettledShutdown: true));
        Assert.True(operation.CleanupFailed); // Final shutdown must not reset admission for new work.
        Assert.Throws<InvalidOperationException>(() => { _ = operation.StartAsync(_ => Task.FromResult("second"), default); });
    }
}
