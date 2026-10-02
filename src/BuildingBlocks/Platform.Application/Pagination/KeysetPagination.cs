namespace Platform.Application.Pagination;

/// <summary>
/// Keyset pagination request specification.
/// </summary>
/// <typeparam name="TKey">The type of the cursor token or key.</typeparam>
/// <param name="Cursor">The cursor denoting the position after which records should be fetched.</param>
/// <param name="Limit">The maximum number of items to return.</param>
/// <param name="Ascending">Whether the sorting order is ascending or descending.</param>
public sealed record KeysetRequest<TKey>(
    TKey? Cursor = default,
    int Limit = 25,
    bool Ascending = false)
{
    public const int MaxLimit = 100;
    public int SafeLimit => Math.Clamp(Limit, 1, MaxLimit);
}

/// <summary>
/// Keyset pagination response container.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <typeparam name="TKey">The cursor key type.</typeparam>
/// <param name="Items">The page items.</param>
/// <param name="NextCursor">The key/cursor to fetch the subsequent page, or null if no further items exist.</param>
/// <param name="HasMore">Indicates whether additional items exist beyond this page.</param>
public sealed record KeysetResponse<TItem, TKey>(
    IReadOnlyList<TItem> Items,
    TKey? NextCursor,
    bool HasMore);
