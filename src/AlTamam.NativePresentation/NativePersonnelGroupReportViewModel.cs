using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 9: explicit group history selection from the CURRENT authorized
/// native administration roster page, read-only via ORIGINAL SQLCipher report.
/// Never infer group IDs from visible name/number, and never auto-select all.
/// </summary>
public sealed class NativePersonnelGroupReportViewModel : INotifyPropertyChanged
{
    private INativePersonnelGroupReportReader? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<IReadOnlyList<NativePersonnelDisplayRow>>? _currentRows;
    private NativeIdentity? _actor;
    private IReadOnlyList<NativePersonnelDisplayRow>? _openedRows;
    private IReadOnlyList<NativePersonnelDisplayRow> _selectedRows = [];
    private CancellationTokenSource? _pending;
    private int _generation;
    private int _summaryPage = 1;
    private bool _open, _busy;
    private string _error = "", _from = "", _to = "";
    private int _movementType;
    private NativePersonnelGroupReportSnapshot? _report;

    public NativePersonnelGroupExcelViewModel Excel { get; } = new();

    public NativePersonnelGroupReportViewModel()
    {
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
        RunCommand = new NativeCommand(_ => _ = RunAsync(), _ => CanRead && _selectedRows.Count > 0);
        ClearFiltersCommand = new NativeCommand(_ => _ = ClearFiltersAsync(), _ => CanRead && _selectedRows.Count > 0);
        NextSummaryCommand = new NativeCommand(_ => _ = NextSummaryAsync(), _ => CanNextSummary);
        PreviousSummaryCommand = new NativeCommand(_ => _ = PreviousSummaryAsync(), _ => CanPreviousSummary);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public string ErrorMessage => _error;
    public NativePersonnelGroupReportSnapshot? Report => _report;
    public IReadOnlyList<NativePersonnelDisplayRow> CandidateRows => _openedRows ?? [];
    public int SelectedCount => _selectedRows.Count;
    public string SelectedCountLabel => $"الأفراد المختارون صراحة: {SelectedCount} من {CandidateRows.Count} في الصفحة الحالية (الحد 100)";
    public string DailySummaryPageLabel => _report is null
        ? "التفاصيل اليومية غير محملة"
        : $"صفحة الملخص اليومي {_report.DailyStatusPage} (100 صف في الصفحة)";
    public string AppliedFiltersLabel => _report is null
        ? "لم يُعتمد تقرير جماعي بعد."
        : $"الفترة المعتمدة: {(_report.DateFrom == "" ? "من البداية" : _report.DateFrom)} — {(_report.DateTo == "" ? "حتى النهاية" : _report.DateTo)}؛ نوع الحركة: {MovementTypeOptions.First(x => x.Value == _report.MovementType).Title}";
    public bool CanNextSummary => CanRead && !_busy &&
        _report is { DailyStatusHasNext: true } && _summaryPage < 100000;
    public bool CanPreviousSummary => CanRead && !_busy &&
        _report is not null && _summaryPage > 1;
    public IReadOnlyList<NativeReportMovementTypeOption> MovementTypeOptions { get; } =
    [
        new(0, "جميع الحركات"),
        new(1, "نقل داخلي"),
        new(2, "نقل خارجي"),
        new(3, "فصل"),
        new(4, "عودة للخدمة")
    ];

    public string DateFromDraft
    {
        get => _from;
        set
        {
            if (_from == value) return;
            _from = value ?? "";
            InvalidateLoadedResult();
        }
    }
    public string DateToDraft
    {
        get => _to;
        set
        {
            if (_to == value) return;
            _to = value ?? "";
            InvalidateLoadedResult();
        }
    }
    public int MovementTypeDraft
    {
        get => _movementType;
        set
        {
            if (_movementType == value) return;
            _movementType = value;
            InvalidateLoadedResult();
        }
    }

    public ICommand CloseCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand NextSummaryCommand { get; }
    public ICommand PreviousSummaryCommand { get; }

    public void AttachBackend(INativePersonnelGroupReportReader backend,
        Func<NativeIdentity?> liveActor,
        Func<IReadOnlyList<NativePersonnelDisplayRow>> currentRows)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Group report backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _currentRows = currentRows ?? throw new ArgumentNullException(nameof(currentRows));
        if (backend is INativePersonnelGroupExcelBackend excel)
            Excel.Attach(excel, () => _liveActor?.Invoke(), CurrentExcelRequest,
                () => _report);
    }

