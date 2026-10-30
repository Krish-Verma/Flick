using System.Windows;
using Image = System.Windows.Controls.Image;

namespace LocalClip.App.Services;

/// <summary>Lifecycle-bound image loading: recycled/hidden images cannot receive an old clip's pixels.</summary>
public static class AsyncThumbnail
{
    private static readonly BoundedThumbnailLoader Loader = new();
    private static int _visibleConsumers;
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(AsyncThumbnail), new PropertyMetadata("", PathChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(RequestState), typeof(AsyncThumbnail), new PropertyMetadata(null));
    private sealed class RequestState
    {
        public int Generation;
        public CancellationTokenSource? Cancellation;
        public bool VisibleConsumer;
    }

    public static void SetPath(DependencyObject element, string value) => element.SetValue(PathProperty, value);
    public static string GetPath(DependencyObject element) => (string)element.GetValue(PathProperty);

    private static void PathChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Image image) return;
        if (image.GetValue(StateProperty) is not RequestState)
        {
            image.SetValue(StateProperty, new RequestState());
            image.Loaded += Loaded;
            image.Unloaded += Unloaded;
            image.IsVisibleChanged += VisibilityChanged;
        }
        Cancel(image);
        if (image.IsLoaded && image.IsVisible) _ = LoadAsync(image, false);
    }

    private static void Loaded(object sender, RoutedEventArgs args)
    { if (sender is Image { IsVisible: true } image) _ = LoadAsync(image, false); }
    private static void Unloaded(object sender, RoutedEventArgs args)
    { if (sender is Image image) Cancel(image); }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Image image) return;
        if (image.IsVisible && image.IsLoaded) _ = LoadAsync(image, false);
        else Cancel(image);
    }

    // The render harness deliberately never shows MainWindow, because Loaded discovers sources.
    // Explicit fixture decoding is safe: it loads the same bounded local still path, no media worker.
    public static Task LoadForSnapshotAsync(Image image) => LoadAsync(image, true);

    private static void Cancel(Image image)
    {
        if (image.GetValue(StateProperty) is not RequestState state) return;
        state.Generation++;
        state.Cancellation?.Cancel();
        state.Cancellation = null;
        image.Source = null;
        if (state.VisibleConsumer)
        {
            state.VisibleConsumer = false;
            if (Interlocked.Decrement(ref _visibleConsumers) == 0) Loader.ClearCache();
        }
    }

    private static async Task LoadAsync(Image image, bool snapshot)
    {
        if (image.GetValue(StateProperty) is not RequestState state) return;
        Cancel(image);
        string path = GetPath(image);
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!snapshot && (!image.IsLoaded || !image.IsVisible)) return;
        if (!snapshot)
        {
            state.VisibleConsumer = true;
            Interlocked.Increment(ref _visibleConsumers);
        }
        int generation = state.Generation;
        using var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;
        try
        {
            var result = await Loader.LoadAsync(path, cancellation.Token).ConfigureAwait(false);
            await OnDispatcherAsync(image, () =>
            {
                if (!cancellation.IsCancellationRequested && state.Generation == generation &&
                    GetPath(image) == path && (snapshot || image.IsLoaded && image.IsVisible))
                    image.Source = result;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await OnDispatcherAsync(image, () =>
            {
                if (ReferenceEquals(state.Cancellation, cancellation)) state.Cancellation = null;
                // Snapshot rendering does not keep the shared idle cache alive.
                if (snapshot && Volatile.Read(ref _visibleConsumers) == 0) Loader.ClearCache();
            }).ConfigureAwait(false);
        }
    }

    private static Task OnDispatcherAsync(Image image, Action action)
    {
        var dispatcher = image.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return Task.CompletedTask;
        if (dispatcher.CheckAccess()) { action(); return Task.CompletedTask; }
        return dispatcher.InvokeAsync(action).Task;
    }
}
