using TwinQuota.Core;
using Xunit;

namespace TwinQuota.Core.Tests;

public sealed class MiniResizeCalculatorTests
{
    [Theory]
    [InlineData(double.NaN, 246.0)]
    [InlineData(double.PositiveInfinity, 246.0)]
    [InlineData(0.0, 246.0)]
    [InlineData(-50.0, 246.0)]
    [InlineData(80.0, 123.0)]
    [InlineData(123.0, 123.0)]
    [InlineData(180.0, 180.0)]
    [InlineData(246.0, 246.0)]
    [InlineData(320.0, 246.0)]
    public void ClampWidth_ClampsToAllowedRange(double input, double expected)
    {
        Assert.Equal(expected, MiniResizeCalculator.ClampWidth(input), 5);
    }

    [Fact]
    public void Calculate_RightEdgeShrinksWidthWithoutMovingLeft()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.Right,
            startWidth: 246,
            startLeft: 500,
            deltaX: -46);

        Assert.Equal(200, result.Width, 5);
        Assert.Equal(500, result.Left, 5);
    }

    [Fact]
    public void Calculate_LeftEdgeShrinksWidthAndShiftsLeftToKeepRightEdgeAnchored()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.Left,
            startWidth: 246,
            startLeft: 500,
            deltaX: 46);

        Assert.Equal(200, result.Width, 5);
        Assert.Equal(546, result.Left, 5);
    }

    [Fact]
    public void Calculate_LeftEdgeClampsAtMinWidthAndPreservesRightAnchor()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.TopLeft,
            startWidth: 246,
            startLeft: 500,
            deltaX: 200);

        Assert.Equal(123, result.Width, 5);
        Assert.Equal(623, result.Left, 5);
    }

    [Fact]
    public void Calculate_RightEdgeExpandsUpToMaxWidthOnly()
    {
        var result = MiniResizeCalculator.Calculate(
            MiniResizeDirection.BottomRight,
            startWidth: 180,
            startLeft: 500,
            deltaX: 120);

        Assert.Equal(246, result.Width, 5);
        Assert.Equal(500, result.Left, 5);
    }
}
