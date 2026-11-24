using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Two-step, operator-approved group-history Excel export. The VM does not
/// create files, accept a target path, or retry a possibly committed export.
/// Backend always re-reads and verifies the canonical source fingerprint.
/// </summary>
public sealed class NativePersonnelGroupExcelViewModel : INotifyPropertyChanged
{
    private INativePersonnelGroupExcelBackend? _backend;
    private Func<NativeIdentity?>? _actor;
    private Func<NativePersonnelGroupReportRequest?>? _request;
    private NativeIdentity? _approvedActor;
    private NativePersonnelGroupReportRequest? _approvedRequest;
    private NativeGroupExcelPreview? _preview;
    private NativeGroupExcelReceipt? _receipt;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy;
    private string _error = "";

    public NativePersonnelGroupExcelViewModel()
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
    public NativeGroupExcelPreview? Preview => _preview;
    public NativeGroupExcelReceipt? Receipt => _receipt;
    public string Error => _error;
    public string CompletionMessage => _receipt is null ? "" :
        $"نجح تصدير تقرير المجموعة إلى Excel ({_receipt.SheetCount} أوراق، {_receipt.DailyRows} صف يومي).";
    private bool CanOpen => _backend is not null && !_open && !_busy &&
        _actor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        ValidRequest(_request?.Invoke());
    public bool CanConfirm => _open && !_busy && _receipt is null &&
        _preview is not null && _approvedActor is { } prior &&
        _actor?.Invoke() == prior &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(prior.Role) &&
        SameRequest(_approvedRequest, _request?.Invoke());

    public void Attach(INativePersonnelGroupExcelBackend backend,
        Func<NativeIdentity?> actor,
        Func<NativePersonnelGroupReportRequest?> request)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Group Excel backend already initialized.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public async Task OpenPreviewAsync(CancellationToken ct = default)
    {
        if (!CanOpen) return;
        NativeIdentity actor = _actor!.Invoke()!;
        var request = _request!.Invoke()!;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _open = _busy = true;
        _approvedActor = actor;
        _approvedRequest = CopyRequest(request);
        _preview = null;
        _receipt = null;
        _error = "";
        Changed();
        try
        {
            var preview = await _backend!.PreviewGroupExcelAsync(actor, request, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (_actor?.Invoke() != actor || !SameRequest(request, _request?.Invoke()))
            {
                Close();
                return;
            }
            ValidatePreview(preview, request);
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _preview = null;
                _error = "تعذرت معاينة بيانات Excel الأصلية. لم تتم كتابة أي ملفات.";
            }
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
            if (_open && _actor?.Invoke() != _approvedActor) Close();
            return;
        }
        var actor = _approvedActor!;
        var request = _approvedRequest!;
        var preview = _preview!;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        Changed();
        try
        {
            var receipt = await _backend!.ExportGroupExcelAsync(
                actor, request, preview.Fingerprint, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (_actor?.Invoke() != actor || !SameRequest(request, _request?.Invoke()))
            {
                Close();
                return;
            }
            ValidateReceipt(receipt, preview);
            _receipt = receipt;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _error = "تعذر تصدير Excel أو التأكد من نتيجته؛ افحص مجلد التقارير قبل المحاولة مجددا.";
            }
        }
        finally
        {
            if (version == _generation)
            {
                _preview = null; // Never retry an uncertain commit without a new preview.
                _pending = null;
                _busy = false;
                Changed();
            }
        }
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _approvedActor = null;
        _approvedRequest = null;
        _preview = null;
        _receipt = null;
        _open = _busy = false;
        _error = "";
        Changed();
    }

    private static bool ValidRequest(NativePersonnelGroupReportRequest? request) =>
        request is { PersonIds: { Count: >= 1 and <= 100 }, SummaryPage: 1 } &&
        request.PersonIds.All(id => id is { Length: >= 3 and <= 128 } &&
            !id.Any(char.IsControl)) &&
        request.PersonIds.Distinct(StringComparer.Ordinal).Count() == request.PersonIds.Count &&
        request.MovementType is >= 0 and <= 4 &&
        request.DateFrom is not null && request.DateTo is not null &&
        request.SiteId is not null && request.StatusId is not null;

    private static NativePersonnelGroupReportRequest CopyRequest(
        NativePersonnelGroupReportRequest request) =>
        request with { PersonIds = request.PersonIds.ToArray() };

    private static bool SameRequest(
        NativePersonnelGroupReportRequest? a, NativePersonnelGroupReportRequest? b) =>
        ValidRequest(a) && ValidRequest(b) &&
        a!.PersonIds.SequenceEqual(b!.PersonIds, StringComparer.Ordinal) &&
        a.DateFrom == b.DateFrom && a.DateTo == b.DateTo &&
        a.SiteId == b.SiteId && a.StatusId == b.StatusId &&
        a.MovementType == b.MovementType && a.SummaryPage == b.SummaryPage;

    private static void ValidatePreview(
        NativeGroupExcelPreview preview, NativePersonnelGroupReportRequest request)
    {
        if (preview is null ||
            !preview.PersonIds.SequenceEqual(request.PersonIds, StringComparer.Ordinal) ||
            preview.PersonCount != request.PersonIds.Count ||
            preview.AttendanceTotal < 0 || preview.MovementTotal < 0 ||
            preview.DateFrom != request.DateFrom || preview.DateTo != request.DateTo ||
            preview.SiteId != request.SiteId || preview.StatusId != request.StatusId ||
            preview.MovementType != request.MovementType ||
            preview.Fingerprint is not { Length: 64 } ||
            preview.Fingerprint.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            preview.SuggestedFileName is not { Length: >= 6 and <= 124 } ||
            !preview.SuggestedFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(preview.SuggestedFileName) != preview.SuggestedFileName)
            throw new InvalidDataException("Untrusted canonical group Excel preview.");
    }

    private static void ValidateReceipt(
        NativeGroupExcelReceipt receipt, NativeGroupExcelPreview preview)
    {
        if (receipt is null || receipt.PersonCount != preview.PersonCount ||
            receipt.AttendanceTotal != preview.AttendanceTotal ||
            receipt.MovementTotal != preview.MovementTotal ||
            receipt.Fingerprint != preview.Fingerprint ||
            receipt.SheetCount != 4 || receipt.DailyRows < 0 ||
            receipt.FilePath is not { Length: >= 6 and <= 1024 } ||
            !Path.IsPathFullyQualified(receipt.FilePath) ||
            !receipt.FilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Group Excel receipt does not match the approved source.");
    }

    private void Changed()
    {
        foreach (var name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(Preview),
            nameof(Receipt), nameof(Error), nameof(CanConfirm),
            nameof(CompletionMessage)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand cmd in new[] { OpenPreviewCommand, ConfirmCommand, CloseCommand })
            ((NativeCommand)cmd).NotifyAvailabilityChanged();
    }
}
