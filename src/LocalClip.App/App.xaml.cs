using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace LocalClip.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "LocalClip_SingleInstance_UserLock";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("LocalClip is already running. Check your system tray.", "LocalClip", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Designer-only opt-in: normal users still start the same qualified main window.
        if (e.Args.Any(arg => arg.Equals("--design-preview", StringComparison.OrdinalIgnoreCase)))
            StartupUri = new Uri("Views/DesignSystemPreviewWindow.xaml", UriKind.Relative);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
