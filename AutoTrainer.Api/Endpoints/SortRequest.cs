namespace AutoTrainer.Api.Endpoints;

// Parses the shared `sort` query param used across list endpoints. Convention (JSON:API style):
//   ?sort=field   -> ascending by `field`
//   ?sort=-field  -> descending by `field`  (leading '-' = descending; a leading '+' is allowed and means ascending)
// Field names are matched case-insensitively by the endpoint. Absent/blank => no client sort, so the endpoint
// keeps its own default order. Which field names are valid is endpoint-specific — SortRequest only carries the
// parsed field name + direction; the endpoint decides what it recognizes.
public readonly record struct SortRequest(string? Field, bool Descending)
{
    public bool HasSort => Field is not null;

    public static SortRequest From(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return default;

        var s = sort.Trim();
        var desc = s[0] == '-';
        if (desc || s[0] == '+') s = s[1..].Trim();

        return s.Length == 0 ? default : new SortRequest(s, desc);
    }

    // True when a sort was requested on `field` (case-insensitive).
    public bool Is(string field) =>
        Field is not null && string.Equals(Field, field, StringComparison.OrdinalIgnoreCase);
}
