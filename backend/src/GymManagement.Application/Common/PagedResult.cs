namespace GymManagement.Application.Common;

/// <summary>Base class for every server-side paged, searchable, sortable list request.</summary>
public class PagedRequest
{
    private int _pageSize = 25;
    private int _pageNumber = 1;

    /// <summary>1-based page index.</summary>
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>Clamped to 200 so a client can never request an unbounded page.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch { < 1 => 25, > 200 => 200, _ => value };
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }

    public int Skip => (PageNumber - 1) * PageSize;
}

/// <summary>One page of results plus the totals the DataGrid pager needs.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    public static PagedResult<T> Empty(int pageSize = 25) => new(Array.Empty<T>(), 0, 1, pageSize);
}
