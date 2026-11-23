using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native WPF only initiates local user-picked XLSX upload and signs off the
/// ORIGINAL SQLCipher import plan. No file can overwrite a prior identity.
/// The underlying service owns all validation, duplicate detection and audit.
/// </summary>
public sealed class NativePersonnelExcelImportViewModel : INotifyPropertyChanged
{
    private const int MaxWorkbookSize = 160 * 1024;
    private INativePersonnelExcelImportBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _allSheets;
    private bool _personnelApproved, _rejectedReviewed, _newSitesApproved,
        _similarNamesReviewed;
    private string _error = "";
    private NativePersonnelExcelWorkbookSelection? _selection;
    private NativePersonnelExcelImportPreview? _preview;
    private NativePersonnelExcelImportReceipt? _receipt;

    public NativePersonnelExcelImportViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted && IsOriginalContextValid();
    public bool CanPickFile => CanEdit && _backend is not null;
    public bool CanPreview => CanEdit && _selection is not null;
    public bool CanConfirm => CanEdit && _preview is { RequiresExplicitConfirmation: true } &&
        _selection is not null && _personnelApproved &&
        (_preview.IssueCount == 0 || _rejectedReviewed) &&
        (_preview.NewSites.Count == 0 || _newSitesApproved) &&
        (_preview.SimilarNameCount == 0 || _similarNamesReviewed) &&
        _preview.WorkbookToken == _selection.WorkbookToken &&
        _preview.FileName == _selection.FileName;
    public bool HasSelection => _selection is not null;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool PersonnelApproved
    {
        get => _personnelApproved;
        set
        {
            if (!CanEdit || _preview is null || value == _personnelApproved) return;
            _personnelApproved = value;
            Notify();
        }
    }
    public bool RejectedRowsReviewed
    {
        get => _rejectedReviewed;
        set
        {
            if (!CanEdit || _preview is null || value == _rejectedReviewed) return;
            _rejectedReviewed = value;
            Notify();
        }
    }
    public bool NewSitesApproved
    {
        get => _newSitesApproved;
        set
        {
            if (!CanEdit || _preview is null || value == _newSitesApproved) return;
            _newSitesApproved = value;
            Notify();
        }
    }
    public bool SimilarNamesReviewed
    {
        get => _similarNamesReviewed;
        set
        {
            if (!CanEdit || _preview is null || value == _similarNamesReviewed) return;
            _similarNamesReviewed = value;
            Notify();
        }
    }
    public bool ImportAllSheets
    {
        get => _allSheets;
        set
        {
            if (!CanEdit || value == _allSheets) return;
            _allSheets = value;
            DiscardSelection(); // old signed workbook token covers different sheets.
            Notify();
        }
    }
    public string Error => _error;
    public NativePersonnelExcelWorkbookSelection? Selection => _selection;
    public NativePersonnelExcelImportPreview? Preview => _preview;
    public NativePersonnelExcelImportReceipt? Receipt => _receipt;
    public string FileDescription => _selection is null
        ? "لم يتم اختيار ملف XLSX من الجهاز."
        : $"ملف {_selection.FileName} — عدد الصفوف {_selection.RowCount} — الأوراق: " +
          string.Join("، ", _selection.SelectedWorksheets);
    public string PreviewDescription => _preview is null ? "" :
        $"الجدد: {_preview.NewCount}، المرفوضون: {_preview.IssueCount}، " +
        $"المواقع الجديدة: {_preview.NewSites.Count}، " +
        $"تشابه الأسماء: {_preview.SimilarNameCount}. " +
        (_preview.NewCount > _preview.AcceptedRows.Count
            ? $"تظهر أول {_preview.AcceptedRows.Count} حالة مقبولة فقط من أصل {_preview.NewCount}. "
            : "") +
        "لن تُستبدل هوية أو اسم أو موقع أو تحضير أي فرد موجود مسبقا.";
    public string CompletionDescription => _receipt is null ? "" :
        $"تم اعتماد دفعة الاستيراد {_receipt.ImportBatchId}: " +
        $"{_receipt.CreatedCount} فرد جديد، {_receipt.RejectedCount} صف مرفوض، " +
        $"{_receipt.CreatedSitesCount} موقع جديد. محفوظ بسجل العمليات الأصلي.";

