using TwinQuota.Core;

namespace TwinQuota.Core.Tests;

public sealed class QuotaDisplayFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 2, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(6, 21, 6, "resets in 6d 21h")]
    [InlineData(0, 2, 8, "resets in 2h 8m")]
    [InlineData(0, 2, 0, "resets in 2h")]
    public void KeepsUsefulResetPrecision(int days, int hours, int minutes, string expected)
    {
        var resetTime = Now.AddDays(days).AddHours(hours).AddMinutes(minutes);

        Assert.Equal(expected, QuotaDisplayFormatter.FormatReset(resetTime, Now));
    }

    [Fact]
    public void RoundsSubMinuteResetUpInsteadOfShowingDueEarly()
    {
        Assert.Equal("resets in 1m", QuotaDisplayFormatter.FormatReset(Now.AddSeconds(5), Now));
    }

    [Fact]
    public void MarksElapsedResetAsDue()
    {
        Assert.Equal("reset due", QuotaDisplayFormatter.FormatReset(Now.AddSeconds(-1), Now));
    }

    [Theory]
    [InlineData(5, 7, "↻ 5.3d")]
    [InlineData(1, 0, "↻ 1d")]
    [InlineData(0, 12, "↻ 12h")]
    public void FormatsCompactMiniReset(int days, int hours, string expected)
    {
        var resetTime = Now.AddDays(days).AddHours(hours);
        Assert.Equal(expected, QuotaDisplayFormatter.FormatMiniReset(resetTime, Now));
    }

    [Fact]
    public void FormatsMinuteMiniReset()
    {
        var resetTime = Now.AddMinutes(45);
        Assert.Equal("↻ 45m", QuotaDisplayFormatter.FormatMiniReset(resetTime, Now));
    }

    [Fact]
    public void FormatsElapsedMiniReset()
    {
        Assert.Equal("↻ reset", QuotaDisplayFormatter.FormatMiniReset(Now.AddSeconds(-1), Now));
        Assert.Equal(string.Empty, QuotaDisplayFormatter.FormatMiniReset(null, Now));
    }
}
