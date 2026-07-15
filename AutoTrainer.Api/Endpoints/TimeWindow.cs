namespace AutoTrainer.Api.Endpoints;

// Parses a compact time-window token such as "30s", "10m", "2h", "5d", "3w", "5c".
// Units: s=seconds, m=minutes, h=hours, d=rolling days (24h), w=weeks, c=calendar days. ("m" is minutes —
// months are not supported.)
//
// The s/m/h/d/w units are a fixed rolling duration measured back from now. The "c" unit is aligned to LOCAL
// midnight instead: "1c" is today (local midnight -> now), "5c" is today plus the previous four calendar days
// (local midnight four days ago -> now). Its length therefore depends on the current local time of day.
public readonly struct TimeWindow
{
    // Exactly one of these describes the window: a fixed rolling duration, or a count of calendar days (>= 1).
    private readonly TimeSpan _rolling;
    private readonly int _calendarDays;

    private TimeWindow(TimeSpan rolling, int calendarDays)
    {
        _rolling = rolling;
        _calendarDays = calendarDays;
    }

    public static readonly TimeWindow Default = new(TimeSpan.FromDays(5), 0);

    public const string Usage =
        "Use a positive number followed by s, m, h, d, w, or c (e.g. 30s, 10m, 2h, 5d, 3w, 5c). " +
        "c = calendar days aligned to local midnight (1c = today).";

    // The inclusive start of the window in UTC, given the current UTC time. Rolling windows subtract the fixed
    // duration; calendar-day windows step back to local midnight of (calendarDays - 1) days ago.
    public DateTime StartUtc(DateTime nowUtc) =>
        _calendarDays > 0
            ? nowUtc.ToLocalTime().Date.AddDays(-(_calendarDays - 1)).ToUniversalTime()
            : nowUtc - _rolling;

    // Returns true (with the default window) when value is null/empty, true with the parsed window when valid,
    // and false when the token is malformed or out of range.
    public static bool TryParse(string? value, out TimeWindow window)
    {
        window = Default;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();

        var split = 0;
        while (split < value.Length && char.IsAsciiDigit(value[split]))
            split++;

        // Need at least one digit and exactly one trailing unit character.
        if (split == 0 || value.Length - split != 1)
            return false;

        if (!long.TryParse(value[..split], NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;

        var unit = char.ToLowerInvariant(value[split]);

        // Calendar days are not a fixed duration — carry the count and resolve it against "now" in StartUtc.
        if (unit == 'c')
        {
            if (amount > int.MaxValue)
                return false;
            window = new TimeWindow(TimeSpan.Zero, (int)amount);
            return true;
        }

        try
        {
            var rolling = unit switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                'w' => TimeSpan.FromDays(amount * 7),
                _ => TimeSpan.Zero
            };

            if (rolling <= TimeSpan.Zero)
                return false;

            window = new TimeWindow(rolling, 0);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
