using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Bounded UI intent for the ORIGINAL MonthlyExcelExportService. This is not
/// an Excel renderer and never chooses user paths, writes attendance, or
/// retries a partially failed export. A human must preview, then confirm.
/// </summary>
public sealed class NativeMonthlyExcelExportViewModel : INotifyPropertyChanged
{
    private INativeMonthlyExcelExportBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeMonthlyExcelFilter?>? _currentFilter;
    private CancellationTokenSource? _pending;
    private NativeIdentity? _previewActor;
    private NativeMonthlyExcelFilter? _previewFilter;
    private int _generation;
    private bool _open, _busy, _exporting, _cancelRequested;
    private int _progressPercent;
    private bool _hasProgress;
    private string _error = "", _outputDirectory = "", _outputFileName = "";
    private NativeMonthlyExcelPreview? _preview;
    private NativeMonthlyExcelReceipt? _receipt;

    public NativeMonthlyExcelExportViewModel()
    {
        OpenPreviewCommand = new NativeCommand(_ => _ = OpenPreviewAsync(),
            _ => CanOpen);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(),
            _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsExporting);
        CancelExportCommand = new NativeCommand(_ => RequestCancel(), _ => IsExporting && !IsCancelRequested);
    }

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool IsExporting => _exporting;
    public bool IsCancelRequested => _cancelRequested;
    public int ProgressPercent => _progressPercent;
    public bool IsProgressIndeterminate => !_hasProgress;
    public string ProgressMessage => !_exporting ? "" : _cancelRequested
        ? "طلب إيقاف التصدير؛ جار التحقق من النتيجة."
        : _hasProgress ? $"جار إنشاء ملفات Excel — {_progressPercent}%" : "جار إعداد ملفات Excel...";
    public ICommand CancelExportCommand { get; }

    private void RequestCancel()
    {
        if (!_exporting || _cancelRequested) return;
        _cancelRequested = true;
        _pending?.Cancel();
        Notify();
    }
    public bool HasDestinationReview => _preview?.OutputDirectory is not null;
    public bool CanEditDestination => HasDestinationReview && _open && !_busy && _receipt is null &&
        _currentFilter?.Invoke() == _previewFilter && _liveActor?.Invoke() == _previewActor;
    public string OutputDirectory => _outputDirectory;
    public string OutputFileName
    {
        get => _outputFileName;
        set
        {
            if(!CanEditDestination || value==_outputFileName)return;
            _outputFileName=value??"";
            Notify();
        }
    }
    public bool TrySelectOutputDirectory(string? folder)
    {
        if(!CanEditDestination || !ValidLocalPath(folder))return false;
        _outputDirectory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder!));
        Notify();
        return true;
    }
    private static bool ValidLocalPath(string? folder)
    {
        if(string.IsNullOrWhiteSpace(folder) || folder.Length>600 || folder.Any(char.IsControl) ||
           folder.StartsWith(@"\\",StringComparison.Ordinal) || folder.StartsWith("//",StringComparison.Ordinal))
            return false;
        try{return Path.IsPathFullyQualified(folder);}
        catch(ArgumentException){return false;}
    }
    private bool ValidDestination => !HasDestinationReview ||
        (ValidLocalPath(_outputDirectory) &&
         MonthlyExcelOutputNamePolicy.TryNormalize(_outputFileName,out var name) && name.Length>0);
    public bool CanConfirm => _open && !_busy && _preview is { PersonCount: > 0 } &&
        _receipt is null && ValidDestination && _currentFilter?.Invoke() == _previewFilter &&
        _liveActor?.Invoke() == _previewActor;
    public NativeMonthlyExcelPreview? Preview => _preview;
    public NativeMonthlyExcelReceipt? Receipt => _receipt;
    public string Error => _error;
    public string PeriodLabel => _preview?.Period ?? "";
    public string CompletionMessage => _receipt is null ? "" :
        $"نجح تصدير {_receipt.FileCount} ملف Excel؛ عدد الأفراد في الكشف الموحد: {_receipt.PersonCount}.";
    public ICommand OpenPreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    private bool CanOpen => !_open && !_busy && _backend is not null &&
        _liveActor?.Invoke() is not null && _currentFilter?.Invoke() is not null;

    public void Attach(INativeMonthlyExcelExportBackend backend,
        Func<NativeIdentity?> liveActor, Func<NativeMonthlyExcelFilter?> currentFilter)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Monthly Excel export backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _currentFilter = currentFilter ?? throw new ArgumentNullException(nameof(currentFilter));
        Notify();
    }

    public void NotifyContextChanged() => Notify();

    public async Task OpenPreviewAsync(CancellationToken ct = default)
    {
        if (!CanOpen || _backend is null ||
            _liveActor?.Invoke() is not { } actor ||
            _currentFilter?.Invoke() is not { } filter)
            return;
        if (!DateOnly.TryParseExact(filter.Period + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            start.Year is < 1900 or > 2199 ||
            start > new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1) ||
            filter.SiteId is null || filter.SiteId.Length > 128 ||
            filter.SiteId.Any(char.IsControl) ||
            filter.SearchTerm is null || filter.SearchTerm.Length > 100 ||
            filter.SearchTerm.Any(char.IsControl))
            return;

        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _open = true;
        _busy = true;
        _error = "";
        _preview = null;
        _receipt = null;
        _progressPercent = 0;
        _hasProgress = false;
        _cancelRequested = false;
        _previewActor = actor;
        _previewFilter = filter;
        Notify();
        try
        {
            var result = await _backend.PreviewExcelAsync(actor, filter, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (_liveActor?.Invoke() != actor || _currentFilter?.Invoke() != filter)
            {
                Close();
                return;
            }
            ValidatePreview(result, filter);
            _preview = result;
            _outputDirectory = ValidLocalPath(result.OutputDirectory)?result.OutputDirectory!:"";
            _outputFileName = result.SuggestedFileName;
            if (result.PersonCount == 0)
                _error = "لا توجد أسماء تطابق هذا الشهر والموقع والبحث لتصديرها.";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذرت معاينة كشف Excel المعتمد؛ لا يمكن بدء التصدير.";
        }
        finally
        {
            if (version == _generation)
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
            _previewFilter is not { } filter ||
            _previewActor is not { } actor)
            return;

        var request=filter;
        if(HasDestinationReview)
        {
            if(!MonthlyExcelOutputNamePolicy.TryNormalize(_outputFileName,out var name) || name.Length==0 ||
               !ValidLocalPath(_outputDirectory))return;
            request=filter with { OutputDirectory=_outputDirectory,OutputFileName=name };
        }
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _exporting = true;
        _cancelRequested = false;
        _hasProgress = false;
        _progressPercent = 0;
        _error = "";
        Notify();
        var context = SynchronizationContext.Current;
        void Report(int processed,int total)
        {
            void Apply()
            {
                if (version != _generation || !_exporting || _liveActor?.Invoke() != actor ||
                    _currentFilter?.Invoke() != filter || total <= 0 || processed < 0 || processed > total) return;
                _hasProgress = true;
                _progressPercent = Math.Max(_progressPercent, Math.Min(99, (int)((long)processed * 100 / total)));
                Notify();
            }
            if (context is not null && SynchronizationContext.Current != context)
                context.Post(_ => Apply(), null);
            else Apply();
        }
        try
        {
            // Exactly one user-initiated write. The original service controls
            // the validated local directory, atomic .part commit, names and
            // cleanup after interruption. We do not auto-retry failures.
            var result = _backend is INativeMonthlyExcelProgressBackend progressBackend
                ? await progressBackend.ExportWithProgressAsync(actor, request, Report, linked.Token)
                : await _backend.ExportExcelAsync(actor, request, linked.Token);
            if (version != _generation) return;
            if (_liveActor?.Invoke() != actor || _currentFilter?.Invoke() != filter)
            {
                Close();
                return;
            }
            ValidateReceipt(result, request);
            _receipt = result;
            _hasProgress = true;
            _progressPercent = 100;
            _preview = null; // the same preview cannot authorize another write
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (version == _generation)
            {
                _preview = null;
                _error = "توقف طلب التصدير. تحقق من مجلد التقارير قبل إعادة المحاولة؛ لم يتم تأكيد اكتمال الملفات.";
            }
        }
        catch
        {
            if (version == _generation)
            {
                _preview = null; // force a fresh preview before any retry
                _error = "تعذر إكمال تصدير Excel أو التحقق من نتيجته. راجع مجلد التقارير المعتمد قبل إعادة المحاولة.";
            }
        }
        finally
        {
            if (version == _generation)
            {
                _pending = null;
                _busy = false;
                _exporting = false;
                Notify();
            }
        }
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _preview = null;
        _receipt = null;
        _previewActor = null;
        _previewFilter = null;
        _error = "";
        _open = false;
        _busy = false;
        _exporting = false;
        _cancelRequested = false;
        _hasProgress = false;
        _progressPercent = 0;
        _outputDirectory = _outputFileName = "";
        Notify();
    }

    private static void ValidatePreview(NativeMonthlyExcelPreview preview,
        NativeMonthlyExcelFilter filter)
    {
        int year = int.Parse(filter.Period[..4], CultureInfo.InvariantCulture);
        int month = int.Parse(filter.Period[5..7], CultureInfo.InvariantCulture);
        if (preview.Period != filter.Period ||
            preview.SiteId != filter.SiteId ||
            preview.SearchTerm != filter.SearchTerm ||
            preview.PersonCount is < 0 or > 100000 ||
            preview.DaysInMonth != DateTime.DaysInMonth(year, month) ||
            string.IsNullOrWhiteSpace(preview.SiteName) ||
            preview.SuggestedFileName.Length is < 6 or > 124 ||
            !preview.SuggestedFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(preview.SuggestedFileName) != preview.SuggestedFileName)
            throw new InvalidDataException("Forged monthly export preview.");
    }

    private static void ValidateReceipt(NativeMonthlyExcelReceipt receipt,
        NativeMonthlyExcelFilter filter)
    {
        if (receipt.Period != filter.Period ||
            receipt.SiteId != filter.SiteId || receipt.SearchTerm != filter.SearchTerm ||
            receipt.PersonCount is < 1 or > 100000 ||
            receipt.FileCount is < 1 or > 10001 ||
            string.IsNullOrWhiteSpace(receipt.PrimaryFilePath) ||
            !receipt.PrimaryFilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Forged monthly Excel export receipt.");
        if(filter.OutputDirectory is { } folder && filter.OutputFileName is { } name &&
           !string.Equals(Path.GetFullPath(receipt.PrimaryFilePath),Path.GetFullPath(Path.Combine(folder,name)),
               OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new InvalidDataException("Monthly Excel receipt destination mismatch.");
    }

    private void Notify()
    {
        foreach (string prop in new[] { nameof(IsOpen), nameof(IsBusy),
                     nameof(CanConfirm), nameof(Preview), nameof(Receipt),
                     nameof(Error), nameof(PeriodLabel),
                     nameof(CompletionMessage), nameof(IsExporting), nameof(IsCancelRequested),
                     nameof(ProgressPercent), nameof(IsProgressIndeterminate), nameof(ProgressMessage),
                     nameof(HasDestinationReview),nameof(CanEditDestination),nameof(OutputDirectory),nameof(OutputFileName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        ((NativeCommand)OpenPreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CancelExportCommand).NotifyAvailabilityChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
