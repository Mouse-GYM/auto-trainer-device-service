namespace AutoTrainer.Api.Endpoints;

// Parses/clamps the page & pageSize query params. 1-based; pageSize bounded to protect on-device SQLite.
// Forgiving by design: page < 1 becomes 1, pageSize is clamped into [1, MaxPageSize] — a caller never gets a
// 400 for an out-of-range page size.
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest From(int? page, int? pageSize)
    {
        var p = page is { } pv && pv > 0 ? pv : 1;
        var s = pageSize is { } sv ? Math.Clamp(sv, 1, MaxPageSize) : DefaultPageSize;
        return new PageRequest(p, s);
    }
}