    public void AttachBackend(INativePersonnelExcelImportBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Original Excel backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public void Open()
    {
        var actor = _liveActor?.Invoke();
        if (_open || _backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _uncertainActor == actor)
            return;
        Reset();
        _actor = actor;
        _open = true;
        Notify();
    }

    /// <summary>
    /// Only called after an explicit local Windows file-picker selection.
    /// Do not persist bytes, paths, base64, row data or credentials in WPF.
    /// The canonical stage also verifies ZIP, size, worksheets and headers.
    /// </summary>
    public async Task LoadWorkbookAsync(string filename, byte[] bytes,
        CancellationToken ct = default)
    {
        if (!CanPickFile || _backend is null) return;
        DiscardSelection();
        if (filename is null || filename.Length is < 6 or > 180 ||
            filename != Path.GetFileName(filename) ||
            !filename.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            filename.Any(char.IsControl) ||
            bytes is null || bytes.Length is < 1 or > MaxWorkbookSize)
        {
            _error = "اختر ملف Excel صحيحا بصيغة XLSX وحجم لا يتجاوز 160 كيلوبايت.";
            Notify();
            return;
        }

        var actor = _actor!;
        var generation = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        Notify();
        try
        {
            var selected = await _backend.StageWorkbookAsync(
                actor, filename, bytes, _allSheets, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidateSelection(selected, filename);
            _selection = selected;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (generation == _generation)
            {
                _selection = null;
                _error = "تعذر استيراد بنية XLSX الأصلية أو فشلت صلاحية الجلسة. لم يُحفظ أي فرد.";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                _busy = false;
                Notify();
            }
        }
    }

    public void ReportFileReadFailure()
    {
        if (!CanPickFile) return;
        DiscardSelection();
        _error = "تعذر فتح ملف Excel المحلي أو فشل التحقق من حجمه. لم تُرفع بيانات غير موثوقة.";
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null ||
            _selection is not { } selected || _actor is not { } actor)
            return;
        var generation = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        InvalidatePreview();
        _error = "";
        Notify();
        try
        {
            var preview = await _backend.PreviewWorkbookAsync(
                actor, selected, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(preview, selected);
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (generation == _generation)
            {
                InvalidatePreview();
                _error = "فشلت معاينة الكشف أو ظهرت بيانات غير موثوقة. أعد اختيار الملف قبل أي اعتماد.";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                _busy = false;
                Notify();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || _backend is null ||
            _actor is not { } actor || _preview is not { } preview)
            return;
        int generation = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // BEFORE first await of possibly durable import write.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            var result = await _backend.CommitWorkbookAsync(
                actor, preview, operationId, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (result is null ||
                result.ImportBatchId is not { Length: >= 5 and <= 128 } ||
                !result.ImportBatchId.StartsWith("IMP-", StringComparison.Ordinal) ||
                result.CreatedCount != preview.NewCount ||
                result.RejectedCount != preview.IssueCount ||
                result.CustomUpdatedCount != 0 ||
                result.CreatedSitesCount != preview.NewSites.Count ||
                !DateTimeOffset.TryParse(result.CommittedAtUtc,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Original Excel import receipt changed.");
            _receipt = result;
            InvalidatePreview();
        }
        catch
        {
            if (generation == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _receipt = null;
                InvalidatePreview();
                _error = "قد يكون الاستيراد قد اعتمد رغم فقدان الرد. راجع سجل دفعات الاستيراد والأفراد قبل أي محاولة أخرى؛ لا إعادة استيراد تلقائية.";
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _pending = null;
                _busy = false;
                Notify();
            }
        }
    }

    public void Close()
    {
        if (!_open || _busy) return;
        bool committed = _receipt is not null;
        Reset();
        if (committed) CommittedAndClosed?.Invoke();
    }

    public void ForceCancel()
    {
        if (_attempted && _receipt is null && _actor is { } actor)
            _uncertainActor = actor;
        Reset();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private void DiscardSelection()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _selection = null;
        InvalidatePreview();
        _error = "";
    }

    private void InvalidatePreview()
    {
        _preview = null;
        _personnelApproved = false;
        _rejectedReviewed = false;
        _newSitesApproved = false;
        _similarNamesReviewed = false;
    }

    private static void ValidateSelection(
        NativePersonnelExcelWorkbookSelection? selected, string filename)
    {
        if (selected is null || selected.FileName != filename ||
            selected.RowCount is < 1 or > 500 ||
            selected.WorkbookToken is not { Length: 64 } ||
            selected.WorkbookToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            selected.SelectedWorksheets is null ||
            selected.SelectedWorksheets.Count is < 1 or > 50 ||
            selected.SelectedWorksheets.Any(s =>
                s is not { Length: >= 1 and <= 100 } || s.Any(char.IsControl)))
            throw new InvalidDataException("Canonical XLSX staging token is malformed.");
    }

    private static void ValidatePreview(
        NativePersonnelExcelImportPreview? preview,
        NativePersonnelExcelWorkbookSelection selected)
    {
        if (preview is null ||
            !preview.RequiresExplicitConfirmation ||
            preview.FileName != selected.FileName ||
            preview.WorkbookToken != selected.WorkbookToken ||
            preview.PreviewToken is not { Length: 64 } ||
            preview.PreviewToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            preview.TotalCount != selected.RowCount ||
            preview.TotalCount is < 1 or > 500 ||
            preview.NewCount is < 0 or > 500 ||
            preview.IssueCount is < 0 or > 500 ||
            preview.CustomUpdateCount != 0 || // NO existing record mutations in WPF.
            preview.NewCount + preview.IssueCount != preview.TotalCount ||
            preview.SimilarNameCount is < 0 or > 10000 ||
            preview.AcceptedRows is null ||
            preview.AcceptedRows.Count != Math.Min(preview.NewCount, 100) ||
            preview.Issues is null ||
            preview.Issues.Count != preview.IssueCount ||
            preview.NewSites is null || preview.NewSites.Count > 500 ||
            preview.NewSites.Any(site =>
                site is null ||
                site.Name is not { Length: >= 2 and <= 100 } ||
                site.Name.Any(char.IsControl) ||
                site.PersonCount is < 1 or > 500) ||
            preview.NewSites.Sum(site => site.PersonCount) > preview.NewCount ||
            preview.AcceptedRows.Any(row =>
                row is null ||
                row.Worksheet is not { Length: >= 1 and <= 100 } ||
                row.Row < 2 ||
                row.Name is not { Length: >= 3 and <= 180 } ||
                row.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                row.ServiceNumberA.Any(c => c is < '0' or > '9') ||
                row.SiteName is not { Length: >= 2 and <= 100 }) ||
            preview.AcceptedRows.Select(x => x.ServiceNumberA)
                .Distinct(StringComparer.Ordinal).Count() !=
                preview.AcceptedRows.Count ||
            preview.Issues.Any(issue =>
                issue is null || issue.Row < 1 ||
                issue.Reason is not { Length: >= 1 and <= 1000 }))
            throw new InvalidDataException("XLSX preview is inconsistent or permits changes.");
    }

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _allSheets = false;
        _actor = null;
        _selection = null;
        _preview = null;
        _receipt = null;
        _error = "";
        InvalidatePreview();
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit),
            nameof(CanPickFile), nameof(CanPreview), nameof(CanConfirm),
            nameof(HasSelection), nameof(HasPreview),
            nameof(HasUnverifiedCommit), nameof(ImportAllSheets),
            nameof(PersonnelApproved), nameof(RejectedRowsReviewed),
            nameof(NewSitesApproved), nameof(SimilarNamesReviewed),
            nameof(Error), nameof(Selection), nameof(Preview),
            nameof(Receipt), nameof(FileDescription),
            nameof(PreviewDescription), nameof(CompletionDescription)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
