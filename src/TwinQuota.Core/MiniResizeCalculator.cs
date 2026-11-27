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

public readonly record struct MiniResizeResult(double Width, double Left);

public static class MiniResizeCalculator
{
    public const double DefaultWidth = 246.0;
    public const double MinWidth = 123.0;
    public const double MaxWidth = 246.0;

    public static double ClampWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
        {
            return DefaultWidth;
        }

        return Math.Clamp(width, MinWidth, MaxWidth);
    }

    public static MiniResizeResult Calculate(
        MiniResizeDirection direction,
        double startWidth,
        double startLeft,
        double deltaX)
    {
        var clampedStartWidth = ClampWidth(startWidth);
        var isLeftEdge = direction is MiniResizeDirection.Left or MiniResizeDirection.TopLeft or MiniResizeDirection.BottomLeft;
        var signedDeltaX = isLeftEdge ? -deltaX : deltaX;

        var newWidth = Math.Clamp(clampedStartWidth + signedDeltaX, MinWidth, MaxWidth);
        var widthChange = newWidth - clampedStartWidth;

        var newLeft = isLeftEdge
            ? startLeft - widthChange
            : startLeft;

        return new MiniResizeResult(newWidth, newLeft);
    }
}
