namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only, authenticated boundary to the CANONICAL SQLCipher archive-period
/// and monthly-grid services. No controller writes or inferred attendance.
/// </summary>
public interface INativeMonthlyAttendanceReader
{
    Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
        NativeIdentity actor, CancellationToken ct = default);

    Task<NativeMonthlySnapshot> ReadMonthAsync(
        NativeIdentity actor, NativeMonthlyFilter filter,
        CancellationToken ct = default);
}

public sealed record NativeMonthlyCatalogue(
    IReadOnlyList<string> Periods,
    IReadOnlyList<NativeDashboardSite> Sites);

public sealed record NativeMonthlyFilter(
    string Period, string SiteId, string SearchTerm, int Page, int PageSize = 100);

public sealed record NativeMonthlyCell(
    string Symbol, string Description, string? Color, string MovementNote);

public sealed record NativeMonthlyRow(
    string PersonId, string ServiceNumberA, string Name, string SiteName,
    IReadOnlyList<string> CustomCells, IReadOnlyList<NativeMonthlyCell?> DayCells)
{
    public int RowNumber { get; init; }
}

public sealed record NativeMonthlySnapshot(
    string Period, string SiteId, int Page, int DaysInMonth, long TotalRows,
    bool HasNext, IReadOnlyList<NativeDashboardColumn> CustomColumns,
    IReadOnlyList<NativeMonthlyRow> Rows);

public sealed record NativeMonthlyPeriodOption(string Period, string Label);

public sealed record NativeMonthlyCellDisplay(string Symbol, string Description);

public sealed record NativeMonthlyDisplayRow(
    int RowNumber, string PersonId, string ServiceNumberA, string Rank,
    string Name, string SiteName, IReadOnlyList<string> CustomCells,
    IReadOnlyList<NativeMonthlyCellDisplay> Days);
