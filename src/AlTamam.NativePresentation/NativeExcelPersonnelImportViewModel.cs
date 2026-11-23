using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Additive native XLSX import. Original SQLCipher service owns ALL parsing,
/// duplicate rejection, site creation and atomic audit. Max 100 visible
/// additions here, so every new person can be reviewed before commitment.
/// </summary>
public sealed class NativeExcelPersonnelImportViewModel : INotifyPropertyChanged
{
    private INativeExcelPersonnelImportBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain;
    private bool _reviewedPeople, _approvedSites, _reviewedSimilarNames;
    private bool _allSheets;
    private string _error = "";
    private NativeExcelImportReview? _review;
    private NativeExcelImportReceipt? _receipt;

    public NativeExcelPersonnelImportViewModel()
    {
        CommitCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanCommit);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanSelect => _open && !_busy && !_attempted &&
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _backend is not null;
    public bool HasReview => _review is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public NativeExcelImportReview? Review => _review;
    public NativeExcelImportReceipt? Receipt => _receipt;
    public string Error => _error;

    /// <summary>
    /// False by default: first worksheet only. Switching modes invalidates
    /// the original actor-bound signed review AND every prior approval.
    /// </summary>
    public bool ImportAllSheets
    {
        get => _allSheets;
        set
        {
            if (!CanSelect || _allSheets == value) return;
            _allSheets = value;
            ++_generation;
            _pending?.Cancel();
            _pending = null;
            _review = null;
            _receipt = null;
            _reviewedPeople = false;
            _approvedSites = false;
            _reviewedSimilarNames = false;
            _error = "";
            Notify();
        }
    }

    public bool ReviewedPeopleAndIssues
    {
        get => _reviewedPeople;
        set
        {
            if (!CanSelect || _review is null || _reviewedPeople == value) return;
            _reviewedPeople = value;
            Notify();
        }
    }
    public bool ApproveCreatingSites
    {
        get => _approvedSites;
        set
        {
            if (!CanSelect || _review is null || _approvedSites == value) return;
            _approvedSites = value;
            Notify();
        }
    }
    public bool AcknowledgeSimilarNames
    {
        get => _reviewedSimilarNames;
        set
        {
            if (!CanSelect || _review is null || _reviewedSimilarNames == value) return;
            _reviewedSimilarNames = value;
            Notify();
        }
    }
    public bool CanCommit => CanSelect && _review is { NewCount: > 0 } &&
        _review.CustomUpdateCount == 0 &&
        _review.NewPeople.Count == _review.NewCount &&
        _reviewedPeople &&
        (_review.NewSites.Count == 0 || _approvedSites) &&
        (_review.SimilarNameCount == 0 || _reviewedSimilarNames);
    public string ReviewSummary => _review is not { } r ? "" :
        $"ملف «{r.FileName}»: {r.TotalRows} صفا. جديد: {r.NewCount}، " +
        $"مرفوض: {r.RejectedCount}، مواقع جديدة: {r.NewSites.Count}، " +
        $"أسماء متشابهة تستوجب المراجعة: {r.SimilarNameCount}. " +
        $"نطاق أوراق العمل: {(_allSheets ? "جميع الأوراق" : "الورقة الأولى")}. " +
        "لا يتغير أي فرد موجود أو تحضير أو انتساب تاريخي. " +
        "تُنشأ المواقع الجديدة فقط إذا وافقت عليها صراحة.";
    public string ReceiptSummary => _receipt is not { } r ? "" :
        $"تم اعتماد دفعة {r.ImportBatchId}: أضيف {r.CreatedCount} فرد جديد، " +
        $"أُنشئ {r.CreatedSitesCount} موقع، وحُفظ تقرير {r.RejectedCount} صف مرفوض. " +
        "لم تُعدّل بيانات فرد موجود.";

