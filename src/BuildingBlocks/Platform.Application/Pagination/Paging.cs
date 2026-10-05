namespace Platform.Application.Pagination;

/// <summary>
/// Offset pagination and sorting shared by every list endpoint:
/// <c>page</c> (1-based), <c>pageSize</c>, <c>q</c> (search), <c>sort</c> (field name), <c>dir</c> (asc/desc).
/// Values are clamped to safe bounds; unknown sort fields fall back to each endpoint's default.
/// </summary>
public sealed record PageRequest(int Page = 1, int PageSize = 20, string? Q = null, string? Sort = null, string? Dir = null)
{
    public const int MaxPageSize = 200;

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    public string? Search => string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

/// <summary>A page of results. Serialised as <c>{ "data": items, "meta": { page, pageSize, total } }</c>.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount) : IPagedResult
{
    object IPagedResult.Items => Items;

    public object Meta => new PageMeta(Page, PageSize, TotalCount);
}

public sealed record PageMeta(int Page, int PageSize, long Total);

public interface IPagedResult
{
    object Items { get; }
    object Meta { get; }
}

/// <summary>A response with custom metadata, e.g. <c>{ data: notifications, meta: { unread } }</c>.</summary>
public sealed record WithMeta(object Data, object Meta);

/// <summary>
/// Keyset (cursor) pagination for feeds and mobile clients: stable under concurrent inserts
/// and O(1) regardless of depth. The cursor is opaque to clients.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
