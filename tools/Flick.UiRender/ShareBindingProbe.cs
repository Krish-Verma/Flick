using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
        var status = (TextBlock)window.FindName("TxtShareExportStatus");
        var format = (TextBlock)window.FindName("ShareFormatHint");
        bool titleMatches = title.Text == vm.ShareClipTitle;
        bool detailsMatch = details.Text == vm.ShareClipDetails;
        bool stateMatches = state.Text == vm.ShareTileStateText;
        bool copyMatches = copy.IsEnabled == vm.ShareCanCopy;
        bool exportMatches = export.IsEnabled == vm.CanStartExport;
        bool thumbnailMatches = ReferenceEquals(thumbnail.Source, vm.SharePreview);
        bool summaryMatches = status.Text == vm.ShareStatusSummary;
        bool fullMessagePreserved = Equals(status.ToolTip, vm.ExportStatusMessage);
        bool formatMatches = format.Text == vm.ShareFormatHint;
        bool readyValid = !requireReady || (vm.TryGetReadyShareFile(out _) && vm.ShareCanCopy &&
            vm.ShareTileStateText == "MP4 ready");
        bool bindingsMatch = titleMatches && detailsMatch && stateMatches && copyMatches && exportMatches && thumbnailMatches &&
            summaryMatches && fullMessagePreserved && formatMatches;
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
            ExpectedStatusSummary = vm.ShareStatusSummary, ActualStatusSummary = status.Text, summaryMatches,
            FullMessage = vm.ExportStatusMessage, fullMessagePreserved,
            ExpectedFormatHint = vm.ShareFormatHint, ActualFormatHint = format.Text, formatMatches,
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

    private static (bool Fits, object Observation) InspectShareNoScrollLayout(MainWindow window)
    {
        T Find<T>(string name) => (T)window.FindName(name);
        var context = Find<Border>("EditorContextLayer");
        var share = Find<Border>("SharePanel");
        var footer = Find<WrapPanel>("ShareActionFooter");
        var root = (FrameworkElement)window.Content;
        var failures = new List<string>();
        var rows = new List<object>();
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bool Contains(Rect outer, Rect inner) => inner.Width > 0 && inner.Height > 0 &&
            inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1 && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;
        object Coordinates(Rect rect) => new { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };
        bool Visible(FrameworkElement element)
        {
            for (DependencyObject? node = element; node is Visual; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement { Visibility: not Visibility.Visible }) return false;
            return true;
        }
        var ancestors = new List<ScrollViewer>();
        for (DependencyObject? node = VisualTreeHelper.GetParent(share); node is Visual; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroller) ancestors.Add(scroller);
        var descendants = ProductVisuals(share).OfType<ScrollViewer>().Where(Visible).ToArray();
        bool NativeSingleLineEditor(ScrollViewer scroll)
        {
            // A TextBox requires its normal PART_ContentHost for caret and
            // selection editing. It is not the Share card's content scroller.
            // Identify only the actual Custom input's one-line template part;
            // its own scroll ranges must still be zero for these small values.
            if (scroll.Name != "PART_ContentHost") return false;
            for (DependencyObject? node = VisualTreeHelper.GetParent(scroll); node is Visual && node != share;
                 node = VisualTreeHelper.GetParent(node))
                if (node is TextBox text) return ReferenceEquals(text, Find<TextBox>("TxtCustomMb")) &&
                    !text.AcceptsReturn && text.TextWrapping == TextWrapping.NoWrap;
            return false;
        }
        var nativeEditors = descendants.Where(NativeSingleLineEditor).ToArray();
        var contentScrollers = descendants.Where(scroll => !NativeSingleLineEditor(scroll)).ToArray();
        var allScrollers = ancestors.Concat(descendants).ToArray();
        double scrollHeight = allScrollers.Select(scroll => scroll.ScrollableHeight).DefaultIfEmpty(0).Max();
        double scrollWidth = allScrollers.Select(scroll => scroll.ScrollableWidth).DefaultIfEmpty(0).Max();
        if (ancestors.Count != 0 || contentScrollers.Length != 0 || scrollHeight > .5 || scrollWidth > .5)
            failures.Add("Share content participates in scrolling.");
        if (Find<ScrollViewer>("EditorContextScroll").Visibility != Visibility.Collapsed)
            failures.Add("The separate Trim/Audio scroll host is not collapsed during Share.");
        if (!Contains(Bounds(Find<Grid>("ClipEditorPanel")), Bounds(context))) failures.Add("Share context exceeds the actual editor allocation.");
        void Check(string name, FrameworkElement container, bool required = true)
        {
            var element = Find<FrameworkElement>(name);
            bool visible = Visible(element);
            bool contained = visible && Contains(Bounds(container), Bounds(element));
            rows.Add(new { Name = name, Visible = visible, Required = required, Contained = contained,
                Bounds = Coordinates(Bounds(element)), Container = container.Name });
            if ((required && !visible) || (visible && !contained)) failures.Add(name + " is hidden or clipped.");
        }
        Check("SharePanel", context);
        Check("ShareActionFooter", context);
        foreach (string name in new[] { "CloseEditorTool", "ShareMoreButton" }) Check(name, context);
        foreach (string name in new[] { "DragHandle", "ShareExportOptions", "ShareFormatHint", "ShareStatusRow", "TxtShareExportStatus", "BtnShareDetails" }) Check(name, share);
        foreach (string name in new[] { "ShareTileTitle", "ShareTileDetails", "ShareTileHint", "ShareOriginalLabel", "TxtShareTileState", "ShareThumbnail" }) Check(name, Find<Border>("DragHandle"));
        foreach (string name in new[] { "CmbExportCap", "CmbShareAspect" }) Check(name, Find<FrameworkElement>("ShareExportOptions"));
        bool custom = Find<FrameworkElement>("CustomCapPanel").Visibility == Visibility.Visible;
        Check("CustomCapPanel", Find<FrameworkElement>("ShareExportOptions"), required: custom);
        Check("TxtCustomMb", Find<FrameworkElement>("CustomCapPanel"), required: custom);
        bool recent = ((MainViewModel)window.DataContext).RecentExports.Count > 0;
        Check("ShareRecentExportsRow", share, required: recent);
        Check("CmbRecentExports", Find<FrameworkElement>("ShareRecentExportsRow"), required: recent);
        Check("ShareExportProgress", share, required: ((MainViewModel)window.DataContext).IsExporting);
        foreach (string name in new[] { "BtnExport", "BtnShareCopyFile", "BtnShareOpenFolder", "BtnShareCancel" })
            Check(name, footer, required: name != "BtnShareCancel" || ((MainViewModel)window.DataContext).IsExporting);
        if (Bounds(share).Bottom > Bounds(footer).Top + 1) failures.Add("Share body overlaps its fixed action footer.");
        var textObservations = new List<object>();
        foreach (string name in new[] { "ShareTileTitle", "ShareTileDetails", "TxtShareTileState", "ShareTileHint", "ShareOriginalLabel", "ShareFormatHint", "TxtShareExportStatus" })
        {
            var text = Find<TextBlock>(name);
            if (!Visible(text)) continue;
            var formatted = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                text.Foreground ?? Brushes.White, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            if (!double.IsNaN(text.LineHeight)) formatted.LineHeight = text.LineHeight;
            if (text.TextWrapping != TextWrapping.NoWrap) formatted.MaxTextWidth = Math.Max(1, text.ActualWidth);
            bool titleEllipsis = name == "ShareTileTitle" && text.TextTrimming == TextTrimming.CharacterEllipsis &&
                Equals(text.ToolTip, ((MainViewModel)window.DataContext).ShareClipTitle);
            bool fits = text.ActualHeight + 1 >= formatted.Height && (titleEllipsis ||
                text.TextWrapping != TextWrapping.NoWrap || text.ActualWidth + 1 >= formatted.WidthIncludingTrailingWhitespace);
            textObservations.Add(new { Name = name, text.Text, text.FontSize, text.ActualWidth, text.ActualHeight,
                NaturalTextHeight = formatted.Height, NaturalTextWidth = formatted.WidthIncludingTrailingWhitespace,
                TitleEllipsisWithFullTooltip = titleEllipsis, Readable = fits });
            if (!fits) failures.Add(name + " text is clipped.");
        }
        foreach (string name in new[] { "CloseEditorTool", "ShareMoreButton", "BtnShareDetails", "BtnExport", "BtnShareCopyFile", "BtnShareOpenFolder", "BtnShareCancel" })
        {
            var button = Find<Button>(name);
            if (!Visible(button)) continue;
            var label = new FormattedText(Convert.ToString(button.Content) ?? "", CultureInfo.CurrentUICulture, button.FlowDirection,
                new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize,
                button.Foreground ?? Brushes.White, VisualTreeHelper.GetDpi(button).PixelsPerDip);
            if (button.ActualWidth + 1 < label.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right ||
                button.ActualHeight + 1 < label.Height + button.Padding.Top + button.Padding.Bottom)
                failures.Add(name + " action label is clipped.");
        }
        return (failures.Count == 0, new { controlsFit = failures.Count == 0, NoScrollViewerAncestor = ancestors.Count == 0,
            ScrollableHeight = scrollHeight, ScrollableWidth = scrollWidth,
            NativeSingleLineEditingHosts = nativeEditors.Select(scroll => new { scroll.Name, scroll.ScrollableHeight, scroll.ScrollableWidth }),
            ShareContentScrollers = contentScrollers.Select(scroll => scroll.Name),
            ContextBounds = Coordinates(Bounds(context)), BodyBounds = Coordinates(Bounds(share)), FooterBounds = Coordinates(Bounds(footer)),
            Rows = rows, Text = textObservations, Failures = failures });
    }
}
