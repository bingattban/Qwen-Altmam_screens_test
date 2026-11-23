namespace AlTamam.NativePresentation;

/// <summary>
/// WPF read-only personnel management: the ONLY data source is the existing
/// PersonRosterService.QueryPersonsAsync/GetPersonSummaryAsync SQLCipher bridge.
/// The session and manager role are checked by the production adapter.
/// </summary>
public interface INativePersonnelDirectoryReader
{
    Task<NativePersonnelCatalogue> ReadPersonnelCatalogueAsync(
        NativeIdentity actor, CancellationToken ct = default);
    Task<NativePersonnelSnapshot> ReadPageAsync(
        NativeIdentity actor, NativePersonnelFilter filter,
        CancellationToken ct = default);
    Task<NativePersonnelProfile> ReadProfileAsync(
        NativeIdentity actor, string personId, CancellationToken ct = default);
}

public sealed record NativePersonnelCatalogue(
    IReadOnlyList<NativeDashboardSite> Sites);

public sealed record NativePersonnelFilter(
    string SiteId, string SearchTerm, int OrganizationalStatus,
    int Page, int PageSize = 100);

public sealed record NativePersonnelRow(
    string PersonId, string ServiceNumberA, string Name, string SiteName,
    int OrganizationalStatus, bool Active, long Revision,
    IReadOnlyList<string> CustomCells)
{
    public int RowNumber { get; init; }
}

public sealed record NativePersonnelSnapshot(
    string SiteId, string SearchTerm, int OrganizationalStatus,
    int Page, long TotalRows, bool HasNext,
    IReadOnlyList<NativeDashboardColumn> CustomColumns,
    IReadOnlyList<NativePersonnelRow> Rows);

public sealed record NativePersonnelIdentityHistory(
    string Name, string ServiceNumberA, string ValidFrom, string ValidTo,
    string Reason, string RecordedAtUtc);

public sealed record NativePersonnelProfile(
    string PersonId, string Name, string ServiceNumberA,
    int OrganizationalStatus, bool Active, long Revision,
    string FirstRecordedLocalDate,
    IReadOnlyList<NativePersonnelIdentityHistory> IdentityHistory);

public sealed record NativePersonnelDisplayRow(
    int RowNumber, string PersonId, string ServiceNumberA, string Rank,
    string Name, string SiteName, string StatusName, bool Active, long Revision,
    IReadOnlyList<string> CustomCells);

public sealed record NativePersonnelStatusOption(int Value, string Label);
