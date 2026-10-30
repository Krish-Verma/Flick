using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LocalClip.App.Services;

namespace LocalClip.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsSingleInstanceMutex;
    private FlickVisualPreferences? _visualPreferences;
    private FlickSingleInstanceActivation? _singleInstanceActivation;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool designPreview = e.Args.Any(arg => arg.Equals("--design-preview", StringComparison.OrdinalIgnoreCase));
        if (!designPreview)
        {
            try { _singleInstanceActivation = new FlickSingleInstanceActivation(); }
            catch (Exception) { /* Optional activation must not prevent the first instance from opening. */ }
        }
        const string mutexName = "LocalClip_SingleInstance_UserLock";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            // A deliberate second launch shows the existing tray instance.
            // Logon/minimized launches and design fixtures never steal focus.
            if (FlickSingleInstanceActivation.ShouldActivateExisting(e.Args))
                _singleInstanceActivation?.RequestActivation();
            _singleInstanceActivation?.Dispose();
            _singleInstanceActivation = null;
            Shutdown();
            return;
        }

        // Designer-only opt-in: normal users still start the same qualified main window.
        if (designPreview)
            StartupUri = new Uri("Views/DesignSystemPreviewWindow.xaml", UriKind.Relative);

        if (_singleInstanceActivation != null)
        {
            // The event already exists, retaining an early second-launch signal.
            // Register after the default StartupUri has created the real window.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted || _singleInstanceActivation == null) return;
                try
                {
                    _singleInstanceActivation.Listen(action =>
                    {
                        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return false;
                        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action);
                        return true;
                    }, () =>
                    {
                        if (MainWindow is LocalClip.App.MainWindow main && !Dispatcher.HasShutdownStarted)
                            main.ActivateFromSecondInstance();
                    });
                }
                catch (Exception)
                {
                    _singleInstanceActivation.Dispose();
                    _singleInstanceActivation = null;
                }
            }));
        }

        _visualPreferences = new FlickVisualPreferences(this);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceActivation?.Dispose();
        _singleInstanceActivation = null;
        _visualPreferences?.Dispose();
        _visualPreferences = null;
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _ownsSingleInstanceMutex = false;
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
