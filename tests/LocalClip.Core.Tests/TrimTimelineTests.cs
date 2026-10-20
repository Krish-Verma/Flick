using System;
using LocalClip.Core.Rules;
using Xunit;

namespace LocalClip.Core.Tests;

public sealed class TrimTimelineTests
{
    [Theory]
    [InlineData(30, 0, 30, 0, 30)]
    [InlineData(30, 4.2, 25.6, 4.2, 25.6)]
    [InlineData(30, -9, 40, 0, 30)]
    [InlineData(30, 29.95, 0, 29.75, 30)]
    public void CreatingSelectionClampsToValidFiniteRange(double duration,
        double s, double e, double expectedStart, double expectedEnd)
    {
        Assert.True(TrimTimelineMath.TryCreate(duration, s, e, out var start, out var end));
        Assert.Equal(expectedStart, start, 5);
        Assert.Equal(expectedEnd, end, 5);
        Assert.True(TrimValidator.Validate(start, end, duration).IsValid);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTimeValuesCannotCreateValidTrim(double bad)
    {
        Assert.False(TrimTimelineMath.TryCreate(30, bad, 20, out _, out _));
        Assert.False(TrimTimelineMath.TryCreate(30, 0, bad, out _, out _));
        Assert.False(TrimValidator.Validate(bad, 10, 30).IsValid);
        Assert.False(TrimValidator.Validate(1, bad, 30).IsValid);
        Assert.False(TrimValidator.Validate(0, 10, bad).IsValid);
    }

    [Fact]
    public void SliderAndKeyboardChangesPreserveMinQuarterSecond()
    {
        Assert.Equal(4.75, TrimTimelineMath.MoveStart(20, 5, 30), 6);
        Assert.Equal(5.25, TrimTimelineMath.MoveEnd(2, 5, 30), 6);
        Assert.Equal(0, TrimTimelineMath.MoveStart(-3, 12, 30), 6);
        Assert.Equal(30, TrimTimelineMath.MoveEnd(100, 12, 30), 6);
    }

    [Theory]
    [InlineData("4.25", 4.25)]
    [InlineData("0:04.25", 4.25)]
    [InlineData("1:02.50", 62.50)]
    [InlineData("1:01:02.5", 3662.5)]
    public void ParsesPreciseTimes(string value, double expected)
    {
        Assert.True(TrimTimelineMath.TryParseTime(value, out var seconds));
        Assert.Equal(expected, seconds, 5);
    }

    [Theory]
    [InlineData("1:63")]
    [InlineData("-2")]
    [InlineData("NaN")]
    [InlineData("")]
    [InlineData("2:3:4:5")]
    public void RejectsNonsensicalTimes(string input)
    {
        Assert.False(TrimTimelineMath.TryParseTime(input, out _));
    }

    [Fact]
    public void FormattingIsStableAtMinuteBoundaries()
    {
        Assert.Equal("1:00.00", TrimTimelineMath.Format(59.999));
        Assert.Equal("0:04.25", TrimTimelineMath.Format(4.25));
        Assert.Equal("1:01:02.50", TrimTimelineMath.Format(3662.50));
    }
}
