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
        var export = (Button)window.FindName("BtnExport");
        var thumbnail = (Image)window.FindName("ShareThumbnail");
        bool titleMatches = title.Text == vm.ShareClipTitle;
        bool detailsMatch = details.Text == vm.ShareClipDetails;
        bool stateMatches = state.Text == vm.ShareTileStateText;
        bool copyMatches = copy.IsEnabled == vm.ShareCanCopy;
        bool exportMatches = export.IsEnabled == vm.CanStartExport;
        bool thumbnailMatches = ReferenceEquals(thumbnail.Source, vm.SharePreview);
        bool readyValid = !requireReady || (vm.TryGetReadyShareFile(out _) && vm.ShareCanCopy &&
            vm.ShareTileStateText == "MP4 ready");
        bool bindingsMatch = titleMatches && detailsMatch && stateMatches && copyMatches && exportMatches && thumbnailMatches;
        object Binding(DependencyObject target, DependencyProperty property)
        {
            var expression = BindingOperations.GetBindingExpression(target, property);
            object? Read(object? source, string member)
            {
                for (Type? type = source?.GetType(); type != null; type = type.BaseType)
                {
                    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.DeclaredOnly;
                    if (type.GetProperty(member, flags) is { } descriptor) return descriptor.GetValue(source);
                    if (type.GetField(member, flags) is { } field) return field.GetValue(source);
                }
                return null;
            }
            var worker = Read(expression, "Worker");
            var pathWorker = Read(worker, "_pathWorker");
            return new { Path = expression?.ParentBinding.Path?.Path,
                Status = expression?.Status.ToString(), Mode = expression?.ParentBinding.Mode.ToString(),
                expression?.HasError, expression?.HasValidationError,
                ResolvedSourceType = expression?.ResolvedSource?.GetType().FullName,
                expression?.ResolvedSourcePropertyName,
                DataItemIsViewModel = ReferenceEquals(expression?.DataItem, vm),
                ResolvedSourceIsViewModel = ReferenceEquals(expression?.ResolvedSource, vm),
                DataContextIsViewModel = target is FrameworkElement element && ReferenceEquals(element.DataContext, vm),
                IsDynamic = Read(expression, "IsDynamic"),
                IsTransferPending = Read(expression, "IsTransferPending"),
                IsInUpdate = Read(expression, "IsInUpdate"),
                IgnoreSourcePropertyChange = Read(expression, "IgnoreSourcePropertyChange"),
                PathWorkerIsDynamic = Read(pathWorker, "_isDynamic"),
                WorkerType = worker?.GetType().FullName };
        }
        observations.Add(new {
            Label = label, RequiredMatch = requireMatch, RequiredReady = requireReady,
            ExpectedTitle = vm.ShareClipTitle, ActualTitle = title.Text, titleMatches,
            ExpectedDetails = vm.ShareClipDetails, ActualDetails = details.Text, detailsMatch,
            ExpectedState = vm.ShareTileStateText, ActualState = state.Text, stateMatches,
            ExpectedCopyEnabled = vm.ShareCanCopy, ActualCopyEnabled = copy.IsEnabled, copyMatches,
            ExpectedExportEnabled = vm.CanStartExport, ActualExportEnabled = export.IsEnabled, exportMatches,
            ExpectedHasThumbnail = vm.ShareHasPreview, ActualHasThumbnail = thumbnail.Source != null, thumbnailMatches,
            bindingsMatch, readyValid,
            TitleBinding = Binding(title, TextBlock.TextProperty), DetailsBinding = Binding(details, TextBlock.TextProperty),
            StateBinding = Binding(state, TextBlock.TextProperty), CopyBinding = Binding(copy, UIElement.IsEnabledProperty), ExportBinding = Binding(export, UIElement.IsEnabledProperty),
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
                $": title={titleMatches}, details={detailsMatch}, state={stateMatches}, copy={copyMatches}, export={exportMatches}, thumbnail={thumbnailMatches}, ready={readyValid}.");
    }
}
