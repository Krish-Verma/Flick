using System.Reflection;
using System.Text.Json;
using LocalClip.Core.Ipc;

namespace LocalClip.Core.Tests;

public class ReplayTerminalDispatchTests
{
    // Exercise the real event routing without launching a native process or devices.
    private static PendingReplaySaves Pending(CaptureHostClient client) =>
        (PendingReplaySaves)typeof(CaptureHostClient).GetField("_pendingSaves", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;

    private static void Deliver(CaptureHostClient client, string name, string json) =>
        typeof(CaptureHostClient).GetMethod("ProcessIncomingEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, new object?[] { name, JsonSerializer.Deserialize<JsonElement>(json) });

    [Fact]
    public void OnlyMatchingCompletionIsForwardedOnceAndGuardedThroughoutDispatch()
    {
        using var client = new CaptureHostClient("unused-test-helper.exe");
        Pending(client).Begin("expected");
        var observed = new List<string>();
        var protectedDuringDispatch = new List<bool>();
        (string Id, string Path, IReadOnlyList<string> Tracks)? saved = null;
        bool protectedAfterSettlement = true;
        client.EventReceived += (name, _) =>
        {
            protectedDuringDispatch.Add(client.HasPendingReplaySaves);
            observed.Add(name);
        };
        client.ReplaySavedWithTracks += (id, path, names) =>
        {
            protectedDuringDispatch.Add(client.HasPendingReplaySaves);
            saved = (id, path, names);
            observed.Add("tracks");
        };
        client.ReplaySaved += (_, _) =>
        {
            protectedDuringDispatch.Add(client.HasPendingReplaySaves);
            observed.Add("saved");
        };
        client.PendingReplaySavesChanged += () =>
        {
            protectedAfterSettlement = client.HasPendingReplaySaves;
            observed.Add("settled");
        };

        Deliver(client, "replay_saved", "{\"save_id\":\"unknown\",\"path\":\"unrelated.mkv\"}");
        Assert.Empty(observed);
        const string valid = "{\"save_id\":\"expected\",\"path\":\"saved.mkv\",\"tracks\":[\"Combined Mix\",\"System Audio\"]}";
        Deliver(client, "replay_saved", valid);
        Deliver(client, "replay_saved", valid);
        Deliver(client, "save_failed", "{\"save_id\":\"expected\",\"reason\":\"stale failure\"}");

        Assert.Equal(new[] { "replay_saved", "tracks", "saved", "settled" }, observed);
        Assert.All(protectedDuringDispatch, value => Assert.True(value));
        Assert.False(protectedAfterSettlement);
        Assert.True(saved.HasValue);
        Assert.Equal("expected", saved.Value.Id);
        Assert.Equal("saved.mkv", saved.Value.Path);
        Assert.Equal(new[] { "Combined Mix", "System Audio" }, saved.Value.Tracks);
        Assert.False(client.HasPendingReplaySaves);
    }

    [Fact]
    public void FailingSubscriberCannotSuppressOtherConsumersOrStrandTheGuard()
    {
        using var client = new CaptureHostClient("unused-test-helper.exe");
        Pending(client).Begin("expected");
        int delivered = 0;
        client.EventReceived += (_, _) => throw new InvalidOperationException("subscriber failure");
        client.ReplaySavedWithTracks += (_, _, _) => throw new InvalidOperationException("subscriber failure");
        client.ReplaySavedWithTracks += (_, _, _) => delivered++;
        client.ReplaySaved += (_, _) => delivered++;

        Deliver(client, "replay_saved", "{\"save_id\":\"expected\",\"path\":\"saved.mkv\"}");

        Assert.Equal(2, delivered);
        Assert.False(client.HasPendingReplaySaves);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"save_id\":\"expected\"}")]
    [InlineData("{\"save_id\":5,\"path\":\"saved.mkv\"}")]
    [InlineData("{\"save_id\":\"expected\",\"path\":\"\"}")]
    [InlineData("{\"save_id\":\"expected\",\"path\":\"saved.mkv\",\"tracks\":[7]}")]
    public void InvalidSavedPayloadDoesNotSettleOrForwardPendingSave(string json)
    {
        using var client = new CaptureHostClient("unused-test-helper.exe");
        Pending(client).Begin("expected");
        int delivered = 0;
        client.EventReceived += (_, _) => delivered++;
        client.ReplaySaved += (_, _) => delivered++;

        Deliver(client, "replay_saved", json);

        Assert.Equal(0, delivered);
        Assert.True(client.HasPendingReplaySaves);
    }

    [Fact]
    public void FailureCompletionIsCorrelatedAndGuardsItsSubscribers()
    {
        using var client = new CaptureHostClient("unused-test-helper.exe");
        Pending(client).Begin("expected");
        int delivered = 0;
        string? observedName = null;
        bool protectedAtEntry = false, protectedAfterExit = false, rejectionReleased = true;
        IReadOnlyList<string>? abandoned = null;
        client.EventReceived += (name, _) =>
        {
            observedName = name;
            protectedAtEntry = client.HasPendingReplaySaves;
            rejectionReleased = Pending(client).Acknowledge("expected", accepted: false);
            abandoned = Pending(client).HostExited(confirmed: true);
            protectedAfterExit = client.HasPendingReplaySaves;
            delivered++;
        };

        Deliver(client, "save_failed", "{\"save_id\":\"unknown\",\"reason\":\"unrelated\"}");
        Deliver(client, "save_failed", "{\"save_id\":\"expected\",\"reason\":\"disk unavailable\"}");
        Deliver(client, "save_failed", "{\"save_id\":\"expected\",\"reason\":\"duplicate\"}");

        Assert.Equal(1, delivered);
        Assert.Equal("save_failed", observedName);
        Assert.True(protectedAtEntry);
        Assert.True(protectedAfterExit);
        Assert.False(rejectionReleased);
        Assert.NotNull(abandoned);
        Assert.Empty(abandoned);
        Assert.False(client.HasPendingReplaySaves);
    }

    [Fact]
    public void ReentrantDuplicateIsDroppedWithoutReleasingOtherPendingSaves()
    {
        using var client = new CaptureHostClient("unused-test-helper.exe");
        Pending(client).Begin("first");
        Pending(client).Begin("second");
        var observed = new List<string>();
        bool protectedAfterFirst = false;
        client.ReplaySaved += (id, _) =>
        {
            observed.Add(id);
            if (id == "first")
                Deliver(client, "replay_saved", "{\"save_id\":\"first\",\"path\":\"duplicate.mkv\"}");
        };

        Deliver(client, "replay_saved", "{\"save_id\":\"first\",\"path\":\"first.mkv\"}");
        protectedAfterFirst = client.HasPendingReplaySaves;
        Deliver(client, "replay_saved", "{\"save_id\":\"second\",\"path\":\"second.mkv\"}");

        Assert.True(protectedAfterFirst);
        Assert.Equal(new[] { "first", "second" }, observed);
        Assert.False(client.HasPendingReplaySaves);
    }
}
