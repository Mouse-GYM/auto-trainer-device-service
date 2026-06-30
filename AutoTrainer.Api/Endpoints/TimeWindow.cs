namespace AutoTrainer.Api.Endpoints;

// Parses a compact time-window token such as "30s", "10m", "2h", "5d", "3w".
// Units: s=seconds, m=minutes, h=hours, d=days, w=weeks. ("m" is minutes — months are not supported.)
public static class TimeWindow
{
    public static readonly TimeSpan Default = TimeSpan.FromDays(5);

    public const string Usage = "Use a positive number followed by s, m, h, d, or w (e.g. 30s, 10m, 2h, 5d, 3w).";

    // Returns true (with the default window) when value is null/empty, true with the parsed
    // window when valid, and false when the token is malformed or out of range.
    public static bool TryParse(string? value, out TimeSpan window)
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

        try
        {
            window = char.ToLowerInvariant(value[split]) switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'm' => TimeSpan.FromMinutes(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                'w' => TimeSpan.FromDays(amount * 7),
                _ => TimeSpan.Zero
            };
        }
        catch (OverflowException)
        {
            return false;
        }

        return window > TimeSpan.Zero;
    }
}
