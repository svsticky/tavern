namespace Backend.Utils.DateTime;

/// <summary>
/// Helpers for working with the association's timezone, configured through the AssociationTimeZone environment variable.
/// </summary>
public static class AssociationTimeZoneUtils
{
    /// <summary>
    /// Resolves the association's timezone from the AssociationTimeZone environment variable, falling back to
    /// Europe/Amsterdam when it is not configured. A configured but unknown timezone identifier deliberately
    /// throws rather than silently falling back, because quietly using the wrong timezone would shift dates without anyone noticing.
    /// </summary>
    /// <returns>The timezone the association operates in.</returns>
    public static TimeZoneInfo Get()
    {
        string timezoneId = Environment.GetEnvironmentVariable("AssociationTimeZone") ?? "Europe/Amsterdam";
        return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    }

    /// <summary>
    /// Interprets a wall-clock time (the Kind and any offset are ignored) as local time in the association's timezone and converts it to UTC.
    /// </summary>
    /// <param name="wallClock">The local date and time in the association's timezone.</param>
    /// <returns>The corresponding UTC time.</returns>
    /// <exception cref="ArgumentException">Thrown when the time does not exist because of a daylight saving gap.</exception>
    public static System.DateTime ToUtc(System.DateTime wallClock)
    {
        var tz = Get();
        var unspecified = System.DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified))
        {
            throw new ArgumentException($"{wallClock:yyyy-MM-dd HH:mm} does not exist in time zone {tz.Id} (daylight saving gap).");
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }
}
