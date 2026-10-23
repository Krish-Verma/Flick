using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public class PendingReplaySavesTests
{
    [Fact]
    public void AcceptedAcknowledgementRemainsPendingUntilMatchingTerminalEvent()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("accepted-save");

        Assert.False(saves.Acknowledge("accepted-save", accepted: true));
        Assert.Equal(1, saves.Count);
        Assert.False(saves.Complete("another-save"));
        Assert.Equal(1, saves.Count);
        Assert.True(saves.Complete("accepted-save"));
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void TerminalBeforeAcknowledgementCannotResurrectCompletedSave()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("fast-save");
        Assert.True(saves.Complete("fast-save"));

        Assert.False(saves.Acknowledge("fast-save", accepted: true));
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public void RejectedRequestOnlySettlesItsOwnSave()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("active-save");
        saves.Begin("busy-rejection");

        Assert.True(saves.Acknowledge("busy-rejection", accepted: false));
        Assert.Equal(1, saves.Count);
        Assert.True(saves.Complete("active-save"));
    }

    [Fact]
    public void OffOrFaultWithoutConfirmedExitDoesNotAbandonAcceptedSave()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("unsettled-save");
        saves.Acknowledge("unsettled-save", accepted: true);

        Assert.Empty(saves.HostExited(confirmed: false));
        Assert.Equal(1, saves.Count);
        Assert.Equal(new[] { "unsettled-save" }, saves.HostExited(confirmed: true));
        Assert.Equal(0, saves.Count);
        Assert.Empty(saves.HostExited(confirmed: true));
    }

    [Fact]
    public void DuplicatePendingIdentityIsRejectedWithoutLosingTheOriginal()
    {
        var saves = new PendingReplaySaves();
        saves.Begin("same-save");
        Assert.Throws<InvalidOperationException>(() => saves.Begin("same-save"));
        Assert.Equal(1, saves.Count);
        Assert.True(saves.Complete("same-save"));
    }

    [Fact]
    public async Task SaveDoesNotLaunchAnIdleHelperOrInventAPendingOperation()
    {
        using var client = new CaptureHostClient(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));

        Assert.False(await client.SaveReplayAsync("idle-save"));
        Assert.False(client.IsRunning);
        Assert.False(client.HasPendingReplaySaves);
        Assert.Equal("Replay capture is no longer active.", client.LastSaveError);
    }
}
