using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LocalClip.Core.Rules;

namespace LocalClip.App.Views.Components;

public sealed class TrimRangeCommittedEventArgs(double start, double end) : EventArgs
{
    public double Start { get; } = start;
    public double End { get; } = end;
}
public sealed class TrimSeekEventArgs(double seconds) : EventArgs { public double Seconds { get; } = seconds; }

/// <summary>
/// Render two accessible handles over one horizontal trim selection, persisting
/// only on drag completion / keyboard / validated precise time entry.
/// This control never touches source media or launches a worker.
/// </summary>
public partial class TrimRangeControl : UserControl
{
    private double _duration;
    private double _start;
    private double _end;
    private double _committedStart;
    private double _committedEnd;
    private bool _keyboardRangeDirty;
    private bool _refreshing;
    private bool _startTimeEdited;
    private bool _endTimeEdited;
    private double _playhead;

    public event EventHandler<TrimRangeCommittedEventArgs>? RangeCommitted;
    public event EventHandler? RangeChanging;
    public event EventHandler? RangeReverted;
    public event EventHandler<TrimSeekEventArgs>? SeekRequested;

    public double SelectionStart => _start;
    public double SelectionEnd => _end;
    public double OriginalDuration => _duration;

    public TrimRangeControl()
    {
        InitializeComponent();
        SetRange(0, 0, 0);
    }

    public void SetRange(double duration, double start, double end)
    {
        _keyboardRangeDirty = false;
        if (!TrimTimelineMath.TryCreate(duration, start, end, out _start, out _end))
        {
            _duration = _start = _end = 0;
            _committedStart = _committedEnd = 0;
            IsEnabled = false;
        }
        else
        {
            _duration = duration;
            _committedStart = _start;
            _committedEnd = _end;
            IsEnabled = true;
        }
        RefreshControls();
    }

    private void RangeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrack();

