using System;
using System.Linq;
using System.Threading;
using System.Windows;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "LocalClip_SingleInstance_UserLock";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            // The existing instance remains available in its tray; do not
            // create a second application-owned bootstrap dialog.
            Shutdown();
            return;
        }

        // Designer-only opt-in: normal users still start the same qualified main window.
        if (e.Args.Any(arg => arg.Equals("--design-preview", StringComparison.OrdinalIgnoreCase)))
            StartupUri = new Uri("Views/DesignSystemPreviewWindow.xaml", UriKind.Relative);

        _visualPreferences = new FlickVisualPreferences(this);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _visualPreferences?.Dispose();
        _visualPreferences = null;
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _ownsSingleInstanceMutex = false;
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
