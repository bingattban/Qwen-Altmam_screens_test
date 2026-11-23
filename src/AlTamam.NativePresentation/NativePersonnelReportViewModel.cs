using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// First read-only slice of ONE unified screen_025: individual historical
/// evidence from the canonical SQLCipher service. No report export or write.
/// </summary>
public sealed class NativePersonnelReportViewModel : INotifyPropertyChanged
{
    private INativePersonnelReportReader? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _isCurrentRow;
    private NativeIdentity? _actor;
    private NativePersonnelDisplayRow? _sourceRow;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy;
    private string _error = "";
    private NativePersonnelReportSnapshot? _report;
    private NativeIndividualReportPages? _pages;
    private string _dateFromDraft = "";
    private string _dateToDraft = "";
    private int _movementTypeDraft;

    public NativePersonnelReportViewModel()
    {
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
        NextAttendanceCommand = new NativeCommand(_ => _ = NextAttendanceAsync(), _ => CanNextAttendance);
        PreviousAttendanceCommand = new NativeCommand(_ => _ = PreviousAttendanceAsync(), _ => CanPreviousAttendance);
        NextMovementsCommand = new NativeCommand(_ => _ = NextMovementsAsync(), _ => CanNextMovements);
        PreviousMovementsCommand = new NativeCommand(_ => _ = PreviousMovementsAsync(), _ => CanPreviousMovements);
        NextIdentitiesCommand = new NativeCommand(_ => _ = NextIdentitiesAsync(), _ => CanNextIdentities);
        PreviousIdentitiesCommand = new NativeCommand(_ => _ = PreviousIdentitiesAsync(), _ => CanPreviousIdentities);
        NextSitesCommand = new NativeCommand(_ => _ = NextSitesAsync(), _ => CanNextSites);
        PreviousSitesCommand = new NativeCommand(_ => _ = PreviousSitesAsync(), _ => CanPreviousSites);
        NextRevisionsCommand = new NativeCommand(_ => _ = NextRevisionsAsync(), _ => CanNextRevisions);
        PreviousRevisionsCommand = new NativeCommand(_ => _ = PreviousRevisionsAsync(), _ => CanPreviousRevisions);
        NextAuditCommand = new NativeCommand(_ => _ = NextAuditAsync(), _ => CanNextAudit);
        PreviousAuditCommand = new NativeCommand(_ => _ = PreviousAuditAsync(), _ => CanPreviousAudit);
        ApplyDateFiltersCommand = new NativeCommand(_ => _ = ApplyDateFiltersAsync(), _ => CanApplyDateFilters);
        ClearDateFiltersCommand = new NativeCommand(_ => _ = ClearDateFiltersAsync(), _ => CanApplyDateFilters);
        ApplyMovementTypeFilterCommand = new NativeCommand(
            _ => _ = ApplyMovementTypeFilterAsync(), _ => CanApplyMovementTypeFilter);
        ClearMovementTypeFilterCommand = new NativeCommand(
            _ => _ = ClearMovementTypeFilterAsync(), _ => CanApplyMovementTypeFilter);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public string ErrorMessage => _error;
    public NativePersonnelReportSnapshot? Report => _report;
    public string DateFromDraft
    {
        get => _dateFromDraft;
        set
        {
            if (_dateFromDraft == value) return;
            _dateFromDraft = value ?? "";
            Changed();
        }
    }
    public string DateToDraft
    {
        get => _dateToDraft;
        set
        {
            if (_dateToDraft == value) return;
            _dateToDraft = value ?? "";
            Changed();
        }
    }
    // Always describes the SOURCE-VERIFIED displayed result, not unsubmitted inputs.
    public string AppliedDateRangeLabel => _report is null
        ? "لا توجد نتائج تاريخية معتمدة حاليا."
        : $"الفترة المطبقة: {(_report.DateFrom.Length == 0 ? "من البداية" : _report.DateFrom)} — {(_report.DateTo.Length == 0 ? "حتى النهاية" : _report.DateTo)}";
    public bool CanApplyDateFilters => CanRead && _pages is not null;
    public bool CanApplyMovementTypeFilter => CanRead && _pages is not null;

    public IReadOnlyList<NativeReportMovementTypeOption> MovementTypeOptions { get; } =
    [
        new(0, "جميع الحركات"),
        new(1, "نقل داخلي"),
        new(2, "نقل خارجي"),
        new(3, "فصل"),
        new(4, "عودة للخدمة")
    ];

    public int MovementTypeDraft
    {
        get => _movementTypeDraft;
        set
        {
            if (_movementTypeDraft == value) return;
            _movementTypeDraft = value;
            Changed();
        }
    }

    public string AppliedMovementTypeLabel => _report is null
        ? "لا توجد بيانات معتمدة للحركات حاليا."
        : "مرشح الحركات المطبق: " + MovementTypeOptions
            .First(x => x.Value == _report.MovementType).Title;
    public string AttendancePageLabel => _report is null ? "—" :
        $"صفحة التحضير {_report.AttendancePage} — إجمالي {_report.AttendanceTotal}";
    public string MovementsPageLabel => _report is null ? "—" :
        $"صفحة التنقلات {_report.MovementPage} — إجمالي {_report.MovementTotal}";
    public string IdentitiesPageLabel => _report is null ? "—" :
        $"صفحة تغير الأسماء والأرقام {_report.IdentityPage} — إجمالي {_report.IdentityTotal}";
    public string SitesPageLabel => _report is null ? "—" :
        $"صفحة المواقع التاريخية {_report.SitePage} — إجمالي {_report.SiteTotal}";
    public string RevisionsPageLabel => _report is null ? "—" :
        $"صفحة تصحيحات التحضير {_report.RevisionPage} — إجمالي {_report.RevisionTotal}";
    public string AuditPageLabel => _report is null ? "—" :
        $"صفحة سجل العمليات {_report.AuditPage} — إجمالي {_report.AuditTotal}";
    public bool CanNextAudit => CanNavigate && _report is { AuditHasNext: true } &&
                                _pages!.AuditPage < 100000;
    public bool CanPreviousAudit => CanNavigate && _pages!.AuditPage > 1;
    public bool CanNextSites => CanNavigate && _report is { SiteHasNext: true } &&
                                _pages!.SitePage < 100000;
    public bool CanPreviousSites => CanNavigate && _pages!.SitePage > 1;
    public bool CanNextRevisions => CanNavigate && _report is { RevisionHasNext: true } &&
                                    _pages!.RevisionPage < 100000;
    public bool CanPreviousRevisions => CanNavigate && _pages!.RevisionPage > 1;
    public bool CanNextAttendance => CanNavigate && _report is { AttendanceHasNext: true } &&
                                     _pages!.AttendancePage < 100000;
    public bool CanPreviousAttendance => CanNavigate && _pages!.AttendancePage > 1;
    public bool CanNextMovements => CanNavigate && _report is { MovementHasNext: true } &&
                                    _pages!.MovementPage < 100000;
    public bool CanPreviousMovements => CanNavigate && _pages!.MovementPage > 1;
    public bool CanNextIdentities => CanNavigate && _report is { IdentityHasNext: true } &&
                                     _pages!.IdentityPage < 100000;
    public bool CanPreviousIdentities => CanNavigate && _pages!.IdentityPage > 1;
    public ICommand CloseCommand { get; }
    public ICommand NextAttendanceCommand { get; }
    public ICommand PreviousAttendanceCommand { get; }
    public ICommand NextMovementsCommand { get; }
    public ICommand PreviousMovementsCommand { get; }
    public ICommand NextIdentitiesCommand { get; }
    public ICommand PreviousIdentitiesCommand { get; }
    public ICommand NextSitesCommand { get; }
    public ICommand PreviousSitesCommand { get; }
    public ICommand NextRevisionsCommand { get; }
    public ICommand PreviousRevisionsCommand { get; }
    public ICommand NextAuditCommand { get; }
    public ICommand PreviousAuditCommand { get; }
    public ICommand ApplyDateFiltersCommand { get; }
    public ICommand ClearDateFiltersCommand { get; }
    public ICommand ApplyMovementTypeFilterCommand { get; }
    public ICommand ClearMovementTypeFilterCommand { get; }

    private bool CanRead => _open && _backend is not null &&
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        _sourceRow is { } row && _isCurrentRow?.Invoke(row) == true &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    private bool CanNavigate => CanRead && !_busy && _report is not null && _pages is not null;

    public void AttachBackend(INativePersonnelReportReader backend,
        Func<NativeIdentity?> liveActor, Func<NativePersonnelDisplayRow, bool> isCurrentRow)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Report backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _isCurrentRow = isCurrentRow ?? throw new ArgumentNullException(nameof(isCurrentRow));
    }