    // In's target sits before its boundary; Out's sits after it. Reserving
    // both targets keeps edge timestamps and very short ranges reachable.
    private double HandleTargetWidth => Math.Max(StartHandle.Width, EndHandle.Width);
    private double TravelWidth => Math.Max(1, RangeCanvas.ActualWidth - 2 * HandleTargetWidth);
    private double Position(double seconds) => _duration <= 0 ? 0 :
        Math.Clamp(seconds / _duration, 0, 1) * TravelWidth;
    private double SecondsAt(double canvasX) => _duration <= 0 ? 0 :
        Math.Clamp((canvasX - HandleTargetWidth) / TravelWidth, 0, 1) * _duration;
    public void SetPlayhead(double seconds) { _playhead = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, _duration) : 0; UpdateTrack(); }

    private void UpdateTrack()
    {
        if (TrackBackground is null || StartHandle is null || EndHandle is null) return;
        double from = Position(_start);
        double to = Position(_end);
        TrackBackground.Width = TravelWidth;
        Canvas.SetLeft(TrackBackground, HandleTargetWidth);
        Canvas.SetLeft(SelectedTrack, from + HandleTargetWidth);
        SelectedTrack.Width = Math.Max(0, to - from);
        Canvas.SetLeft(StartHandle, from);
        Canvas.SetTop(StartHandle, 1);
        Canvas.SetLeft(EndHandle, to + HandleTargetWidth);
        Canvas.SetTop(EndHandle, 1);
        Canvas.SetLeft(PlayheadLine, Position(_playhead) + HandleTargetWidth - PlayheadLine.Width / 2);
    }

    private void RefreshControls()
    {
        if (StartTimeBox is null) return;
        _refreshing = true;
        StartTimeBox.Text = TrimTimelineMath.Format(_start);
        EndTimeBox.Text = TrimTimelineMath.Format(_end);
        _refreshing = false;
        _startTimeEdited = _endTimeEdited = false;
        SelectedDurationText.Text = _duration > 0
            ? "Selected: " + TrimTimelineMath.Format(_end - _start)
            : "No clip selected";
        UpdateTrack();
    }

    private void StartHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_duration <= 0) return;
        _start = TrimTimelineMath.MoveStart(
            _start + e.HorizontalChange * _duration / TravelWidth, _end, _duration);
        RefreshControls();
        RangeChanging?.Invoke(this, EventArgs.Empty);
    }

    private void EndHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_duration <= 0) return;
        _end = TrimTimelineMath.MoveEnd(
            _end + e.HorizontalChange * _duration / TravelWidth, _start, _duration);
        RefreshControls();
        RangeChanging?.Invoke(this, EventArgs.Empty);
    }

    private void Handle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!e.Canceled) { Commit(); return; }
        _start = _committedStart;
        _end = _committedEnd;
        RefreshControls();
        RangeReverted?.Invoke(this, EventArgs.Empty);
    }

    private void Handle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        ((Thumb)sender).Focus();

    private void Handle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MoveHandle(ReferenceEquals(sender, StartHandle), e.Key,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) e.Handled = true;
    }

    private bool MoveHandle(bool startHandle, Key key, bool coarse)
    {
        if (_duration <= 0) return false;
        double step = coarse ? TrimTimelineMath.KeyboardCoarseStep : TrimTimelineMath.KeyboardFineStep;
        double current = startHandle ? _start : _end;
        double desired = key switch
        {
            Key.Left => current - step,
            Key.Right => current + step,
            Key.Home => 0,
            Key.End => _duration,
            _ => double.NaN
        };
        if (!double.IsFinite(desired)) return false;
        if (startHandle)
            _start = TrimTimelineMath.MoveStart(desired, _end, _duration);
        else
            _end = TrimTimelineMath.MoveEnd(desired, _start, _duration);
        RefreshControls();
        _keyboardRangeDirty = true;
        RangeChanging?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Handle_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_keyboardRangeDirty && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            CommitKeyboardRange();
            e.Handled = true;
        }
    }

    private void Handle_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitKeyboardRange();

    private void CommitKeyboardRange()
    {
        if (!_keyboardRangeDirty) return;
        _keyboardRangeDirty = false;
        Commit(); // One completed held-key action, rather than each repeat.
    }

    private void TimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTimeBoxes();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RefreshControls();
            RangeReverted?.Invoke(this, EventArgs.Empty);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TimeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => CommitTimeBoxes();
    private void TimeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_refreshing || _duration <= 0) return;
        if (ReferenceEquals(sender, StartTimeBox)) _startTimeEdited = true;
        if (ReferenceEquals(sender, EndTimeBox)) _endTimeEdited = true;
        RangeChanging?.Invoke(this, EventArgs.Empty);
    }

    private void Track_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_duration <= 0) return;
        DependencyObject? source = e.OriginalSource as DependencyObject;
        while (source != null && !ReferenceEquals(source, RangeCanvas))
        {
            if (source is Thumb) return;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        double seconds = SecondsAt(e.GetPosition(RangeCanvas).X);
        SetPlayhead(seconds); SeekRequested?.Invoke(this, new(seconds)); e.Handled = true;
    }

    private void CommitTimeBoxes()
    {
        if (_duration <= 0 || (!_startTimeEdited && !_endTimeEdited)) return;
        // The display rounds to hundredths. Reparse only fields the user edited,
        // so focus changes and editing Out preserve an exact In timestamp.
        double s = _start, t = _end;
        if ((_startTimeEdited && !TrimTimelineMath.TryParseTime(StartTimeBox.Text, out s)) ||
            (_endTimeEdited && !TrimTimelineMath.TryParseTime(EndTimeBox.Text, out t)) ||
            t <= s || t > _duration || s < 0 ||
            !TrimTimelineMath.TryCreate(_duration, s, t, out var start, out var end))
        {
            RefreshControls(); // Reject rather than persist an invalid range.
            RangeReverted?.Invoke(this, EventArgs.Empty);
            return;
        }
        _start = start;
        _end = end;
        RefreshControls();
        Commit();
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        if (_duration <= 0) return;
        _start = 0;
        _end = _duration;
        RefreshControls();
        Commit();
    }

    private void Commit()
    {
        if (_duration <= 0) return;
        if (Math.Abs(_start - _committedStart) < 1e-7 &&
            Math.Abs(_end - _committedEnd) < 1e-7)
        { RangeReverted?.Invoke(this, EventArgs.Empty); return; }
        if (!TrimValidator.Validate(_start, _end, _duration).IsValid)
        {
            _start = _committedStart;
            _end = _committedEnd;
            RefreshControls();
            RangeReverted?.Invoke(this, EventArgs.Empty);
            return;
        }
        // Change is announced only after the user completes an intentional edit.
        _committedStart = _start;
        _committedEnd = _end;
        RangeCommitted?.Invoke(this, new TrimRangeCommittedEventArgs(_start, _end));
    }
}
