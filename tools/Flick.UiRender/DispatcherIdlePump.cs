using System.Windows.Threading;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void DrainDispatcherThroughContextIdle(Dispatcher dispatcher)
    {
        dispatcher.VerifyAccess();
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            throw new InvalidOperationException("Cannot settle a shutting-down WPF dispatcher.");
        var frame = new DispatcherFrame();
        bool reachedIdle = false;
        // WPF DataBindEngine marshals cross-thread PropertyChanged at
        // ContextIdle. A DataBind/Render/Background-only pump starves it and
        // leaves IsTransferPending set, suppressing even later UI updates.
        var idle = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            reachedIdle = true;
            frame.Continue = false;
        }));
        var guard = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(3) };
        guard.Tick += (_, _) => frame.Continue = false;
        guard.Start();
        try { Dispatcher.PushFrame(frame); }
        finally
        {
            guard.Stop();
            if (idle.Status == DispatcherOperationStatus.Pending) idle.Abort();
        }
        if (!reachedIdle) throw new TimeoutException("WPF did not reach ContextIdle within the fixture deadline.");
    }
}
