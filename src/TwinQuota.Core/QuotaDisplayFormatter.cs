namespace TwinQuota.Core;

public static class QuotaDisplayFormatter
{
    public static string FormatReset(DateTimeOffset? resetTime, DateTimeOffset now)
    {
        if (resetTime is null)
        {
            return "reset time unknown";
        }

        var remaining = resetTime.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "reset due";
        }

        if (remaining.TotalDays >= 1)
        {
            return remaining.Hours > 0
                ? $"resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
                : $"resets in {(int)remaining.TotalDays}d";
        }

        if (remaining.TotalHours >= 1)
        {
            return remaining.Minutes > 0
                ? $"resets in {(int)remaining.TotalHours}h {remaining.Minutes}m"
                : $"resets in {(int)remaining.TotalHours}h";
        }

        return $"resets in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }
}
