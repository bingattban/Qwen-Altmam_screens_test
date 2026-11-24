using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only individual XLSX intent: separate preview/confirm and exact
/// source-verified immutable PersonId, live manager session and totals.
/// No paths/ZIP/SQL/retry in WPF; all source reads and file commit in service.
/// </summary>
public sealed class NativeIndividualExcelViewModel : INotifyPropertyChanged
{
    private INativePersonnelIndividualExcelBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeIndividualReportPages?>? _currentRequest;
    private Func<NativePersonnelReportSnapshot?>? _currentReport;
    private NativeIdentity? _approvedActor;
    private NativeIndividualReportPages? _approvedRequest;
    private NativePersonnelReportSnapshot? _approvedReport;
    private NativeIndividualExcelPreview? _preview;
    private NativeIndividualExcelReceipt? _receipt;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy;
    private string _error = "";

    public NativeIndividualExcelViewModel()
    {
        OpenPreviewCommand = new NativeCommand(_ => _ = OpenPreviewAsync(), _ => CanOpen);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand OpenPreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }
    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public NativeIndividualExcelPreview? Preview => _preview;
    public NativeIndividualExcelReceipt? Receipt => _receipt;
    public string Error => _error;
    public string CompletionMessage => _receipt is null ? "" :
        $"تم تصدير تقرير الفرد التاريخي إلى Excel في {_receipt.SheetCount} أوراق.";
    private bool CanOpen => !_open && !_busy && _backend is not null &&
        _liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        ValidRequest(_currentRequest?.Invoke()) && _currentReport?.Invoke() is not null;
    public bool CanConfirm => _open && !_busy && _preview is not null &&
        _receipt is null && _approvedActor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _approvedRequest == _currentRequest?.Invoke() &&
        ReferenceEquals(_approvedReport, _currentReport?.Invoke());

