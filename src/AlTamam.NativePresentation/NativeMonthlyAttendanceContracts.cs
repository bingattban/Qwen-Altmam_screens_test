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
    string Period, string SiteId, string SearchTerm, int Page, int PageSize = 100)
{
    public bool ExplicitArchive { get; init; }
}

public sealed record NativeMonthlyCell(
    string Symbol, string Description, string? Color, string MovementNote)
{
    public string MovementKind { get; init; } = "";
}

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

public sealed record NativeMonthlyCellDisplay(string Symbol, string Description)
{
    public string BackgroundColor { get; init; } = "Transparent";
    public string ForegroundColor { get; init; } = "#0F2942";
    public string MovementKind { get; init; } = "";
    public string DisplaySymbol
    {
        get
        {
            string marker = MovementKind switch
            {
                "return" => "↩",
                "internal-transfer" => "↔",
                "external-transfer" => "↗",
                "separation" => "⊗",
                _ => ""
            };
            return marker.Length == 0 ? Symbol : Symbol.Length == 0 ? marker : Symbol + " " + marker;
        }
    }

    // Only opaque RGB snapshots are accepted, matching the original browser UI.
    // No status or movement is inferred from a caption, current catalogue or color.
    public static NativeMonthlyCellDisplay From(NativeMonthlyCell? saved)
    {
        if (saved is null) return new("", "");
        string background = "Transparent";
        string foreground = "#0F2942";
        if (saved.Color is { Length: 7 } color && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit))
        {
            background = color.ToUpperInvariant();
            static double Linear(string rgb, int offset)
            {
                double s = Convert.ToInt32(rgb.Substring(offset, 2), 16) / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            double luminance = 0.2126 * Linear(background, 1) +
                               0.7152 * Linear(background, 3) +
                               0.0722 * Linear(background, 5);
            foreground = luminance > 0.179 ? "#000000" : "#FFFFFF";
        }
        return new(saved.Symbol, saved.Description)
        {
            BackgroundColor = background,
            ForegroundColor = foreground,
            MovementKind = saved.MovementKind
        };
    }
}

public sealed record NativeMonthlyDisplayRow(
    int RowNumber, string PersonId, string ServiceNumberA, string Rank,
    string Name, string SiteName, IReadOnlyList<string> CustomCells,
    IReadOnlyList<NativeMonthlyCellDisplay> Days);

 
/// <summary>
/// Read-only authenticated daily snapshot drill-down. Implementations MUST
/// reuse the canonical MonthlyCellDetailsAsync service and validate the live
/// SQLCipher user/role/nonce before and after fetching the historical record.
/// </summary>
public interface INativeMonthlyCellDetailsReader
{
    Task<NativeMonthlyCellDetails> ReadCellDetailsAsync(
        NativeIdentity actor, NativeMonthlyCellRequest request,
        CancellationToken ct = default);
}

public sealed record NativeMonthlyCellRequest(
    string PersonId, DateOnly BusinessDate, string SiteId);

public sealed record NativeMonthlyRevisionDetail(
    string BeforeStatus, string AfterStatus, string Reason,
    string ActorUserId, string ExecutedAtUtc, string AuthorizationMethod);

public sealed record NativeMonthlyCellDetails(
    string PersonId, DateOnly BusinessDate, string PersonName,
    string ServiceNumberA, string SiteName,
    string AttendanceStatus, string AttendanceSymbol, string AttendanceSiteName,
    string AttendanceSource, string AttendanceCommittedAtUtc,
    string MovementSummary, string MovementReason,
    IReadOnlyList<NativeMonthlyRevisionDetail> Revisions);

/// <summary>
/// Native monthly XLSX export is a privileged, two-step user intention:
/// preview is read-only, while explicit confirm calls the ORIGINAL
/// MonthlyExcelExportService and its locally configured safe output directory.
/// </summary>
public interface INativeMonthlyExcelExportBackend
{
    Task<NativeMonthlyExcelPreview> PreviewExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default);
    Task<NativeMonthlyExcelReceipt> ExportExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default);
}

/// <summary>Actual canonical workbook row progress, not a timer or simulated percentage.</summary>
public interface INativeMonthlyExcelProgressBackend
{
    Task<NativeMonthlyExcelReceipt> ExportWithProgressAsync(
        NativeIdentity actor,NativeMonthlyExcelFilter filter,Action<int,int> progress,
        CancellationToken ct=default);
}

public sealed record NativeMonthlyExcelFilter(
    string Period, string SiteId, string SearchTerm)
{
    // Populated only by explicit export confirmation; monthly scope remains
    // the original period/site/search triple.
    public string? OutputDirectory {get;init;}
    public string? OutputFileName {get;init;}
}

public sealed record NativeMonthlyExcelPreview(
    string Period, string SiteId, string SearchTerm, string SiteName,
    string SuggestedFileName, long PersonCount, int DaysInMonth)
{
    // Null denotes legacy callers. Empty denotes an unavailable configured
    // folder; a native operator must explicitly select a local directory.
    public string? OutputDirectory {get;init;}
}

public sealed record NativeMonthlyExcelReceipt(
    string Period, string SiteId, string SearchTerm,
    string PrimaryFilePath, int FileCount, long PersonCount)
{
    public IReadOnlyList<NativeMonthlyExcelFile>? Files {get;init;}
}

public sealed record NativeMonthlyExcelFile(
    string SiteId,string SiteName,string FilePath,long PersonCount);
