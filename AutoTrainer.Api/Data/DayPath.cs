namespace AutoTrainer.Api.Data;

public static class DayPath
{
    // ApiProjectStatus.DayPath is the absolute path to the current day's directory, not a bare date, so the
    // YYYYMMDD day is its final component. Parse once here so the stored column is a real date.
    //
    // The day deliberately does not come from the event's `when`: that is UTC, and a UTC day boundary would
    // split a training day that runs across midnight. DayPath is the producer's own notion of the day.
    public static DateOnly? TryGetDay(string? dayPath)
    {
        if (string.IsNullOrWhiteSpace(dayPath))
            return null;

        var name = Path.GetFileName(
            dayPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return DateOnly.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day)
            ? day
            : null;
    }
}
