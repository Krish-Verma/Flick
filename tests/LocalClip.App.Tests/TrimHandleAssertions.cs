using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LocalClip.App.Views.Components;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.App.Tests;

// Runs in the suite's existing STA/Application. No source media, decoder,
// playback surface, device, profile or external process is opened.
internal static class TrimHandleAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        EndpointsAndShortRangesRemainReachable();
        KeyboardAndDragRemainBoundedAndCommitOnce();
        PreciseTimesSurviveFocusAndSingleFieldEdits();
    }

    private static void EndpointsAndShortRangesRemainReachable()
    {
        foreach (double width in new[] { 300d, 420d, 720d })
        foreach (double scale in new[] { 1d, 1.25d, 1.5d, 2d })
        {
            var timeline = new TrimRangeControl { Width = width, LayoutTransform = new ScaleTransform(scale, scale) };
            timeline.SetRange(3600, 0, 3600);
            timeline.Measure(new Size(width * scale, 200 * scale));
            timeline.Arrange(new Rect(0, 0, width * scale, 200 * scale));
            timeline.UpdateLayout();
            var canvas = Find<Canvas>(timeline, "RangeCanvas");
            var start = Find<Thumb>(timeline, "StartHandle");
            var end = Find<Thumb>(timeline, "EndHandle");
            Assert.InRange(start.ActualWidth, 24, 32);
            Assert.InRange(start.ActualHeight, 28, 36);
            Assert.Equal(start.ActualWidth, end.ActualWidth);
            Assert.True(start.Focusable && end.Focusable);
            Assert.True(start.IsTabStop && end.IsTabStop);
            Assert.Same(Cursors.SizeWE, start.Cursor);
            Assert.Equal(0, Canvas.GetLeft(start), 6);
            Assert.Equal(canvas.ActualWidth, Canvas.GetLeft(end) + end.ActualWidth, 6);
            double origin = Canvas.GetLeft(start) + start.ActualWidth;
            double finish = Canvas.GetLeft(end);
            Assert.Equal(0, (double)Invoke(timeline, "SecondsAt", origin)!, 6);
            Assert.Equal(3600, (double)Invoke(timeline, "SecondsAt", finish)!, 6);
            Assert.Equal(1800, (double)Invoke(timeline, "SecondsAt", (origin + finish) / 2)!, 6);
            Assert.Equal(0, (double)Invoke(timeline, "SecondsAt", -100)!, 6);
            Assert.Equal(3600, (double)Invoke(timeline, "SecondsAt", canvas.ActualWidth + 100)!, 6);
            AssertGripAndReservedFocus(start);
            AssertGripAndReservedFocus(end);
            AssertHit(canvas, start);
            AssertHit(canvas, end);

            // The minimum temporal range occupies much less than one DIP on
            // a one-hour clip. Its two full targets must still never overlap.
            timeline.SetRange(3600, 1800, 1800 + TrimTimelineMath.MinSelectionSeconds);
            timeline.UpdateLayout();
            Assert.True(Canvas.GetLeft(start) + start.ActualWidth <= Canvas.GetLeft(end));
            AssertHit(canvas, start);
            AssertHit(canvas, end);
            timeline.SetRange(double.NaN, 0, 0);
            Assert.False(timeline.IsEnabled);
            Assert.Same(Cursors.Arrow, start.Cursor);
            Assert.Same(Cursors.Arrow, end.Cursor);
        }
    }

    private static void KeyboardAndDragRemainBoundedAndCommitOnce()
    {
        var timeline = new TrimRangeControl { Width = 420 };
        timeline.SetRange(10, 1, 8);
        timeline.Measure(new Size(420, 160));
        timeline.Arrange(new Rect(0, 0, 420, 160));
        timeline.UpdateLayout();
        int commits = 0;
        timeline.RangeCommitted += (_, _) => commits++;
        Assert.True((bool)Invoke(timeline, "MoveHandle", true, Key.Right, false)!);
        Assert.True((bool)Invoke(timeline, "MoveHandle", true, Key.Right, false)!);
        Assert.Equal(1.2, timeline.SelectionStart, 8);
        Assert.Equal(0, commits); // Held-key repeat has not completed.
        Invoke(timeline, "CommitKeyboardRange");
        Assert.Equal(1, commits);
        Invoke(timeline, "CommitKeyboardRange"); // Focus loss after key-up is inert.
        Assert.Equal(1, commits);
        Invoke(timeline, "MoveHandle", false, Key.Left, true);
        Assert.Equal(7, timeline.SelectionEnd, 8);
        Invoke(timeline, "CommitKeyboardRange");
        Assert.Equal(2, commits);
        Invoke(timeline, "MoveHandle", true, Key.End, false);
        Assert.Equal(timeline.SelectionEnd - TrimTimelineMath.MinSelectionSeconds, timeline.SelectionStart, 8);
        Invoke(timeline, "MoveHandle", false, Key.Home, false);
        Assert.Equal(TrimTimelineMath.MinSelectionSeconds, timeline.SelectionEnd - timeline.SelectionStart, 8);
        Assert.False((bool)Invoke(timeline, "MoveHandle", true, Key.A, false)!);
        Invoke(timeline, "CommitKeyboardRange");
        timeline.SetRange(10, 1, 8);
        var start = Find<Thumb>(timeline, "StartHandle");
        var end = Find<Thumb>(timeline, "EndHandle");
        Invoke(timeline, "StartHandle_DragDelta", start, new DragDeltaEventArgs(10000, 0));
        Assert.Equal(7.75, timeline.SelectionStart, 8);
        Invoke(timeline, "EndHandle_DragDelta", end, new DragDeltaEventArgs(-10000, 0));
        Assert.Equal(8, timeline.SelectionEnd, 8);
        Invoke(timeline, "StartHandle_DragDelta", start, new DragDeltaEventArgs(-10000, 0));
        Assert.Equal(0, timeline.SelectionStart, 8);
        Invoke(timeline, "EndHandle_DragDelta", end, new DragDeltaEventArgs(10000, 0));
        Assert.Equal(10, timeline.SelectionEnd, 8);
        Assert.True(TrimValidator.Validate(timeline.SelectionStart, timeline.SelectionEnd, timeline.OriginalDuration).IsValid);
    }

    private static void PreciseTimesSurviveFocusAndSingleFieldEdits()
    {
        var timeline = new TrimRangeControl();
        const double exactIn = 1.23456789, exactOut = 8.98765432;
        timeline.SetRange(10, exactIn, exactOut);
        int commits = 0;
        timeline.RangeCommitted += (_, _) => commits++;
        Invoke(timeline, "CommitTimeBoxes"); // Merely entering/leaving a rounded display field.
        Assert.Equal(exactIn, timeline.SelectionStart);
        Assert.Equal(exactOut, timeline.SelectionEnd);
        Assert.Equal(0, commits);
        Find<TextBox>(timeline, "EndTimeBox").Text = "9.125";
        Invoke(timeline, "CommitTimeBoxes");
        Assert.Equal(exactIn, timeline.SelectionStart);
        Assert.Equal(9.125, timeline.SelectionEnd);
        Assert.Equal(1, commits);
        Invoke(timeline, "CommitTimeBoxes");
        Assert.Equal(1, commits);
        Find<TextBox>(timeline, "StartTimeBox").Text = "9.5";
        Invoke(timeline, "CommitTimeBoxes"); // Reject crossed input without rounding either value.
        Assert.Equal(exactIn, timeline.SelectionStart);
        Assert.Equal(9.125, timeline.SelectionEnd);
        Assert.Equal(1, commits);
        Invoke(timeline, "ResetTrim_Click", timeline, new RoutedEventArgs());
        Assert.Equal(0, timeline.SelectionStart);
        Assert.Equal(10, timeline.SelectionEnd);
        Assert.Equal(2, commits);
    }

    private static void AssertGripAndReservedFocus(Thumb thumb)
    {
        thumb.ApplyTemplate();
        var grip = Assert.IsType<Border>(thumb.Template.FindName("Grip", thumb));
        Assert.InRange(grip.ActualWidth, 8, 12);
        var focus = Assert.IsType<Border>(thumb.Template.FindName("FocusFrame", thumb));
        Assert.Equal(new Thickness(2), focus.BorderThickness);
    }

    private static void AssertHit(Canvas canvas, Thumb thumb)
    {
        var point = new Point(Canvas.GetLeft(thumb) + thumb.ActualWidth / 2, Canvas.GetTop(thumb) + thumb.ActualHeight / 2);
        DependencyObject? hit = VisualTreeHelper.HitTest(canvas, point)?.VisualHit;
        while (hit != null && hit is not Thumb) hit = VisualTreeHelper.GetParent(hit);
        Assert.Same(thumb, hit);
    }

    private static T Find<T>(TrimRangeControl timeline, string name) => Assert.IsType<T>(timeline.FindName(name));
    private static object? Invoke(TrimRangeControl timeline, string method, params object[] args) => typeof(TrimRangeControl)
        .GetMethod(method, Hidden)!.Invoke(timeline, args);
}
