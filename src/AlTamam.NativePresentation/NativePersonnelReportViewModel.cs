using System.ComponentModel;
using System.IO;
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

    public NativePersonnelReportViewModel()
    {
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
        NextAttendanceCommand = new NativeCommand(_ => _ = NextAttendanceAsync(), _ => CanNextAttendance);
        PreviousAttendanceCommand = new NativeCommand(_ => _ = PreviousAttendanceAsync(), _ => CanPreviousAttendance);
        NextMovementsCommand = new NativeCommand(_ => _ = NextMovementsAsync(), _ => CanNextMovements);
        PreviousMovementsCommand = new NativeCommand(_ => _ = PreviousMovementsAsync(), _ => CanPreviousMovements);
        NextIdentitiesCommand = new NativeCommand(_ => _ = NextIdentitiesAsync(), _ => CanNextIdentities);
        PreviousIdentitiesCommand = new NativeCommand(_ => _ = PreviousIdentitiesAsync(), _ => CanPreviousIdentities);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public string ErrorMessage => _error;
    public NativePersonnelReportSnapshot? Report => _report;
    public string AttendancePageLabel => _report is null ? "—" :
        $"صفحة التحضير {_report.AttendancePage} — إجمالي {_report.AttendanceTotal}";
    public string MovementsPageLabel => _report is null ? "—" :
        $"صفحة التنقلات {_report.MovementPage} — إجمالي {_report.MovementTotal}";
    public string IdentitiesPageLabel => _report is null ? "—" :
        $"صفحة تغير الأسماء والأرقام {_report.IdentityPage} — إجمالي {_report.IdentityTotal}";
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

    private Task ChangePageAsync(bool allowed, string stream, int step, CancellationToken ct)
    {
        if (!allowed || _pages is not { } pages) return Task.CompletedTask;
        _pages = stream switch
        {
            "attendance" => pages with { AttendancePage = checked(pages.AttendancePage + step) },
            "movements" => pages with { MovementPage = checked(pages.MovementPage + step) },
            "identities" => pages with { IdentityPage = checked(pages.IdentityPage + step) },
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
            report.AttendanceTotal < 0 || report.MovementTotal < 0 || report.IdentityTotal < 0 ||
            report.AttendanceHasNext != ((long)requested.AttendancePage * 100 < report.AttendanceTotal) ||
            report.MovementHasNext != ((long)requested.MovementPage * 100 < report.MovementTotal) ||
            report.IdentityHasNext != ((long)requested.IdentityPage * 100 < report.IdentityTotal) ||
            report.StatusTotals is null || report.Attendance is null ||
            report.Movements is null || report.Identities is null ||
            report.StatusTotals.Count > 500 ||
            report.StatusTotals.Any(x => x is null || x.StatusName is not { Length: >= 1 and <= 180 } || x.Count < 0) ||
            !ValidPageCount(report.Attendance.Count, report.AttendanceTotal, requested.AttendancePage) ||
            !ValidPageCount(report.Movements.Count, report.MovementTotal, requested.MovementPage) ||
            !ValidPageCount(report.Identities.Count, report.IdentityTotal, requested.IdentityPage) ||
            report.Attendance.Any(x => x is null || x.StatusName is null || x.SiteName is null ||
                x.Date is not { Length: 10 } || x.Revision < 0) ||
            report.Movements.Any(x => x is null || x.Type is null || x.NameSnapshot is null ||
                x.NumberSnapshot is null || x.Date is not { Length: 10 }) ||
            report.Identities.Any(x => x is null || x.Name is null ||
                x.ServiceNumberA is null || x.ValidFrom is not { Length: 10 }))
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
            nameof(AttendancePageLabel), nameof(MovementsPageLabel),
            nameof(IdentitiesPageLabel), nameof(CanNextAttendance),
            nameof(CanPreviousAttendance), nameof(CanNextMovements),
            nameof(CanPreviousMovements), nameof(CanNextIdentities),
            nameof(CanPreviousIdentities)
        })
            PropertyChanged?.Invoke(this, new(name));
        foreach (ICommand command in new[]
        {
            CloseCommand, NextAttendanceCommand, PreviousAttendanceCommand,
            NextMovementsCommand, PreviousMovementsCommand,
            NextIdentitiesCommand, PreviousIdentitiesCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
