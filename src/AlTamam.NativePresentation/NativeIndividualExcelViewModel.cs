using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native confirmation UI for a verified individual report. The backend owns
/// authorization, approval tokens, database access, audit and file publication.
/// </summary>
public sealed class NativeIndividualExcelViewModel : INotifyPropertyChanged
{
    private INativeIndividualExcelBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelReportSnapshot?>? _source;
    private NativeIdentity? _actor;
    private NativePersonnelReportSnapshot? _report;
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
        "تم تصدير التقرير الفردي إلى Excel (9 أوراق عمل).";
    private bool CanOpen => _backend is not null && !_open && !_busy &&
        _liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _source?.Invoke() is not null;
    private bool SameContext => _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _report is not null && ReferenceEquals(_report, _source?.Invoke());
    public bool CanConfirm => _open && !_busy && _preview is not null &&
        _receipt is null && SameContext;

    public void Attach(INativeIndividualExcelBackend backend,
        Func<NativeIdentity?> liveActor, Func<NativePersonnelReportSnapshot?> source)
    {
        if (_backend is not null) throw new InvalidOperationException("Already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Changed();
    }

    private static NativeIndividualExcelRequest Request(NativePersonnelReportSnapshot report) =>
        new(report.PersonId, report.DateFrom, report.DateTo, report.MovementType,
            report.SiteId, report.StatusId);

    public async Task OpenPreviewAsync(CancellationToken ct = default)
    {
        if (!CanOpen) return;
        var actor = _liveActor!.Invoke();
        var report = _source!.Invoke();
        if (actor is null || report is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)) return;
        int generation = ++_generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _actor = actor;
        _report = report;
        _preview = null;
        _receipt = null;
        _error = "";
        _open = _busy = true;
        Changed();
        try
        {
            var preview = await _backend!.PreviewIndividualExcelAsync(actor, Request(report), linked.Token);
            if (generation != _generation) return;
            if (linked.IsCancellationRequested || !SameContext) { Close(); return; }
            ValidatePreview(preview, report);
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (generation == _generation) Close();
        }
        catch
        {
            if (generation == _generation)
            {
                _backend!.InvalidateIndividualExcelApproval();
                _error = "تعذر اعتماد معاينة التقرير. لم يتم إنشاء ملف.";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                _busy = false;
                Changed();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm)
        {
            if (_open && !SameContext) Close();
            return;
        }
        var actor = _actor!;
        var preview = _preview!;
        int generation = ++_generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        Changed();
        try
        {
            var receipt = await _backend!.ExportIndividualExcelAsync(actor, preview.Request,
                preview.ApprovalToken, linked.Token);
            if (generation != _generation) return;
            if (linked.IsCancellationRequested || !SameContext) { Close(); return; }
            ValidateReceipt(receipt, preview);
            _receipt = receipt;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (generation == _generation)
                _error = "أُلغي الطلب؛ تحقق من مجلد التقارير قبل إعادة المحاولة.";
        }
        catch
        {
            if (generation == _generation)
                _error = "تعذر التأكد من نتيجة التصدير؛ افحص مجلد التقارير قبل إعادة المحاولة.";
        }
        finally
        {
            if (generation == _generation)
            {
                _preview = null; // A failed or completed confirmation is never retried.
                _backend!.InvalidateIndividualExcelApproval();
                _pending = null;
                _busy = false;
                Changed();
            }
        }
    }

    public void NotifySourceChanged()
    {
        if (_open && !SameContext) { Close(); return; }
        Changed();
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        if (_open) _backend?.InvalidateIndividualExcelApproval();
        _actor = null;
        _report = null;
        _preview = null;
        _receipt = null;
        _open = _busy = false;
        _error = "";
        Changed();
    }

    private static bool IsHash(string value) =>
        value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private static void ValidatePreview(NativeIndividualExcelPreview preview,
        NativePersonnelReportSnapshot report)
    {
        if (preview is null || preview.Request != Request(report) ||
            preview.Name != report.Name || preview.ServiceNumberA != report.ServiceNumberA ||
            preview.AttendanceTotal != report.AttendanceTotal ||
            preview.IdentityTotal != report.IdentityTotal ||
            preview.MovementTotal != report.MovementTotal ||
            preview.RevisionTotal != report.RevisionTotal ||
            preview.AuditTotal != report.AuditTotal || preview.SiteTotal != report.SiteTotal ||
            !IsHash(preview.Fingerprint) || !IsHash(preview.ApprovalToken) ||
            string.IsNullOrWhiteSpace(preview.Directory) ||
            !Path.IsPathFullyQualified(preview.Directory))
            throw new InvalidDataException("Untrusted individual Excel preview.");
    }

    private static void ValidateReceipt(NativeIndividualExcelReceipt receipt,
        NativeIndividualExcelPreview preview)
    {
        if (receipt is null || receipt.Request != preview.Request ||
            receipt.Fingerprint != preview.Fingerprint || receipt.SheetCount != 9 ||
            string.IsNullOrWhiteSpace(receipt.FilePath) ||
            !Path.IsPathFullyQualified(receipt.FilePath) ||
            !receipt.FilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(
                Path.GetFullPath(receipt.FilePath)) ?? ""),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(preview.Directory)),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected individual Excel receipt.");
    }

    private void Changed()
    {
        foreach (var name in new[] { nameof(IsOpen), nameof(IsBusy), nameof(Preview),
            nameof(Receipt), nameof(Error), nameof(CanConfirm), nameof(CompletionMessage) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand command in new[] { OpenPreviewCommand, ConfirmCommand, CloseCommand })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
