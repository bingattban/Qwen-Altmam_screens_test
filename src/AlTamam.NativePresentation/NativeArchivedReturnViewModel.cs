using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Re-admit the SAME identified archived human using original ArchivedReturnService.
/// A named and numbered historic identity is frozen; the operator explicitly
/// selects the destination, inspects ALL returned candidates and confirms sameness.
/// No implicit identity inference, second SQL writer or background commit.
/// </summary>
public sealed class NativeArchivedReturnViewModel : INotifyPropertyChanged
{
    private INativeArchivedReturnBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private NativePersonnelDisplayRow? _row;
    private IReadOnlyList<NativeDashboardSite> _sites = [];
    private NativeArchivedReturnDraft? _approvedDraft;
    private NativeArchivedReturnPreview? _preview;
    private NativeArchivedReturnReceipt? _receipt;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _identityConfirmed;
    private string _selectedSiteId = "", _error = "";

    public NativeArchivedReturnViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool HasNumberConflict => _preview is { } preview &&
        preview.Candidates.Any(x =>
            x.PersonId != preview.Draft.PersonId && x.NumberMatch);
    public bool CanPreview => CanEdit && _backend is not null &&
        CurrentDraft() is not null;
    public bool CanConfirm => CanEdit && _identityConfirmed &&
        !HasNumberConflict &&
        _preview is not null && _approvedDraft is not null &&
        CurrentDraft() == _approvedDraft &&
        SelectedCandidate(_preview, _approvedDraft) is not null;
    public string Error => _error;
    public NativeArchivedReturnPreview? Preview => _preview;
    public NativeArchivedReturnReceipt? Receipt => _receipt;
    public string PersonName => _row?.Name ?? "";
    public string ServiceNumberA => _row?.ServiceNumberA ?? "";
    public string PersonStatus => _row?.StatusName ?? "";
    public string OriginalPersonId => _row?.PersonId ?? "";
    public string SelectedDestinationName => _sites.FirstOrDefault(x => x.SiteId == _selectedSiteId)?.Name ?? "";
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public string SelectedSiteId
    {
        get => _selectedSiteId;
        set
        {
            if (!CanEdit || _selectedSiteId == value) return;
            _selectedSiteId = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }
    public bool IdentityConfirmed
    {
        get => _identityConfirmed;
        set
        {
            if (!CanEdit || _preview is null || _identityConfirmed == value) return;
            _identityConfirmed = value;
            Notify();
        }
    }
    public string ConfirmationNote => _preview is null ? "" :
        $"العودة ستعيد نفس هوية الفرد «{_approvedDraft?.OriginalName}» ({_approvedDraft?.PersonId}) إلى «{_approvedDraft?.DestinationSiteName}» بتاريخ {_preview.BusinessDate}، مع الاحتفاظ بتاريخ الفصل أو النقل السابق. ظهرت {_preview.Candidates.Count} هوية مؤرشفة محتملة؛ يجب التأكد من الهوية بنفسك.";
    public string CompletionNote => _receipt is null ? "" :
        $"تم اعتماد عودة الفرد نفسه وحفظ الحركة {_receipt.MovementId}، في الموقع {_receipt.SiteId}.";

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativeArchivedReturnBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool> currentRow)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Native archived return is already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = currentRow ?? throw new ArgumentNullException(nameof(currentRow));
        Notify();
    }

    public void Open(NativePersonnelDisplayRow row,
        IReadOnlyList<NativeDashboardSite> sites)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_backend is null || actor is null || row is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _uncertainActor == actor ||
            _onPage?.Invoke(row) != true ||
            row.Active || row.StatusName is not ("منقول خارجيا" or "مفصول" or "مؤرشف") ||
            row.Revision < 1 || string.IsNullOrWhiteSpace(row.PersonId) ||
            row.PersonId.Length > 128 ||
            row.Name is not { Length: >= 3 and <= 200 } ||
            row.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
            sites is null || sites.Count is < 1 or > 10001 ||
            sites.Any(x => x is null || x.SiteId is null || x.Name is null ||
                x.SiteId.Length > 128 || x.SiteId.Any(char.IsControl) ||
                x.Name.Length is < 1 or > 200) ||
            sites.Select(x => x.SiteId).Distinct(StringComparer.Ordinal).Count() != sites.Count)
            return;

        Reset();
        _row = row;
        _actor = actor;
        _sites = sites.Where(x => !string.IsNullOrWhiteSpace(x.SiteId)).ToArray();
        _open = true;
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null || _actor is not { } actor)
        {
            if (_open && _preview is null)
            {
                _error = "اختر موقع العودة المعتمد من القائمة قبل طلب معاينة الهوية.";
                Notify();
            }
            return;
        }
        NativeArchivedReturnDraft? draft = CurrentDraft();
        if (draft is null) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _approvedDraft = null;
        _identityConfirmed = false;
        _error = "";
        Notify();
        try
        {
            NativeArchivedReturnPreview result =
                await _backend.PreviewReturnAsync(actor, draft, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(result, draft);
            _approvedDraft = draft;
            _preview = result;
            _identityConfirmed = false;
            if (HasNumberConflict)
                _error = "يوجد فرد مؤرشف آخر يستخدم الرقم (ع) نفسه. يجب حل تعارض الهوية قبل اعتماد العودة.";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _approvedDraft = null;
                _preview = null;
                _error = "تعذرت مطابقة الهوية المؤرشفة وموقع العودة؛ لم تُحفظ أي حركة. راجع بيانات الفرد والموقع.";
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
        if (!CanConfirm || _backend is null || _actor is not { } actor ||
            _preview is not { } preview)
            return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // Set BEFORE first awaited write, never reset to retry.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            NativeArchivedReturnReceipt receipt = await _backend.CommitReturnAsync(
                actor, preview, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (!receipt.Returned || receipt.PersonId != preview.Draft.PersonId ||
                receipt.SiteId != preview.Draft.DestinationSiteId ||
                receipt.BusinessDate != preview.BusinessDate ||
                receipt.Revision != preview.Draft.Revision + 1 ||
                string.IsNullOrWhiteSpace(receipt.MovementId) ||
                !receipt.MovementId.StartsWith("MOV-", StringComparison.Ordinal))
                throw new InvalidDataException("Canonical archived return receipt changed.");
            _receipt = receipt;
            _preview = null;
            _approvedDraft = null;
            _identityConfirmed = false;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _preview = null;
                _approvedDraft = null;
                _identityConfirmed = false;
                _error = "قد تكون العودة قد اعتُمدت رغم انقطاع الرد. راجع سجل الأفراد وأرشيف التنقلات وسجل العمليات قبل أي إجراء جديد. لا إعادة إرسال تلقائية.";
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
        _row = null;
        _sites = [];
        _approvedDraft = null;
        _preview = null;
        _receipt = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _identityConfirmed = false;
        _selectedSiteId = "";
        _error = "";
        Notify();
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _approvedDraft = null;
        _preview = null;
        _identityConfirmed = false;
        _error = "";
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _row is { Active: false, Revision: >= 1 } row &&
        row.StatusName is "منقول خارجيا" or "مفصول" or "مؤرشف" &&
        _onPage?.Invoke(row) == true;

    private NativeArchivedReturnDraft? CurrentDraft()
    {
        if (!IsOriginalContextValid() || _row is not { } row) return null;
        var site = _sites.Where(x => x.SiteId == _selectedSiteId &&
            x.SiteId.Length > 0).ToArray();
        if (site.Length != 1 || string.IsNullOrWhiteSpace(site[0].Name)) return null;
        int state = row.StatusName switch
        {
            "منقول خارجيا" => 2,
            "مفصول" => 3,
            "مؤرشف" => 4,
            _ => -1
        };
        if (state < 2 || row.Name != row.Name.Trim() ||
            row.ServiceNumberA.Any(c => c is < '0' or > '9') ||
            row.ServiceNumberA.Length is < 4 or > 70 ||
            row.Revision < 1)
            return null;
        return new NativeArchivedReturnDraft(row.PersonId, row.Name,
            row.ServiceNumberA, row.Revision, state,
            site[0].SiteId, site[0].Name);
    }

    private static NativeArchivedReturnCandidate? SelectedCandidate(
        NativeArchivedReturnPreview preview, NativeArchivedReturnDraft draft) =>
        preview.Candidates?.FirstOrDefault(x =>
            x.PersonId == draft.PersonId &&
            x.Name == draft.OriginalName &&
            x.ServiceNumberA == draft.ServiceNumberA &&
            x.Revision == draft.Revision &&
            x.OrganizationalStatus == draft.OrganizationalStatus &&
            x.NumberMatch && x.NameMatch);

    private static void ValidatePreview(
        NativeArchivedReturnPreview preview, NativeArchivedReturnDraft draft)
    {
        string today = DateOnly.FromDateTime(DateTime.Now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (preview is null || preview.Draft != draft ||
            preview.BusinessDate != today ||
            preview.PreviewToken is not { Length: 64 } ||
            preview.PreviewToken.Any(x => !char.IsAsciiHexDigit(x)) ||
            !preview.NumberContested ||
            preview.Candidates is null ||
            preview.Candidates.Count is < 1 or > 50 ||
            preview.Candidates.Any(x => x is null ||
                string.IsNullOrWhiteSpace(x.PersonId) ||
                x.Revision < 1 ||
                x.OrganizationalStatus is not (2 or 3 or 4) ||
                string.IsNullOrWhiteSpace(x.Name) ||
                string.IsNullOrWhiteSpace(x.ServiceNumberA)) ||
            preview.Candidates.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != preview.Candidates.Count ||
            SelectedCandidate(preview, draft) is null)
            throw new InvalidDataException("Missing or forged original archived identity.");
    }

    private void Notify()
    {
        foreach (string property in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit), nameof(HasPreview),
            nameof(HasUnverifiedCommit), nameof(HasNumberConflict),
            nameof(CanPreview), nameof(CanConfirm),
            nameof(Error), nameof(Preview), nameof(Receipt), nameof(PersonName),
            nameof(ServiceNumberA), nameof(PersonStatus), nameof(OriginalPersonId),
            nameof(SelectedDestinationName), nameof(SiteOptions),
            nameof(SelectedSiteId), nameof(IdentityConfirmed),
            nameof(ConfirmationNote), nameof(CompletionNote)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
