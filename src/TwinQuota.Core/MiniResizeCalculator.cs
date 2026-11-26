namespace TwinQuota.Core;

public enum MiniResizeDirection
{
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public readonly record struct MiniResizeResult(double Scale, double Left, double Top);

public static class MiniResizeCalculator
{
    public const double MinScale = 0.5;
    public const double MaxScale = 1.0;

    public static double ClampScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return MaxScale;
        }

        return Math.Clamp(scale, MinScale, MaxScale);
    }

    public static MiniResizeResult Calculate(
        MiniResizeDirection direction,
        double startScale,
        double startLeft,
        double startTop,
        double baseWidth,
        double baseHeight,
        double deltaX,
        double deltaY)
    {
        var clampedStartScale = ClampScale(startScale);
        if (baseWidth <= 0 || baseHeight <= 0)
        {
            return new MiniResizeResult(clampedStartScale, startLeft, startTop);
        }

        var startWidth = baseWidth * clampedStartScale;
        var startHeight = baseHeight * clampedStartScale;

        var signedDeltaX = direction is MiniResizeDirection.Left or MiniResizeDirection.TopLeft or MiniResizeDirection.BottomLeft
            ? -deltaX
            : deltaX;

        var signedDeltaY = direction is MiniResizeDirection.TopLeft or MiniResizeDirection.TopRight
            ? -deltaY
            : deltaY;

        double targetScale;
        if (direction is MiniResizeDirection.Left or MiniResizeDirection.Right)
        {
            targetScale = (startWidth + signedDeltaX) / baseWidth;
        }
        else
        {
            var scaleDeltaX = signedDeltaX / baseWidth;
            var scaleDeltaY = signedDeltaY / baseHeight;
            var chosenDelta = Math.Abs(scaleDeltaX) >= Math.Abs(scaleDeltaY)
                ? scaleDeltaX
                : scaleDeltaY;
            targetScale = clampedStartScale + chosenDelta;
        }

        var newScale = Math.Clamp(targetScale, MinScale, MaxScale);
        var scaleChange = newScale - clampedStartScale;

        var newLeft = direction is MiniResizeDirection.Left or MiniResizeDirection.TopLeft or MiniResizeDirection.BottomLeft
            ? startLeft - scaleChange * baseWidth
            : startLeft;

        var newTop = direction is MiniResizeDirection.TopLeft or MiniResizeDirection.TopRight
            ? startTop - scaleChange * baseHeight
            : startTop;

        return new MiniResizeResult(newScale, newLeft, newTop);
    }
}
