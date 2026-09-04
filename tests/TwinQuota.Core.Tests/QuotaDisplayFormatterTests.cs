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
}
