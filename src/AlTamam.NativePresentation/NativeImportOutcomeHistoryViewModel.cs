using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Recent Excel import batches owned by the live administrator, read only.
/// A batch is selected only from the current canonical list; stale results
/// after refresh, logout or a role change are discarded without caching.
/// </summary>
public sealed class NativeImportOutcomeHistoryViewModel : INotifyPropertyChanged
{
    private INativeImportOutcomeHistoryBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending, _detailPending, _exportPending;
    private int _generation, _detailGeneration, _exportGeneration;
    private bool _open, _busy, _detailBusy, _exportBusy, _exportAttempted, _approvedIssues;
    private string _error = "", _detailError = "", _exportError = "";
    private IReadOnlyList<NativeImportBatchRow> _batches = [];
    private NativeImportBatchDetail? _detail;
    private NativeImportIssuesExportReceipt? _exportReceipt;

    public NativeImportOutcomeHistoryViewModel()
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead && !_busy);
        DetailsCommand = new NativeCommand(x =>
        {
            if (x is NativeImportBatchRow row) _ = OpenDetailsAsync(row);
        }, x => CanRead && !_busy && !_detailBusy &&
                 x is NativeImportBatchRow row && _batches.Any(b => ReferenceEquals(b, row)));
        CloseDetailsCommand = new NativeCommand(_ => CloseDetails(), _ => _open && (_detailBusy || _detail is not null));
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
        ExportIssuesCommand = new NativeCommand(_ => _ = ExportIssuesAsync(),
            _ => CanExport);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public bool IsDetailLoading => _detailBusy;
    public string LoadError => _error;
    public string DetailError => _detailError;
    public IReadOnlyList<NativeImportBatchRow> Batches => _batches;
    public NativeImportBatchDetail? SelectedDetail => _detail;
    public bool HasDetails => _detail is not null;
    public bool IsExportBusy => _exportBusy;
    public NativeImportIssuesExportReceipt? ExportReceipt => _exportReceipt;
    public string ExportError => _exportError;
    public bool HasUnverifiedExport => _actor is { } actor && _uncertainActor == actor;
    public string ExportStateSummary => HasUnverifiedExport
        ? "قد يكون تقرير Excel قد حُفظ رغم فقدان الرد. افحص مجلد التقارير المعتمد يدويا قبل أي محاولة جديدة، ولا تعِد التصدير تلقائيا."
        : _exportReceipt is { } receipt
            ? $"تم حفظ تقرير {_exportReceipt.FileName} في {receipt.FilePath}."
            : "يُنشأ تقرير أخطاء Excel فقط بعد مراجعة الصفوف المرفوضة والموافقة صراحة.";
    public bool AcknowledgeSavedIssues
    {
        get => _approvedIssues;
        set
        {
            if (!CanRead || _busy || _detailBusy || _exportBusy ||
                _exportAttempted || _detail is null) return;
            if (_approvedIssues == value) return;
            _approvedIssues = value;
            Changed();
        }
    }
    public bool CanExport => CanRead && !_busy && !_detailBusy && !_exportBusy &&
        !_exportAttempted && !HasUnverifiedExport && _approvedIssues &&
        _detail is { Issues.Count: > 0 } && _detail.Batch.RejectedRows == _detail.Issues.Count;
    public ICommand RefreshCommand { get; }
    public ICommand DetailsCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand ExportIssuesCommand { get; }

    private bool CanRead => _open && _backend is not null &&
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    public void AttachBackend(INativeImportOutcomeHistoryBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null) throw new InvalidOperationException("History backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (_open || _backend is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)) return;
        Close();
        _actor = actor;
        _open = true;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CanRead || _actor is not { } actor || _backend is null)
        {
            Close();
            return;
        }
        int version = ++_generation;
        _pending?.Cancel();
        CloseDetails();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _batches = [];
        _error = "";
        Changed();
        try
        {
            IReadOnlyList<NativeImportBatchRow> result =
                await _backend.ReadRecentAsync(actor, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            ValidateList(result);
            _batches = result.ToArray();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _batches = [];
                _error = "تعذر التحقق من سجل دفعات Excel؛ أُخفيت البيانات السابقة.";
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

    public async Task OpenDetailsAsync(NativeImportBatchRow? row,
        CancellationToken ct = default)
    {
        if (!CanRead || _backend is null || _actor is not { } actor ||
            row is null || _busy || _exportBusy || !_batches.Any(b => ReferenceEquals(b, row)))
            return;
        // A different selected batch revokes any earlier export authorization.
        RevokeExportState();
        int version = ++_detailGeneration;
        _detailPending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _detailPending = linked;
        _detail = null;
        _detailError = "";
        _detailBusy = true;
        Changed();
        try
        {
            NativeImportBatchDetail detail =
                await _backend.ReadBatchAsync(actor, row.ImportBatchId, linked.Token);
            if (version != _detailGeneration || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            if (!_batches.Any(b => ReferenceEquals(b, row)) || detail is null ||
                detail.Batch != row || detail.Issues is null ||
                detail.Issues.Count != row.RejectedRows ||
                detail.Issues.Count > 500 ||
                detail.Issues.Any(x => x is null || x.Row < 1 ||
                    x.Reason is not { Length: >= 1 and <= 500 } ||
                    x.Name is null || x.SiteName is null || x.ServiceNumberA is null))
                throw new InvalidDataException("Forged or incomplete import issues.");
            _detail = detail;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _detailGeneration)
                _detailError = "تعذرت مطابقة تفاصيل الدفعة مع سجل الاستيراد المحفوظ.";
        }
        finally
        {
            if (version == _detailGeneration)
            {
                _detailPending = null;
                _detailBusy = false;
                Changed();
            }
        }
    }

    /// <summary>
    /// One explicit attempt at the original owner-scoped XLSX issues writer.
    /// No filename or directory can be supplied by the native UI.
    /// On lost/forged replies the active actor cannot retry this session.
    /// </summary>
    public async Task ExportIssuesAsync(CancellationToken ct = default)
    {
        if (!CanExport || _backend is null || _actor is not { } actor ||
            _detail is not { } detail) return;
        int version = ++_exportGeneration;
        _exportPending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _exportPending = linked;
        _exportAttempted = true; // Before invoking the potentially committed file writer.
        _exportBusy = true;
        _approvedIssues = false;
        _exportError = "";
        _exportReceipt = null;
        Changed();
        try
        {
            NativeImportIssuesExportReceipt result = await _backend.ExportIssuesAsync(
                actor, detail.Batch.ImportBatchId, linked.Token);
            if (version != _exportGeneration || linked.IsCancellationRequested)
            {
                _uncertainActor = actor;
                return;
            }
            if (!CanRead || _detail != detail)
            {
                _uncertainActor = actor;
                Close();
                return;
            }
            if (result is null ||
                result.ImportBatchId != detail.Batch.ImportBatchId ||
                result.IssueCount != detail.Issues.Count ||
                result.Format != "xlsx" ||
                result.FileName is not { Length: >= 6 and <= 180 } ||
                !result.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                result.FileName.Any(char.IsControl) ||
                result.FilePath is not { Length: >= 8 and <= 2048 } ||
                !Path.IsPathFullyQualified(result.FilePath) ||
                !string.Equals(Path.GetFileName(result.FilePath), result.FileName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original issue export receipt mismatched.");
            _exportReceipt = result;
        }
        catch
        {
            if (version == _exportGeneration)
            {
                _uncertainActor = actor;
                _exportReceipt = null;
                _exportError = "نتيجة تصدير الأخطاء غير مؤكدة؛ تحقق من مجلد التقارير المحفوظ قبل أي محاولة أخرى.";
            }
        }
        finally
        {
            if (version == _exportGeneration)
            {
                _exportPending = null;
                _exportBusy = false;
                Changed();
            }
        }
    }

    private void RevokeExportState()
    {
        if (_exportAttempted && _exportReceipt is null && _actor is { } actor)
            _uncertainActor = actor;
        ++_exportGeneration;
        _exportPending?.Cancel();
        _exportPending = null;
        _exportBusy = false;
        _exportAttempted = false;
        _approvedIssues = false;
        _exportReceipt = null;
        _exportError = "";
    }

    public void CloseDetails()
    {
        RevokeExportState();
        ++_detailGeneration;
        _detailPending?.Cancel();
        _detailPending = null;
        _detail = null;
        _detailError = "";
        _detailBusy = false;
        Changed();
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        CloseDetails();
        _actor = null;
        _open = false;
        _busy = false;
        _batches = [];
        _error = "";
        Changed();
    }

    private static void ValidateList(IReadOnlyList<NativeImportBatchRow> batches)
    {
        if (batches is null || batches.Count > 30 ||
            batches.Any(x => x is null || x.ImportBatchId is not { Length: >= 5 and <= 128 } ||
                !x.ImportBatchId.StartsWith("IMP-", StringComparison.Ordinal) ||
                x.FileName is not { Length: >= 1 and <= 180 } ||
                x.Status != "COMMITTED" ||
                x.TotalRows is < 1 or > 500 ||
                x.AcceptedRows is < 0 or > 500 ||
                x.RejectedRows is < 0 or > 500 ||
                x.AcceptedRows + x.RejectedRows != x.TotalRows ||
                !DateTimeOffset.TryParse(x.CommittedAtUtc, out _)) ||
            batches.Select(x => x.ImportBatchId)
                .Distinct(StringComparer.Ordinal).Count() != batches.Count)
            throw new InvalidDataException("Malformed original import history.");
    }

    private void Changed()
    {
        foreach (string property in new[]
        {
            nameof(IsOpen), nameof(IsLoading), nameof(IsDetailLoading),
            nameof(LoadError), nameof(DetailError), nameof(Batches),
            nameof(SelectedDetail), nameof(HasDetails),
            nameof(IsExportBusy), nameof(ExportReceipt), nameof(ExportError),
            nameof(HasUnverifiedExport), nameof(ExportStateSummary),
            nameof(AcknowledgeSavedIssues), nameof(CanExport)
        })
            PropertyChanged?.Invoke(this, new(property));
        foreach (ICommand command in new[]
        { RefreshCommand, DetailsCommand, CloseDetailsCommand, CloseCommand, ExportIssuesCommand })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
