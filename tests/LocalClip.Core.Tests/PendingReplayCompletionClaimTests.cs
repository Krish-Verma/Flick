using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public class PendingReplayCompletionClaimTests
{
    [Fact]
    public void TerminalClaimKeepsProtectionUntilDispatchFinishes()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("accepted");

        Assert.True(saves.TryClaimCompletion("accepted"));
        Assert.Equal(1, saves.Count);
        Assert.False(saves.TryClaimCompletion("accepted"));
        Assert.False(saves.TryClaimCompletion("unknown"));
        Assert.False(saves.Complete("accepted"));
        Assert.False(saves.Acknowledge("accepted", accepted: false));
        Assert.Equal(1, saves.Count);

        Assert.True(saves.FinishCompletion("accepted"));
        Assert.False(saves.FinishCompletion("accepted"));
        Assert.Equal(0, saves.Count);
        Assert.False(saves.Acknowledge("accepted", accepted: true));
        Assert.False(saves.TryClaimCompletion("accepted"));
    }

    [Fact]
    public void ConfirmedExitDoesNotAbandonCompletionAlreadyBeingDelivered()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("delivering");
        saves.Begin("unsettled");
        Assert.True(saves.TryClaimCompletion("delivering"));

        Assert.Empty(saves.HostExited(confirmed: false));
        Assert.Equal(new[] { "unsettled" }, saves.HostExited(confirmed: true));
        Assert.Equal(1, saves.Count);
        Assert.False(saves.FinishCompletion("unsettled"));
        Assert.True(saves.FinishCompletion("delivering"));
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task ConcurrentTerminalEventsHaveExactlyOneOwner()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("one-save");

        var claims = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => saves.TryClaimCompletion("one-save"))));

        Assert.Single(claims, claimed => claimed);
        Assert.Equal(1, saves.Count);
        Assert.True(saves.FinishCompletion("one-save"));
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void MissingAcknowledgementDoesNotDiscardAValidLateTerminalEvent()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("ack-lost");
        // No acknowledgement is conclusive after an IPC timeout.
        Assert.Empty(saves.HostExited(confirmed: false));
        Assert.True(saves.TryClaimCompletion("ack-lost"));
        Assert.Equal(1, saves.Count);
        Assert.True(saves.FinishCompletion("ack-lost"));
        Assert.Equal(0, saves.Count);
    }
}
