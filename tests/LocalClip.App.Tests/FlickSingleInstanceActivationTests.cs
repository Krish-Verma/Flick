using System.Collections.Concurrent;
using System.Threading;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

public sealed class FlickSingleInstanceActivationTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("--startup", false)]
    [InlineData("--STARTUP", false)]
    [InlineData("--minimized", false)]
    [InlineData("--design-preview", false)]
    public void OnlyDeliberateForegroundLaunchesActivateAnExistingWindow(string argument, bool expected) =>
        Assert.Equal(expected, FlickSingleInstanceActivation.ShouldActivateExisting(
            argument.Length == 0 ? Array.Empty<string>() : new[] { argument }));

    [Fact]
    public void BackgroundMarkerAnywhereInTheLaunchArgumentsSuppressesFocus()
    {
        Assert.False(FlickSingleInstanceActivation.ShouldActivateExisting(new[] { "--other", "--startup", "--minimized" }));
        Assert.False(FlickSingleInstanceActivation.ShouldActivateExisting(new[] { "--DESIGN-PREVIEW", "--other" }));
    }

    [Fact]
    public void EarlySecondLaunchSignalIsRetainedAndDispatchedOnceToTheOwningUi()
    {
        string name = TestChannel();
        using var first = new FlickSingleInstanceActivation(name);
        using var second = new FlickSingleInstanceActivation(name);
        using var queued = new ManualResetEventSlim();
        var actions = new ConcurrentQueue<Action>();
        int activations = 0;
        // The second process can signal while the first is still creating its
        // main window. AutoReset retains that request without a polling timer.
        Assert.True(second.RequestActivation());
        first.Listen(action => { actions.Enqueue(action); queued.Set(); return true; },
            () => Interlocked.Increment(ref activations));
        Assert.True(queued.Wait(TimeSpan.FromSeconds(5)), "The owner did not receive its isolated activation signal.");
        Assert.Equal(0, Volatile.Read(ref activations)); // Queue seam respects UI-thread ownership.
        Assert.True(actions.TryDequeue(out var activate));
        activate();
        Assert.Equal(1, Volatile.Read(ref activations));
        Assert.Empty(actions);
    }

    [Fact]
    public void DisposalCancelsAlreadyQueuedWindowActivationAndFurtherRequests()
    {
        string name = TestChannel();
        using var first = new FlickSingleInstanceActivation(name);
        using var second = new FlickSingleInstanceActivation(name);
        using var queued = new ManualResetEventSlim();
        var actions = new ConcurrentQueue<Action>();
        int activations = 0;
        first.Listen(action => { actions.Enqueue(action); queued.Set(); return true; },
            () => Interlocked.Increment(ref activations));
        Assert.True(second.RequestActivation());
        Assert.True(queued.Wait(TimeSpan.FromSeconds(5)));
        first.Dispose();
        Assert.True(actions.TryDequeue(out var activate));
        activate();
        Assert.Equal(0, Volatile.Read(ref activations));
        Assert.False(first.RequestActivation());
        Assert.Throws<ObjectDisposedException>(() => first.Listen(_ => true, () => { }));
    }

    [Fact]
    public void DifferentNamedChannelDoesNotDispatchAnActivation()
    {
        // Both names are GUID-scoped fixtures. Never open the real Flick channel
        // or enumerate/activate any owner windows or processes.
        using var first = new FlickSingleInstanceActivation(TestChannel());
        using var unrelated = new FlickSingleInstanceActivation(TestChannel());
        using var queued = new ManualResetEventSlim();
        first.Listen(_ => { queued.Set(); return true; }, () => throw new Exception("Unrelated channel activated."));
        Assert.True(unrelated.RequestActivation());
        Assert.False(queued.Wait(TimeSpan.FromMilliseconds(150)));
    }

    private static string TestChannel() => "Flick_Activation_Test_" + Guid.NewGuid().ToString("N");
}
