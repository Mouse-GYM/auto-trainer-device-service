namespace AutoTrainer.Api.Contracts;

// The consistent paged envelope for every list endpoint. 1-based Page.
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

// The count-only analogue of PagedResult, for /count sub-resources that report just how many rows match a
// list endpoint's filters — no items, no paging.
public sealed record CountDto(int Count);
