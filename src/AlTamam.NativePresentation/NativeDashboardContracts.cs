using AlTamam.Domain.Enums;

namespace AlTamam.NativePresentation;

/// <summary>
/// Typed read-only dashboard projection. This interface never grants a write,
/// hides SQLCipher or replaces the audited C# daily read services. Each request
/// carries the server-issued native principal, not a user-chosen role.
/// </summary>
public interface INativeDashboardReader
{
    Task<NativeDashboardSnapshot> ReadAsync(NativeIdentity principal,
        NativeDashboardFilter filter, CancellationToken ct = default);
}

public sealed record NativeDashboardFilter(
    DateOnly BusinessDate, string SiteId, string StatusId, string SearchTerm,
    int Page, int PageSize = 100);

public sealed record NativeDashboardStatus(
    string StatusId, string Name, long Count, string Color);

public sealed record NativeDashboardSite(string SiteId, string Name);

public sealed record NativeDashboardColumn(string ColumnId, string Caption);

public sealed record NativeDashboardRow(
    string PersonId, string ServiceNumberA, string Name, string SiteName,
    string StatusName, string? StatusColor, IReadOnlyList<string> CustomCells)
{
    public int RowNumber { get; init; }
}

/// <summary>
/// Values are taken verbatim from GetDashboardSummary and QueryDailyRoster
/// (including their documented custom columns). Unprepared is intentionally
/// not a primary Dashboard counter, matching the existing functional UI.
/// </summary>
public sealed record NativeDashboardSnapshot(
    DateOnly BusinessDate, string SiteId, long TotalForce,
    long AutomaticallyPrepared, long InternalTransferDay,
    IReadOnlyList<NativeDashboardStatus> StatusCounts,
    IReadOnlyList<NativeDashboardSite> Sites,
    IReadOnlyList<NativeDashboardRow> Rows,
    IReadOnlyList<NativeDashboardColumn> CustomColumns,
    long TotalRows, int Page, bool HasNext);

public sealed record NativeDashboardCounter(
    string StatusId, string Label, long Count, string Color, string Percentage)
{
    public bool IsSelected { get; init; }
}
