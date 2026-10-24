using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace LocalClip.App.Tests;

public sealed class WpfDispatcherBindingTests
{
    [Fact]
    public void WorkerThenUiNotificationsSettleReadonlyAndWritableBindingsThroughContextIdle()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                // No Application, Window, MainViewModel, profile or native host.
                var source = new BindingProbe();
                var writable = new TextBlock();
                var computed = new TextBlock();
                writable.SetBinding(TextBlock.TextProperty, new Binding(nameof(BindingProbe.Text)) { Source = source });
                computed.SetBinding(TextBlock.TextProperty, new Binding(nameof(BindingProbe.ComputedText)) { Source = source });
                DrainToIdle(dispatcher);
                Assert.Equal("initial", writable.Text);
                Assert.Equal("computed:initial", computed.Text);
                Assert.Equal(BindingStatus.Active, computed.GetBindingExpression(TextBlock.TextProperty)!.Status);
                int interfaceCallbacks = 0, weakCallbacks = 0;
                bool observedWorker = false;
                PropertyChangedEventHandler direct = (_, args) =>
                {
                    if (args.PropertyName != nameof(BindingProbe.ComputedText)) return;
                    Interlocked.Increment(ref interfaceCallbacks);
                    if (!dispatcher.CheckAccess()) observedWorker = true;
                };
                EventHandler<PropertyChangedEventArgs> weak = (_, _) => Interlocked.Increment(ref weakCallbacks);
                ((INotifyPropertyChanged)source).PropertyChanged += direct;
                PropertyChangedEventManager.AddHandler(source, weak, nameof(BindingProbe.ComputedText));
                try
                {
                    Assert.True(Task.Run(() => source.Text = "worker").Wait(TimeSpan.FromSeconds(5)),
                        "The synthetic worker notification did not complete.");
                    source.Text = "latest-ui";
                    Assert.True(observedWorker);
                    Assert.Equal(2, interfaceCallbacks);
                    Assert.Equal(2, weakCallbacks);
                    // Reading/laying out the target must not replace/rebind it.
                    // Pump the real queue where WPF schedules worker transfers;
                    // it must read the newest source rather than the old event.
                    DrainToIdle(dispatcher);
                    Assert.Equal("latest-ui", writable.Text);
                    Assert.Equal("computed:latest-ui", computed.Text);
                    source.Text = "after-idle";
                    DrainToIdle(dispatcher);
                    Assert.Equal("after-idle", writable.Text);
                    Assert.Equal("computed:after-idle", computed.Text);
                }
                finally
                {
                    ((INotifyPropertyChanged)source).PropertyChanged -= direct;
                    PropertyChangedEventManager.RemoveHandler(source, weak, nameof(BindingProbe.ComputedText));
                }
            }
            catch (Exception error) { failure = error; }
            finally { dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The isolated WPF binding regression timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void DrainToIdle(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        bool idleReached = false;
        var idle = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            idleReached = true;
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
        Assert.True(idleReached, "The isolated dispatcher did not reach ContextIdle.");
    }

    private sealed class BindingProbe : INotifyPropertyChanged
    {
        private string _text = "initial";
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                PropertyChanged?.Invoke(this, new(nameof(Text)));
                PropertyChanged?.Invoke(this, new(nameof(ComputedText)));
            }
        }
        public string ComputedText => "computed:" + _text;
    }
}
