using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LocalClip.App;
using LocalClip.App.ViewModels;

namespace Flick.UiRender;

internal static partial class Program
{
    // Observe the production bindings after ordinary dispatcher/layout work.
    // Never UpdateTarget, replace DataContext, assign bound values, or recreate
    // a control to make stale UI look correct in a rendering fixture.
    private static void ObserveShareBindings(MainWindow window, MainViewModel vm, string label,
        List<object> observations, IReadOnlyList<object> notifications, string output,
        bool requireMatch, bool requireReady = false)
    {
        var share = (Border)window.FindName("SharePanel");
        var title = ProductVisuals(share).OfType<TextBlock>().Single(control =>
            BindingOperations.GetBinding(control, TextBlock.TextProperty)?.Path?.Path == nameof(vm.ShareClipTitle));
        var details = ProductVisuals(share).OfType<TextBlock>().Single(control =>
            BindingOperations.GetBinding(control, TextBlock.TextProperty)?.Path?.Path == nameof(vm.ShareClipDetails));
        var state = (TextBlock)window.FindName("TxtShareTileState");
        var copy = (Button)window.FindName("BtnShareCopyFile");
        var thumbnail = (Image)window.FindName("ShareThumbnail");
        bool titleMatches = title.Text == vm.ShareClipTitle;
        bool detailsMatch = details.Text == vm.ShareClipDetails;
        bool stateMatches = state.Text == vm.ShareTileStateText;
        bool copyMatches = copy.IsEnabled == vm.ShareCanCopy;
        bool thumbnailMatches = ReferenceEquals(thumbnail.Source, vm.SharePreview);
        bool readyValid = !requireReady || (vm.TryGetReadyShareFile(out _) && vm.ShareCanCopy &&
            vm.ShareTileStateText == "MP4 ready");
        bool bindingsMatch = titleMatches && detailsMatch && stateMatches && copyMatches && thumbnailMatches;
        object Binding(DependencyObject target, DependencyProperty property)
        {
            var expression = BindingOperations.GetBindingExpression(target, property);
            return new { Path = expression?.ParentBinding.Path?.Path,
                Status = expression?.Status.ToString(), Mode = expression?.ParentBinding.Mode.ToString(),
                expression?.HasError, expression?.HasValidationError,
                ResolvedSourceType = expression?.ResolvedSource?.GetType().FullName,
                expression?.ResolvedSourcePropertyName,
                DataItemIsViewModel = ReferenceEquals(expression?.DataItem, vm),
                ResolvedSourceIsViewModel = ReferenceEquals(expression?.ResolvedSource, vm),
                DataContextIsViewModel = target is FrameworkElement element && ReferenceEquals(element.DataContext, vm) };
        }
        observations.Add(new {
            Label = label, RequiredMatch = requireMatch, RequiredReady = requireReady,
            ExpectedTitle = vm.ShareClipTitle, ActualTitle = title.Text, titleMatches,
            ExpectedDetails = vm.ShareClipDetails, ActualDetails = details.Text, detailsMatch,
            ExpectedState = vm.ShareTileStateText, ActualState = state.Text, stateMatches,
            ExpectedCopyEnabled = vm.ShareCanCopy, ActualCopyEnabled = copy.IsEnabled, copyMatches,
            ExpectedHasThumbnail = vm.ShareHasPreview, ActualHasThumbnail = thumbnail.Source != null, thumbnailMatches,
            bindingsMatch, readyValid,
            TitleBinding = Binding(title, TextBlock.TextProperty), DetailsBinding = Binding(details, TextBlock.TextProperty),
            StateBinding = Binding(state, TextBlock.TextProperty), CopyBinding = Binding(copy, UIElement.IsEnabledProperty),
            ThumbnailBinding = Binding(thumbnail, Image.SourceProperty),
            SharePresentationActive = typeof(MainViewModel).GetField("_sharePresentationActive", Private)!.GetValue(vm),
            ShareDisposed = typeof(MainViewModel).GetField("_shareDisposed", Private)!.GetValue(vm),
            CurrentThreadIsDispatcher = window.Dispatcher.CheckAccess()
        });
        File.WriteAllText(Path.Combine(output, "share-binding-observations.json"), JsonSerializer.Serialize(new {
            Method = "Production BindingExpression read-only observations; ordinary layout/dispatcher drains only; no UpdateTarget, rebinding or local property assignment",
            Observations = observations, Notifications = notifications
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (requireMatch && (!bindingsMatch || !readyValid))
            throw new InvalidOperationException("Share UI disagrees with its real view model in " + label +
                $": title={titleMatches}, details={detailsMatch}, state={stateMatches}, copy={copyMatches}, thumbnail={thumbnailMatches}, ready={readyValid}.");
    }
}
