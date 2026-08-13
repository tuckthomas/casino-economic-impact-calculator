namespace SaveNEIN.Server.Services;

public static class IsochroneSchedule
{
    public static bool IsWithinWindow(TimeOnly localTime, TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            return false;
        }

        return start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }

    public static TimeSpan GetRemainingWindow(TimeOnly localTime, TimeOnly end)
    {
        var remaining = end.ToTimeSpan() - localTime.ToTimeSpan();
        return remaining > TimeSpan.Zero ? remaining : remaining + TimeSpan.FromDays(1);
    }

    public static TimeZoneInfo ResolveTimeZone(string? configuredId, ILogger logger)
    {
        var candidates = new[]
        {
            configuredId,
            "America/New_York",
            "Eastern Standard Time"
        };

        foreach (var candidate in candidates.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate!);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        logger.LogWarning("Unable to resolve the configured Eastern Time zone. Falling back to the server local time zone.");
        return TimeZoneInfo.Local;
    }
}