    public void Attach(INativePersonnelIndividualExcelBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativeIndividualReportPages?> request,
        Func<NativePersonnelReportSnapshot?> currentReport)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Individual Excel backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _currentRequest = request ?? throw new ArgumentNullException(nameof(request));
        _currentReport = currentReport ?? throw new ArgumentNullException(nameof(currentReport));
    }

    private static bool ValidRequest(NativeIndividualReportPages? req)
    {
        if (req is null || req.PersonId is not { Length: >= 3 and <= 128 } ||
            req.PersonId.Any(char.IsControl) ||
            req.DateFrom is null || req.DateTo is null ||
            req.SiteId is null || req.StatusId is null ||
            req.SiteId.Length > 128 || req.StatusId.Length > 128 ||
            req.SiteId.Any(char.IsControl) || req.StatusId.Any(char.IsControl) ||
            req.MovementType is < 0 or > 4)
            return false;
        static bool ISO(string value) => value.Length == 0 ||
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
        return ISO(req.DateFrom) && ISO(req.DateTo) &&
            (req.DateFrom.Length == 0 || req.DateTo.Length == 0 ||
             string.CompareOrdinal(req.DateFrom, req.DateTo) <= 0);
    }

    // Pager positions are display-only. Excel always fetches ALL six streams
    // from page 1 using currently applied immutable individual filters.
    private static NativeIndividualReportPages ExportRequest(NativeIndividualReportPages pages) =>
        new(pages.PersonId, DateFrom: pages.DateFrom, DateTo: pages.DateTo,
            MovementType: pages.MovementType,
            SiteId: pages.SiteId, StatusId: pages.StatusId);

    private static void ValidatePreview(
        NativeIndividualExcelPreview p, NativeIndividualReportPages request,
        NativePersonnelReportSnapshot shown)
    {
        if (p is null || p.PersonId != request.PersonId || p.PersonId != shown.PersonId ||
            p.DateFrom != request.DateFrom || p.DateTo != request.DateTo ||
            p.SiteId != request.SiteId || p.StatusId != request.StatusId ||
            p.MovementType != request.MovementType ||
            p.Name != shown.Name || p.ServiceNumberA != shown.ServiceNumberA ||
            p.AttendanceTotal != shown.AttendanceTotal ||
            p.MovementTotal != shown.MovementTotal ||
            p.IdentityTotal != shown.IdentityTotal ||
            p.SiteTotal != shown.SiteTotal ||
            p.RevisionTotal != shown.RevisionTotal ||
            p.AuditTotal != shown.AuditTotal ||
            p.Fingerprint is not { Length: 64 } ||
            p.Fingerprint.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            p.SuggestedFileName is not { Length: >= 6 and <= 124 } ||
            !p.SuggestedFileName.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(p.SuggestedFileName) != p.SuggestedFileName)
            throw new InvalidDataException("Original individual Excel preview does not match displayed history.");
    }

    private static void ValidateReceipt(
        NativeIndividualExcelReceipt r, NativeIndividualExcelPreview p)
    {
        if (r is null || r.Fingerprint != p.Fingerprint ||
            r.AttendanceRows != p.AttendanceTotal ||
            r.MovementRows != p.MovementTotal ||
            r.IdentityRows != p.IdentityTotal ||
            r.SiteRows != p.SiteTotal ||
            r.RevisionRows != p.RevisionTotal ||
            r.AuditRows != p.AuditTotal ||
            r.SheetCount != 8 ||
            r.FilePath is not { Length: >= 6 and <= 1024 } ||
            !Path.IsPathFullyQualified(r.FilePath) ||
            !r.FilePath.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Individual Excel final receipt was not authorized.");
    }

    public async Task OpenPreviewAsync(CancellationToken ct = default)
    {
        if (!CanOpen) return;
        var actor = _liveActor!.Invoke()!;
        var selection = _currentRequest!.Invoke()!;
        var report = _currentReport!.Invoke()!;
        var request = ExportRequest(selection);
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _approvedActor = actor;
        _approvedRequest = selection;
        _approvedReport = report;
        _open = _busy = true;
        _preview = null;
        _receipt = null;
        _error = "";
        Changed();
        try
        {
            var preview = await _backend!.PreviewAsync(actor, request, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (_liveActor?.Invoke() != actor ||
                _currentRequest?.Invoke() != selection ||
                !ReferenceEquals(_currentReport?.Invoke(), report))
            {
                Close(); return;
            }
            ValidatePreview(preview, request, report);
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذر اعتماد معاينة التقرير الفردي الأصلي؛ لم يكتب أي ملف.";
        }
        finally
        {
            if (version == _generation)
            {
                _busy = false;
                _pending = null;
                Changed();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm)
        {
            if (_open && _liveActor?.Invoke() != _approvedActor)
                Close();
            return;
        }
        var actor = _approvedActor!;
        var selected = _approvedRequest!;
        var shown = _approvedReport!;
        var preview = _preview!;
        var request = ExportRequest(selected);
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        Changed();
        try
        {
            var receipt = await _backend!.ExportAsync(
                actor, request, preview.Fingerprint, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (_liveActor?.Invoke() != actor ||
                _currentRequest?.Invoke() != selected ||
                !ReferenceEquals(_currentReport?.Invoke(), shown))
            {
                Close(); return;
            }
            ValidateReceipt(receipt, preview);
            _receipt = receipt;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذر إكمال التصدير أو التحقق منه؛ افحص مجلد التقارير قبل إعادة المحاولة.";
        }
        finally
        {
            if (version == _generation)
            {
                _preview = null; // Never repeat an uncertain file commit.
                _busy = false;
                _pending = null;
                Changed();
            }
        }
    }

    public void NotifySourceChanged()
    {
        if (_open && (_approvedActor != _liveActor?.Invoke() ||
            _approvedRequest != _currentRequest?.Invoke() ||
            !ReferenceEquals(_approvedReport, _currentReport?.Invoke())))
        {
            Close();
            return;
        }
        Changed();
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _approvedActor = null;
        _approvedRequest = null;
        _approvedReport = null;
        _preview = null;
        _receipt = null;
        _open = _busy = false;
        _error = "";
        Changed();
    }

    private void Changed()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen),nameof(IsBusy),nameof(Preview),nameof(Receipt),
            nameof(Error),nameof(CanConfirm),nameof(CompletionMessage)
        })
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name));
        foreach (ICommand cmd in new[] { OpenPreviewCommand,ConfirmCommand,CloseCommand })
            ((NativeCommand)cmd).NotifyAvailabilityChanged();
    }
}
