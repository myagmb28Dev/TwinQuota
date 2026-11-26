using TwinQuota.Core;
using Xunit;

namespace TwinQuota.Core.Tests;

public sealed class MiniResizeCalculatorTests
{
    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(-0.5, 1.0)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    public void ClampScale_ClampsToAllowedRange(double input, double expected)
    {
        Assert.Equal(expected, MiniResizeCalculator.ClampScale(input), 5);
    }

    [Fact]
    public void Calculate_RightEdgeShrinksProportionallyWithoutMovingTopLeft()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.Right,
            startScale: 1.0,
            startLeft: 500,
            startTop: 300,
            baseWidth: 246,
            baseHeight: 100,
            deltaX: -49.2,
            deltaY: 0);

        Assert.Equal(0.8, result.Scale, 5);
        Assert.Equal(500, result.Left, 5);
        Assert.Equal(300, result.Top, 5);
    }

    [Fact]
    public void Calculate_LeftEdgeShrinksAndShiftsLeftToKeepRightEdgeAnchored()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.Left,
            startScale: 1.0,
            startLeft: 500,
            startTop: 300,
            baseWidth: 200,
            baseHeight: 100,
            deltaX: 50,
            deltaY: 0);

        Assert.Equal(0.75, result.Scale, 5);
        Assert.Equal(550, result.Left, 5);
        Assert.Equal(300, result.Top, 5);
    }

    [Fact]
    public void Calculate_TopLeftCornerClampsAtMinScaleAndPreservesBottomRightAnchor()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.TopLeft,
            startScale: 1.0,
            startLeft: 500,
            startTop: 300,
            baseWidth: 200,
            baseHeight: 100,
            deltaX: 160,
            deltaY: 20);

        Assert.Equal(0.5, result.Scale, 5);
        Assert.Equal(600, result.Left, 5);
        Assert.Equal(350, result.Top, 5);
    }

    [Fact]
    public void Calculate_BottomRightCornerExpandsUpToMaxScaleOnly()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.BottomRight,
            startScale: 0.75,
            startLeft: 500,
            startTop: 300,
            baseWidth: 200,
            baseHeight: 100,
            deltaX: 20,
            deltaY: 80);

        Assert.Equal(1.0, result.Scale, 5);
        Assert.Equal(500, result.Left, 5);
        Assert.Equal(300, result.Top, 5);
    }
}
