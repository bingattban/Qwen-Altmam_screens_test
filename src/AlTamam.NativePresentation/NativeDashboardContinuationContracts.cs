namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only application boundary for the configured automatic attendance
/// deadline and the saved automatic-prepared register. It has NO mutation or
/// scheduler/RunPending method: the original C# policy and scheduler own those.
/// </summary>
public interface INativeDashboardContinuationReader
{
    Task<NativeAutoAttendancePolicy> ReadPolicyAsync(
        NativeIdentity principal, CancellationToken ct = default);

    Task<NativeAutoRegisterPage> ReadAutoRegisterAsync(
        NativeIdentity principal, NativeAutoRegisterFilter filter,
        CancellationToken ct = default);
}

public sealed record NativeAutoAttendancePolicy(string DueTimeLocal, bool Configured);

public sealed record NativeAutoRegisterFilter(
    DateOnly BusinessDate, string SiteId, string SearchTerm, int Page, int PageSize = 100)
{
    public string StatusId => "__AUTO_PREPARED__";
}

public sealed record NativeAutoRegisterRow(
    string PersonId, string ServiceNumberA, string Name, string SiteName,
    string SavedBusinessDate, string AutoSource, string CommittedAtUtc,
    string StatusName, IReadOnlyList<string> CustomCells)
{
    public int RowNumber { get; init; }
}

public sealed record NativeAutoRegisterPage(
    DateOnly BusinessDate, string SiteId, long Total, int Page, bool HasNext,
    IReadOnlyList<NativeDashboardColumn> CustomColumns,
    IReadOnlyList<NativeAutoRegisterRow> Rows);