    // Only an already displayed canonical report for the SAME current,
    // explicitly selected PersonIds can authorize a fresh export preview.
    private NativePersonnelGroupReportRequest? CurrentExcelRequest()
    {
        if (!CanRead || _busy || _report is not { } report ||
            report.PersonIds.Count != _selectedRows.Count ||
            !_selectedRows.Select(r => r.PersonId)
                .SequenceEqual(report.PersonIds, StringComparer.Ordinal) ||
            report.DateFrom != _from || report.DateTo != _to ||
            report.MovementType != _movementType)
            return null;
        return new NativePersonnelGroupReportRequest(
            report.PersonIds.ToArray(), report.DateFrom, report.DateTo,
            report.MovementType, 1, report.SiteId, report.StatusId);
    }

    private bool CanRead => _open && _backend is not null &&
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _openedRows is { Count: >= 1 and <= 100 } rows &&
        ReferenceEquals(_currentRows?.Invoke(), rows) &&
        _selectedRows.All(row => rows.Any(current => ReferenceEquals(current, row)));

    public void Open(IReadOnlyList<NativePersonnelDisplayRow> rows)
    {
        Close();
        if (_backend is null || rows is not { Count: >= 1 and <= 100 } ||
            !ReferenceEquals(_currentRows?.Invoke(), rows) ||
            _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            rows.Any(r => r is null || r.PersonId is not { Length: >= 3 and <= 128 } ||
                r.PersonId.Any(char.IsControl)) ||
            rows.Select(r => r.PersonId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            return;
        _actor = actor;
        _openedRows = rows;
        _open = true;
        Changed();
    }

    public void SelectRows(IReadOnlyList<NativePersonnelDisplayRow> selected)
    {
        if (!CanRead) { Close(); return; }
        var candidates = _openedRows!;
        if (selected is null || selected.Count > 100 ||
            selected.Any(r => r is null || !candidates.Any(c => ReferenceEquals(c, r))) ||
            selected.Select(r => r.PersonId).Distinct(StringComparer.Ordinal).Count() != selected.Count)
        {
            InvalidateLoadedResult();
            _error = "اختيار غير صالح: يجب تحديد الأفراد من الصفحة الحالية دون تكرار أو تجاوز 100.";
            Changed();
            return;
        }
        if (selected.Count == _selectedRows.Count &&
            selected.Zip(_selectedRows).All(pair => ReferenceEquals(pair.First, pair.Second)))
            return;
        _selectedRows = selected.ToArray();
        InvalidateLoadedResult();
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!CanRead) { Close(); return; }
        if (_selectedRows.Count is < 1 or > 100)
        {
            _error = "حدد فردا واحدا على الأقل من الصفحة الحالية لإنشاء تقرير جماعي.";
            Changed();
            return;
        }
        if (!ValidDateRange(_from, _to) || _movementType is < 0 or > 4)
        {
            _error = "الفترة غير صالحة؛ استخدم YYYY-MM-DD وتأكد من ترتيب التاريخ ونوع الحركة.";
            Changed();
            return;
        }
        _summaryPage = 1;
        Excel.Close();
        await LoadAsync(ct);
    }

    public Task ClearFiltersAsync(CancellationToken ct = default)
    {
        _from = "";
        _to = "";
        _movementType = 0;
        Excel.Close();
        InvalidateLoadedResult();
        return RunAsync(ct);
    }

    public async Task NextSummaryAsync(CancellationToken ct = default)
    {
        if (!CanNextSummary) return;
        _summaryPage++;
        await LoadAsync(ct);
    }

    public async Task PreviousSummaryAsync(CancellationToken ct = default)
    {
        if (!CanPreviousSummary) return;
        _summaryPage--;
        await LoadAsync(ct);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!CanRead || _backend is null || _actor is not { } actor)
        {
            Close();
            return;
        }
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        var request = new NativePersonnelGroupReportRequest(
            _selectedRows.Select(row => row.PersonId).ToArray(),
            _from, _to, _movementType, _summaryPage);
        _busy = true;
        _report = null;
        _error = "";
        Changed();
        try
        {
            var result = await _backend.ReadGroupAsync(actor, request, linked.Token);
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
                _error = "تعذر التحقق من التقرير الجماعي المعتمد؛ أُخفيت النتائج حماية للسجلات.";
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

    private void InvalidateLoadedResult()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        Excel.Close();
        _busy = false;
        _report = null;
        _error = "";
        _summaryPage = 1;
        Changed();
    }

    public void Close()
    {
        Excel.Close();
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _openedRows = null;
        _selectedRows = [];
        _open = _busy = false;
        _error = _from = _to = "";
        _movementType = 0;
        _summaryPage = 1;
        _report = null;
        Changed();
    }

    private static bool ValidDateRange(string from, string to)
    {
        static bool IsDate(string value) => DateOnly.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
        return (from.Length == 0 || IsDate(from)) &&
               (to.Length == 0 || IsDate(to)) &&
               (from.Length == 0 || to.Length == 0 ||
                string.CompareOrdinal(from, to) <= 0);
    }

    private static void Validate(
        NativePersonnelGroupReportSnapshot report,
        NativePersonnelGroupReportRequest request)
    {
        if (report is null || report.PersonIds is null ||
            report.Persons is null || report.StatusTotals is null ||
            report.DailyStatuses is null ||
            report.PersonCount != request.PersonIds.Count ||
            report.PersonIds.Count != request.PersonIds.Count ||
            !report.PersonIds.SequenceEqual(request.PersonIds, StringComparer.Ordinal) ||
            report.DateFrom != request.DateFrom || report.DateTo != request.DateTo ||
            report.MovementType != request.MovementType ||
            report.SiteId != request.SiteId || report.StatusId != request.StatusId ||
            report.DailyStatusPage != request.SummaryPage ||
            report.Persons.Count != request.PersonIds.Count ||
            report.Persons.Where((row, index) => row is null ||
                row.PersonId != request.PersonIds[index] ||
                row.Name is not { Length: > 0 } ||
                row.ServiceNumberA is not { Length: > 0 } ||
                row.FirstRecordedDate is not { Length: 10 } ||
                row.AttendanceTotal < 0 || row.MovementTotal < 0 ||
                row.StatusTotals is null ||
                row.StatusTotals.Count > 5000 ||
                row.StatusTotals.Any(s => s is null ||
                    s.Count < 0 || s.StatusId is null || s.StatusName is null) ||
                row.StatusTotals.Sum(s => s.Count) != row.AttendanceTotal).Any() ||
            report.AttendanceTotal < 0 || report.MovementTotal < 0 ||
            report.Persons.Sum(row => row.AttendanceTotal) != report.AttendanceTotal ||
            report.Persons.Sum(row => row.MovementTotal) != report.MovementTotal ||
            report.StatusTotals.Count > 5000 ||
            report.StatusTotals.Any(s => s is null ||
                s.Count < 0 || s.StatusId is null || s.StatusName is null) ||
            report.StatusTotals.Sum(s => s.Count) != report.AttendanceTotal ||
            report.DailyStatuses.Count > 100 ||
            (report.DailyStatusHasNext && report.DailyStatuses.Count != 100) ||
            report.DailyStatuses.Any(s => s is null ||
                s.Date is not { Length: 10 } ||
                !DateOnly.TryParseExact(s.Date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                s.StatusId is null || s.StatusName is null ||
                s.Count is < 0 || s.PersonCount is < 0 ||
                s.PersonCount > report.PersonCount || s.Count < s.PersonCount))
            throw new InvalidDataException("Forged, incomplete or mismatched canonical group report.");
    }

    private void Changed()
    {
        Excel.NotifySourceChanged();
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsLoading), nameof(ErrorMessage),
            nameof(Report), nameof(CandidateRows), nameof(SelectedCount),
            nameof(SelectedCountLabel), nameof(DateFromDraft), nameof(DateToDraft),
            nameof(MovementTypeDraft), nameof(MovementTypeOptions),
            nameof(DailySummaryPageLabel), nameof(AppliedFiltersLabel),
            nameof(CanNextSummary), nameof(CanPreviousSummary),
            nameof(Excel)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand command in new[]
        {
            CloseCommand, RunCommand, ClearFiltersCommand,
            NextSummaryCommand, PreviousSummaryCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
