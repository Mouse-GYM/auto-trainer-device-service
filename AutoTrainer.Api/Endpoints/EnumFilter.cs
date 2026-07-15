namespace AutoTrainer.Api.Endpoints;

// Parses repeated (and/or comma-joined) enum query values into a validated list, or fails with a message.
// Accepts names ("AnimalMissing") and numeric codes ("201"); rejects anything not a defined enum member.
//
// Multi-select convention: repeated keys (?alarmId=201&alarmId=301) AND comma lists (?alarmId=201,301) both work.
public static class EnumFilter
{
    public static bool TryParse<TEnum>(string[]? raw, out List<TEnum> values, out string? error)
        where TEnum : struct, Enum
    {
        values = [];
        error = null;
        if (raw is null) return true;

        foreach (var item in raw)
        foreach (var token in item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Enum.IsDefined after TryParse rejects an out-of-range numeric like "999" (which TryParse would
            // otherwise accept as an undefined value of the enum type).
            if (Enum.TryParse<TEnum>(token, ignoreCase: true, out var v) && Enum.IsDefined(v))
                values.Add(v);
            else
            {
                error = $"Invalid {typeof(TEnum).Name} value '{token}'.";
                return false;
            }
        }

        return true;
    }
}