    public async Task OpenAsync(NativePersonnelDisplayRow? selected, CancellationToken ct = default)
    {
        if (_backend is null || selected is null || selected.PersonId is not
            { Length: >= 3 and <= 128 } || selected.PersonId.Any(char.IsControl) ||
            _isCurrentRow?.Invoke(selected) != true ||
            _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
            return;
        Close();
        _actor = actor;
        _sourceRow = selected;
        _pages = new NativeIndividualReportPages(selected.PersonId);
        _open = true;
        Changed();
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct = default)
    {
        if (!CanRead || _pages is null || _backend is null || _actor is not { } actor)
        {
            Close();
            return;
        }
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _report = null; // Do not show outdated historical evidence during paging.
        _error = "";
        NativeIndividualReportPages request = _pages;
        Changed();
        try
        {
            NativePersonnelReportSnapshot result =
                await _backend.ReadIndividualAsync(actor, request, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            Validate(result, request);
            _report = result;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _report = null;
                _error = "تعذر التحقق من التقرير التاريخي المعتمد؛ أُخفيت البيانات حماية للسجلات.";
            }
        }
        finally
        {
            if (version == _generation)
            {
                _pending = null;
                _busy = false;
                Changed();
            }
        }
    }

    private static bool ValidDateRange(string from, string to)
    {
        static bool IsIsoDate(string text) => DateOnly.TryParseExact(
            text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
        return (from.Length == 0 || IsIsoDate(from)) &&
               (to.Length == 0 || IsIsoDate(to)) &&
               (from.Length == 0 || to.Length == 0 ||
                string.CompareOrdinal(from, to) <= 0);
    }

    public async Task ApplyDateFiltersAsync(CancellationToken ct = default)
    {
        if (!CanRead || _pages is not { } pages)
        {
            Close();
            return;
        }
        string from = _dateFromDraft;
        string to = _dateToDraft;
        if (!ValidDateRange(from, to))
        {
            _error = "أدخل التاريخ بصيغة YYYY-MM-DD، ويجب ألا يتجاوز تاريخ البداية تاريخ النهاية.";
            Changed();
            return;
        }
        // An application of a range is one new original service query:
        // all six independent history streams reset to first page.
        _pages = pages with
        {
            AttendancePage = 1, MovementPage = 1, IdentityPage = 1,
            SitePage = 1, RevisionPage = 1, AuditPage = 1,
            DateFrom = from, DateTo = to
        };
        await LoadAsync(ct);
    }

    public Task ClearDateFiltersAsync(CancellationToken ct = default)
    {
        _dateFromDraft = "";
        _dateToDraft = "";
        Changed();
        return ApplyDateFiltersAsync(ct);
    }

    public async Task ApplyMovementTypeFilterAsync(CancellationToken ct = default)
    {
        if (!CanRead || _pages is not { } pages)
        {
            Close();
            return;
        }
        int movementType = _movementTypeDraft;
        if (movementType is < 0 or > 4)
        {
            _error = "نوع الحركة غير صالح؛ اختر نوعا من القائمة المعتمدة.";
            Changed();
            return;
        }
        // Only the movement stream is restricted. Retain period and all
        // other independent source page indices without hidden rewrites.
        _pages = pages with { MovementType = movementType, MovementPage = 1 };
        await LoadAsync(ct);
    }

    public Task ClearMovementTypeFilterAsync(CancellationToken ct = default)
    {
        _movementTypeDraft = 0;
        Changed();
        return ApplyMovementTypeFilterAsync(ct);
    }

    public Task NextAttendanceAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextAttendance, "attendance", +1, ct);
    public Task PreviousAttendanceAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousAttendance, "attendance", -1, ct);
    public Task NextMovementsAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextMovements, "movements", +1, ct);
    public Task PreviousMovementsAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousMovements, "movements", -1, ct);
    public Task NextIdentitiesAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextIdentities, "identities", +1, ct);
    public Task PreviousIdentitiesAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousIdentities, "identities", -1, ct);
    public Task NextSitesAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextSites, "sites", +1, ct);
    public Task PreviousSitesAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousSites, "sites", -1, ct);
    public Task NextRevisionsAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextRevisions, "revisions", +1, ct);
    public Task PreviousRevisionsAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousRevisions, "revisions", -1, ct);
    public Task NextAuditAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanNextAudit, "audit", +1, ct);
    public Task PreviousAuditAsync(CancellationToken ct = default) =>
        ChangePageAsync(CanPreviousAudit, "audit", -1, ct);

    private Task ChangePageAsync(bool allowed, string stream, int step, CancellationToken ct)
    {
        if (!allowed || _pages is not { } pages) return Task.CompletedTask;
        _pages = stream switch
        {
            "attendance" => pages with { AttendancePage = checked(pages.AttendancePage + step) },
            "movements" => pages with { MovementPage = checked(pages.MovementPage + step) },
            "identities" => pages with { IdentityPage = checked(pages.IdentityPage + step) },
            "sites" => pages with { SitePage = checked(pages.SitePage + step) },
            "revisions" => pages with { RevisionPage = checked(pages.RevisionPage + step) },
            "audit" => pages with { AuditPage = checked(pages.AuditPage + step) },
            _ => throw new InvalidOperationException("Unknown report page.")
        };
        return LoadAsync(ct);
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _sourceRow = null;
        _pages = null;
        _dateFromDraft = "";
        _dateToDraft = "";
        _movementTypeDraft = 0;
        _open = false;
        _busy = false;
        _report = null;
        _error = "";
        Changed();
    }

    private static void Validate(NativePersonnelReportSnapshot report,
        NativeIndividualReportPages requested)
    {
        if (report is null || report.PersonId != requested.PersonId ||
            report.Name is not { Length: >= 1 and <= 180 } ||
            report.ServiceNumberA is not { Length: >= 1 and <= 70 } ||
            report.PageSize != 100 ||
            report.AttendancePage != requested.AttendancePage ||
            report.MovementPage != requested.MovementPage ||
            report.IdentityPage != requested.IdentityPage ||
            report.SitePage != requested.SitePage ||
            report.RevisionPage != requested.RevisionPage ||
            report.AuditPage != requested.AuditPage ||
            report.DateFrom != requested.DateFrom ||
            report.DateTo != requested.DateTo ||
            report.MovementType != requested.MovementType ||
            report.MovementType is < 0 or > 4 ||
            report.AttendanceTotal < 0 || report.MovementTotal < 0 || report.IdentityTotal < 0 ||
            report.SiteTotal < 0 || report.RevisionTotal < 0 ||
            report.AuditTotal < 0 ||
            report.AttendanceHasNext != ((long)requested.AttendancePage * 100 < report.AttendanceTotal) ||
            report.MovementHasNext != ((long)requested.MovementPage * 100 < report.MovementTotal) ||
            report.IdentityHasNext != ((long)requested.IdentityPage * 100 < report.IdentityTotal) ||
            report.SiteHasNext != ((long)requested.SitePage * 100 < report.SiteTotal) ||
            report.RevisionHasNext != ((long)requested.RevisionPage * 100 < report.RevisionTotal) ||
            report.AuditHasNext != ((long)requested.AuditPage * 100 < report.AuditTotal) ||
            report.StatusTotals is null || report.Attendance is null ||
            report.Movements is null || report.Identities is null ||
            report.Sites is null || report.Revisions is null ||
            report.MonthlyTotals is null || report.Audit is null ||
            report.StatusTotals.Count > 500 ||
            report.StatusTotals.Any(x => x is null || x.StatusName is not { Length: >= 1 and <= 180 } || x.Count < 0) ||
            !ValidPageCount(report.Attendance.Count, report.AttendanceTotal, requested.AttendancePage) ||
            !ValidPageCount(report.Movements.Count, report.MovementTotal, requested.MovementPage) ||
            !ValidPageCount(report.Identities.Count, report.IdentityTotal, requested.IdentityPage) ||
            !ValidPageCount(report.Sites.Count, report.SiteTotal, requested.SitePage) ||
            !ValidPageCount(report.Revisions.Count, report.RevisionTotal, requested.RevisionPage) ||
            !ValidPageCount(report.Audit.Count, report.AuditTotal, requested.AuditPage) ||
            report.MonthlyTotals.Count > 5000 ||
            report.MonthlyTotals.Any(month => month is null ||
                !DateOnly.TryParseExact(month.Period + "-01", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                month.StatusId is not { Length: >= 1 and <= 128 } ||
                month.StatusName is not { Length: >= 1 and <= 180 } ||
                month.Count <= 0) ||
            report.MonthlyTotals.Select(month =>
                (month.Period, month.StatusId, month.StatusName)).Distinct().Count() !=
                report.MonthlyTotals.Count ||
            report.MonthlyTotals.Sum(month => month.Count) != report.AttendanceTotal ||
            report.Audit.Any(item => item is null ||
                item.Operation is not { Length: >= 1 and <= 180 } ||
                item.UsernameSnapshot is not { Length: >= 1 and <= 180 } ||
                item.FullNameSnapshot is null ||
                item.ExecutedAtUtc is not { Length: >= 10 and <= 80 } ||
                item.Description is null) ||
            report.Attendance.Any(x => x is null || x.StatusName is null || x.SiteName is null ||
                x.Date is not { Length: 10 } || x.Revision < 0) ||
            report.Movements.Any(x => x is null || x.Type is null || x.NameSnapshot is null ||
                x.NumberSnapshot is null || x.Date is not { Length: 10 }) ||
            report.Identities.Any(x => x is null || x.Name is null ||
                x.ServiceNumberA is null || x.ValidFrom is not { Length: 10 }) ||
            report.Sites.Any(x => x is null ||
                x.SiteId is not { Length: >= 1 and <= 128 } ||
                x.SiteName is not { Length: >= 1 and <= 180 } ||
                x.ValidFrom is not { Length: 10 } ||
                x.CreatedAtUtc is null) ||
            report.Revisions.Any(x => x is null ||
                x.Date is not { Length: 10 } ||
                x.Before is not { Length: >= 1 and <= 180 } ||
                x.After is not { Length: >= 1 and <= 180 } ||
                x.ExecutedAtUtc is not { Length: >= 10 } ||
                x.ExecutorSnapshot is null))
            throw new InvalidDataException("Incomplete or forged canonical personnel report.");
    }

    private static bool ValidPageCount(int count, long total, int page)
    {
        if (page < 1 || page > 100000) return false;
        long preceding = (long)(page - 1) * 100;
        return count == (int)Math.Min(100L, Math.Max(0L, total - preceding));
    }

    private void Changed()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsLoading), nameof(ErrorMessage), nameof(Report),
            nameof(DateFromDraft), nameof(DateToDraft),
            nameof(AppliedDateRangeLabel), nameof(CanApplyDateFilters),
            nameof(MovementTypeOptions), nameof(MovementTypeDraft),
            nameof(AppliedMovementTypeLabel), nameof(CanApplyMovementTypeFilter),
            nameof(AttendancePageLabel), nameof(MovementsPageLabel),
            nameof(IdentitiesPageLabel), nameof(SitesPageLabel),
            nameof(RevisionsPageLabel), nameof(AuditPageLabel),
            nameof(CanNextAudit), nameof(CanPreviousAudit), nameof(CanNextSites),
            nameof(CanPreviousSites), nameof(CanNextRevisions),
            nameof(CanPreviousRevisions), nameof(CanNextAttendance),
            nameof(CanPreviousAttendance), nameof(CanNextMovements),
            nameof(CanPreviousMovements), nameof(CanNextIdentities),
            nameof(CanPreviousIdentities)
        })
            PropertyChanged?.Invoke(this, new(name));
        foreach (ICommand command in new[]
        {
            CloseCommand, NextAttendanceCommand, PreviousAttendanceCommand,
            NextMovementsCommand, PreviousMovementsCommand,
            NextIdentitiesCommand, PreviousIdentitiesCommand,
            NextSitesCommand, PreviousSitesCommand,
            NextRevisionsCommand, PreviousRevisionsCommand,
            NextAuditCommand, PreviousAuditCommand,
            ApplyDateFiltersCommand, ClearDateFiltersCommand,
            ApplyMovementTypeFilterCommand, ClearMovementTypeFilterCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