    public ICommand CommitCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativeExcelPersonnelImportBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Native import backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public void Open()
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_open || _backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _uncertainActor == actor)
            return;
        Reset();
        _actor = actor;
        _open = true;
        Notify();
    }

    public async Task LoadWorkbookAsync(string fileName, byte[] workbook,
        CancellationToken ct = default)
    {
        if (!CanSelect) return;
        // Invalidate any earlier approved preview FIRST, even if this new file
        // is malformed. Never reuse an obsolete review or signed one-use token.
        int version = ++_generation;
        _pending?.Cancel();
        _review = null;
        _receipt = null;
        _reviewedPeople = false;
        _approvedSites = false;
        _reviewedSimilarNames = false;
        _error = "";
        Notify();
        if (fileName is not { Length: >= 6 and <= 180 } ||
            !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            fileName.Any(char.IsControl) ||
            workbook is not { Length: >= 1 and <= 160 * 1024 })
        {
            _error = "اختر كشف Excel بصيغة XLSX حتى 160 كيلوبايت. لا تُستبدل أي بيانات.";
            Notify();
            return;
        }
        if (_actor is not { } actor || _backend is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        Notify();
        try
        {
            NativeExcelImportReview preview = await _backend.StageAndPreviewAsync(
                actor, fileName, workbook, _allSheets, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanReadOriginalActor(actor)) { ForceCancel(); return; }
            // Original previews display at most 100 accepted people. Never
            // authorize the undisplayed 101st...500th even when allSheets
            // is selected. The operator must divide the XLSX into safe batches.
            if (preview is { NewCount: > 100 })
            {
                _error = "الكشف يحتوي على أكثر من 100 فرد جديد مقبول. " +
                    "لم يُحفظ أي تغيير؛ قسّم الكشف إلى دفعات من 100 فرد " +
                    "حتى تتمكن من مراجعة هوية كل فرد قبل الاعتماد.";
                return;
            }
            ValidateCanonicalReview(preview, fileName);
            _review = preview;
            if (preview.NewCount == 0)
                _error = "لا يوجد أفراد جدد صالحون للإضافة. راجع أسباب الرفض؛ لم تُكتب بيانات.";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _review = null;
                _error = "فشلت معاينة ملف XLSX الأصلي أو يوجد تعارض أو خطأ فيه. لم تُحفظ أي تغييرات.";
            }
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
        if (!CanCommit || _backend is null ||
            _actor is not { } actor || _review is not { } review)
            return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // BEFORE first awaited original SQLCipher transaction.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            var outcome = await _backend.CommitAsync(
                actor, review, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !CanReadOriginalActor(actor))
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (outcome is null ||
                outcome.ImportBatchId is not { Length: >= 5 and <= 128 } ||
                !outcome.ImportBatchId.StartsWith("IMP-", StringComparison.Ordinal) ||
                outcome.CreatedCount != review.NewCount ||
                outcome.RejectedCount != review.RejectedCount ||
                outcome.CreatedSitesCount != review.NewSites.Count ||
                outcome.CustomUpdatedCount != 0 ||
                !DateTimeOffset.TryParse(outcome.CommittedAtUtc, out _))
                throw new InvalidDataException("Original XLSX import receipt mismatch.");
            _receipt = outcome;
            _review = null;
            _reviewedPeople = false;
            _approvedSites = false;
            _reviewedSimilarNames = false;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _receipt = null;
                _review = null;
                _reviewedPeople = false;
                _approvedSites = false;
                _reviewedSimilarNames = false;
                _error = "قد تكون دفعة Excel قد حُفظت رغم فقدان الرد. تحقق من سجل دفعات الاستيراد والأفراد والمواقع قبل إعادة استيراد الملف؛ لا إعادة تنفيذ تلقائية.";
            }
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

    public void Close()
    {
        if (!_open || _busy) return;
        bool success = _receipt is not null;
        Reset();
        if (success) CommittedAndClosed?.Invoke();
    }

    public void ForceCancel()
    {
        if (_attempted && _receipt is null && _actor is { } actor)
            _uncertainActor = actor;
        Reset();
    }

    private bool CanReadOriginalActor(NativeIdentity actor) =>
        _open && _actor == actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private static void ValidateCanonicalReview(
        NativeExcelImportReview review, string requestedFile)
    {
        if (review is null || review.FileName != requestedFile ||
            review.Sha256 is not { Length: 64 } ||
            review.Sha256.Any(c => !char.IsAsciiHexDigit(c)) ||
            review.PreviewToken is not { Length: 64 } ||
            review.PreviewToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            review.TotalRows is < 1 or > 500 ||
            review.NewCount is < 0 or > 100 ||
            review.RejectedCount < 0 || review.CustomUpdateCount != 0 ||
            review.NewCount + review.RejectedCount != review.TotalRows ||
            review.NewPeople is null || review.Issues is null ||
            review.NewSites is null || review.Collisions is null ||
            review.Collisions.Count != review.SimilarNameCount ||
            review.Collisions.Count > 40 ||
            review.NewPeople.Count != review.NewCount ||
            review.Issues.Count != review.RejectedCount ||
            review.NewSites.Count > 100 ||
            review.SimilarNameCount is < 0 or > 500 ||
            review.NewPeople.Any(p => p is null ||
                p.Row < 2 ||
                p.Worksheet is not { Length: >= 1 and <= 100 } ||
                p.Name is not { Length: >= 3 and <= 180 } ||
                p.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                p.SiteName is not { Length: >= 2 and <= 100 }) ||
            review.NewPeople.Select(p => p.ServiceNumberA)
                .Distinct(StringComparer.Ordinal).Count() != review.NewPeople.Count ||
            review.Issues.Any(x => x is null || x.Row < 1 ||
                string.IsNullOrWhiteSpace(x.Reason)) ||
            review.NewSites.Any(x => x is null ||
                x.Name is not { Length: >= 2 and <= 100 } ||
                x.PersonCount is < 1 or > 100))
            throw new InvalidDataException("Invalid, abbreviated or write-enabled canonical import preview.");
    }

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _reviewedPeople = false;
        _approvedSites = false;
        _reviewedSimilarNames = false;
        _allSheets = false;
        _review = null;
        _receipt = null;
        _error = "";
        Notify();
    }

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanSelect),
            nameof(CanCommit), nameof(HasReview),
            nameof(HasUnverifiedCommit), nameof(Review), nameof(Receipt),
            nameof(ReviewedPeopleAndIssues), nameof(ApproveCreatingSites),
            nameof(AcknowledgeSimilarNames), nameof(ImportAllSheets),
            nameof(ReviewSummary),
            nameof(ReceiptSummary), nameof(Error)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((NativeCommand)CommitCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
