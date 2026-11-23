using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// External exit of 1–100 explicitly ticked CURRENT-page personnel only.
/// Original PersonnelMovementService atomically deactivates exited persons
/// AND suspends linked accounts/revokes sessions. Two independent unchecked
/// approvals are mandatory; a lost acknowledgement never causes retry.
/// </summary>
public sealed class NativeBulkExternalTransferViewModel : INotifyPropertyChanged
{
    private INativeBulkExternalTransferBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain;
    private bool _identityReviewed, _accountAcknowledged;
    private string _sourceSiteId = "", _sourceSiteName = "";
    private string _externalDestination = "", _error = "";
    private IReadOnlyList<NativeBulkTransferOption> _options = [];
    private NativeBulkExternalTransferDraft? _approvedDraft;
    private IReadOnlyList<NativeBulkInternalPerson>? _approvedPeople;
    private NativeBulkExternalTransferPreview? _preview;
    private NativeBulkExternalTransferReceipt? _receipt;

    public NativeBulkExternalTransferViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !_busy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted &&
        IsOriginalContextValid();
    public bool CanPreview => CanEdit && _backend is not null &&
        CurrentDraft() is not null;
    public bool CanConfirm => CanEdit && _identityReviewed &&
        _accountAcknowledged && _preview is not null &&
        _approvedDraft is not null && _approvedPeople is not null &&
        CurrentDraft() is { } draft &&
        SameDraft(draft, _approvedDraft) &&
        CurrentPeople().SequenceEqual(_approvedPeople);
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public int SelectedCount => _options.Count(x => x.IsSelected);
    public string SourceSiteName => _sourceSiteName;
    public string SourceSiteId => _sourceSiteId;
    public string ExternalDestinationName
    {
        get => _externalDestination;
        set
        {
            if (!CanEdit || _externalDestination == value) return;
            _externalDestination = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }
    public bool IdentityReviewed
    {
        get => _identityReviewed;
        set
        {
            if (!CanEdit || _preview is null || _identityReviewed == value)
                return;
            _identityReviewed = value;
            Notify();
        }
    }
    public bool AccountSuspensionAcknowledged
    {
        get => _accountAcknowledged;
        set
        {
            if (!CanEdit || _preview is null || _accountAcknowledged == value)
                return;
            _accountAcknowledged = value;
            Notify();
        }
    }
    public string Error => _error;
    public NativeBulkExternalTransferPreview? Preview => _preview;
    public NativeBulkExternalTransferReceipt? Receipt => _receipt;
    public IReadOnlyList<NativeBulkTransferOption> Options => _options;
    public string ReviewMessage => _preview is null ? "" :
        $"خروج خارجي لعدد {_preview.Persons.Count} فردا محددين بالمعرف والرقم (ع)، " +
        $"من «{_sourceSiteName}» إلى الجهة «{_preview.DestinationName}» بتاريخ {_preview.BusinessDate}. " +
        "ستُعلّق الحسابات المرتبطة بالأفراد المنقولين خارجيا وتُلغى جلساتها النشطة، " +
        "ويُحفظ تحضيرهم وحركاتهم التاريخية دون مساس. لا تشمل العملية الأفراد غير المحددين أو الصفحات المخفية.";
    public string CompletionMessage => _receipt is null ? "" :
        $"اعتمد النقل الخارجي لـ {_receipt.Count} فرد إلى «{_receipt.DestinationName}» بتاريخ {_receipt.BusinessDate}. " +
        "طبقت الخدمة الأصلية سياسة تعليق الحسابات المرتبطة وإلغاء جلساتها.";

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativeBulkExternalTransferBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool> currentRow)
    {
        if (_backend is not null)
            throw new InvalidOperationException("External transfer backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = currentRow ?? throw new ArgumentNullException(nameof(currentRow));
    }

    public void Open(IReadOnlyList<NativePersonnelDisplayRow> rows,
        string sourceSiteId, IReadOnlyList<NativeDashboardSite> sites)
    {
        var actor = _liveActor?.Invoke();
        if (_backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _uncertainActor == actor ||
            sourceSiteId is not { Length: >= 1 and <= 128 } ||
            sourceSiteId.Any(char.IsControl) ||
            rows is null || rows.Count is < 1 or > 100 ||
            sites is null || sites.Count is < 1 or > 10000 ||
            rows.Any(row => row is null || _onPage?.Invoke(row) != true ||
                !row.Active || row.StatusName != "على القوة" ||
                row.Revision < 1 ||
                row.PersonId is not { Length: >= 1 and <= 128 } ||
                row.PersonId.Any(char.IsControl) ||
                row.Name is not { Length: >= 2 and <= 200 } ||
                row.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                row.ServiceNumberA.Any(ch => ch is < '0' or > '9')) ||
            rows.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != rows.Count ||
            sites.Select(x => x.SiteId)
                .Distinct(StringComparer.Ordinal).Count() != sites.Count)
            return;
        var source = sites.Where(x => x.SiteId == sourceSiteId).ToArray();
        if (source.Length != 1 ||
            source[0].Name is not { Length: >= 1 and <= 200 } ||
            rows.Any(x => x.SiteName != source[0].Name))
            return;
        Reset();
        _actor = actor;
        _sourceSiteId = sourceSiteId;
        _sourceSiteName = source[0].Name;
        _open = true;
        // Nobody is silently selected. Operator explicitly opts in per member.
        _options = rows.Select(row => new NativeBulkTransferOption(
            row, () => CanEdit, OnSelectionChanged)).ToArray();
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        var draft = CurrentDraft();
        if (!CanEdit || _backend is null || draft is null ||
            _actor is not { } actor)
            return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _approvedDraft = null;
        _approvedPeople = null;
        _identityReviewed = false;
        _accountAcknowledged = false;
        _error = "";
        Notify();
        try
        {
            var preview = await _backend.PreviewExternalBatchAsync(
                actor, draft, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(preview, draft);
            _approvedDraft = draft;
            _approvedPeople = draft.Persons.ToArray();
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _preview = null;
                _approvedDraft = null;
                _approvedPeople = null;
                _error = "تعذرت معاينة النقل الخارجي أو تغيرت بيانات أحد الأفراد. لم تُحفظ أي حركة أو حساب.";
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
        if (!CanConfirm || _actor is not { } actor ||
            _backend is null || _preview is not { } preview) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // One original SQLCipher COMMIT ONLY.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            var result = await _backend.CommitExternalBatchAsync(
                actor, preview, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (result is null || !result.IsExternal ||
                result.BusinessDate != preview.BusinessDate ||
                result.Count != preview.Persons.Count ||
                result.DestinationName != preview.DestinationName)
                throw new InvalidDataException("Canonical external exit receipt changed.");
            _receipt = result;
            _preview = null;
            _approvedDraft = null;
            _approvedPeople = null;
            _identityReviewed = false;
            _accountAcknowledged = false;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _preview = null;
                _approvedDraft = null;
                _approvedPeople = null;
                _identityReviewed = false;
                _accountAcknowledged = false;
                _error = "قد تكون الدفعة الخارجية وتعليق الحسابات قد نُفذت رغم فقدان الرد. " +
                    "راجع سجل التنقلات والأفراد والحسابات والجلسات قبل أي محاولة جديدة. " +
                    "لا إعادة تنفيذ تلقائية.";
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

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _options = [];
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _identityReviewed = false;
        _accountAcknowledged = false;
        _sourceSiteId = "";
        _sourceSiteName = "";
        _externalDestination = "";
        _error = "";
        _approvedDraft = null;
        _approvedPeople = null;
        _preview = null;
        _receipt = null;
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _options.Count is >= 1 and <= 100 &&
        _options.All(o => _onPage?.Invoke(o.Row) == true &&
            o.Row.Active && o.Row.StatusName == "على القوة" &&
            o.Row.Revision >= 1 && o.Row.SiteName == _sourceSiteName);

    private IReadOnlyList<NativeBulkInternalPerson> CurrentPeople() =>
        _options.Where(x => x.IsSelected)
            .Select(x => new NativeBulkInternalPerson(
                x.Row.PersonId, x.Row.Name, x.Row.ServiceNumberA,
                x.Row.Revision)).ToArray();

    private NativeBulkExternalTransferDraft? CurrentDraft()
    {
        if (!IsOriginalContextValid()) return null;
        var people = CurrentPeople();
        string destination = _externalDestination.Trim();
        if (people.Count is < 1 or > 100 ||
            people.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != people.Count ||
            destination.Length is < 2 or > 150 ||
            destination.Any(char.IsControl))
            return null;
        return new NativeBulkExternalTransferDraft(
            _sourceSiteId, _sourceSiteName, destination, people);
    }

    private static bool SameDraft(NativeBulkExternalTransferDraft a,
        NativeBulkExternalTransferDraft b) =>
        a.SourceSiteId == b.SourceSiteId &&
        a.SourceSiteName == b.SourceSiteName &&
        a.ExternalDestinationName == b.ExternalDestinationName &&
        a.Persons.SequenceEqual(b.Persons);

    private void ValidatePreview(NativeBulkExternalTransferPreview p,
        NativeBulkExternalTransferDraft draft)
    {
        string today = DateOnly.FromDateTime(DateTime.Now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (p is null || !p.IsExternal ||
            !SameDraft(p.Draft, draft) ||
            p.BusinessDate != today ||
            p.PreviewToken is not { Length: 64 } ||
            p.PreviewToken.Any(x => !char.IsAsciiHexDigit(x)) ||
            p.DestinationName != draft.ExternalDestinationName ||
            p.Persons is null || p.Persons.Count != draft.Persons.Count ||
            p.Persons.Count is < 1 or > 100)
            throw new InvalidDataException("Altered external batch preview envelope.");
        for (int index = 0; index < draft.Persons.Count; index++)
        {
            var before = draft.Persons[index];
            var actual = p.Persons[index];
            if (actual is null ||
                actual.PersonId != before.PersonId ||
                actual.Name != before.Name ||
                actual.ServiceNumberA != before.ServiceNumberA ||
                actual.Revision != before.Revision ||
                actual.SourceSiteId != draft.SourceSiteId ||
                actual.SourceSiteName != draft.SourceSiteName ||
                actual.SourceAssignmentId is not { Length: >= 3 and <= 128 })
                throw new InvalidDataException("Altered or missing PersonId/assignment.");
        }
    }

    private void OnSelectionChanged()
    {
        InvalidatePreview();
        Notify();
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _preview = null;
        _approvedDraft = null;
        _approvedPeople = null;
        _identityReviewed = false;
        _accountAcknowledged = false;
        _error = "";
    }

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit),
            nameof(CanPreview), nameof(CanConfirm),
            nameof(HasPreview), nameof(HasUnverifiedCommit),
            nameof(SelectedCount), nameof(SourceSiteName),
            nameof(SourceSiteId), nameof(ExternalDestinationName),
            nameof(IdentityReviewed), nameof(AccountSuspensionAcknowledged),
            nameof(Error), nameof(Preview), nameof(Receipt),
            nameof(Options), nameof(ReviewMessage),
            nameof(CompletionMessage)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
