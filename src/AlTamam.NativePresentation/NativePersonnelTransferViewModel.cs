using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 8: one-person movement, internal or external, through the ORIGINAL
/// actor-bound SQLCipher preview/commit. The current site must have been
/// explicitly selected as a directory filter, never guessed from its name.
/// </summary>
public sealed class NativePersonnelTransferViewModel : INotifyPropertyChanged
{
    private INativePersonnelMovementBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private NativePersonnelDisplayRow? _row;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _external, _busy, _attempted, _uncertain;
    private string _sourceSiteId = "", _sourceSiteName = "";
    private string _destinationSiteId = "", _externalName = "", _error = "";
    private IReadOnlyList<NativeDashboardSite> _sites = [];
    private NativePersonnelMovementDraft? _approvedDraft;
    private NativePersonnelMovementPreview? _preview;
    private NativePersonnelMovementReceipt? _receipt;

    public NativePersonnelTransferViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsExternal => _external;
    public bool IsInternal => _open && !_external;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanPreview => CanEdit && _backend is not null &&
        IsOriginalContextValid() && CurrentDraft() is not null;
    public bool CanConfirm => CanEdit && _preview is not null &&
        _approvedDraft is not null && CurrentDraft() == _approvedDraft &&
        IsOriginalContextValid();
    public string Header => _external ? "النقل الخارجي — تأكيد الحركة" :
        "النقل الداخلي — تأكيد الحركة";
    public string SelectedPersonName => _row?.Name ?? "";
    public string SelectedPersonNumber => _row?.ServiceNumberA ?? "";
    public string SourceSiteName => _sourceSiteName;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public string Error => _error;
    public string ReviewMessage => _preview is null ? "" :
        $"سيتم نقل «{_preview.Persons[0].Name}» من «{_sourceSiteName}» إلى «{_preview.DestinationName}» بتاريخ {_preview.BusinessDate}. لا يغير النظام التحضير السابق، ويُنشئ حركة وسجل عملية أصليين.";
    public string CompletionMessage => _receipt is null ? "" :
        $"نجح النقل إلى «{_receipt.DestinationName}» للفرد المحدد بتاريخ {_receipt.BusinessDate}.";
    public NativePersonnelMovementPreview? Preview => _preview;
    public NativePersonnelMovementReceipt? Receipt => _receipt;

    public string FormDestinationSiteId
    {
        get => _destinationSiteId;
        set
        {
            if (!CanEdit || _external || _destinationSiteId == value) return;
            _destinationSiteId = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }
    public string FormExternalName
    {
        get => _externalName;
        set
        {
            if (!CanEdit || !_external || _externalName == value) return;
            _externalName = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativePersonnelMovementBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool> rowIsCurrent)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Movement backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = rowIsCurrent ?? throw new ArgumentNullException(nameof(rowIsCurrent));
        Notify();
    }

    public void Open(NativePersonnelDisplayRow row, string sourceSiteId,
        IReadOnlyList<NativeDashboardSite> sites, bool external)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_backend is null || actor is null || row is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _uncertainActor == actor || sites is null ||
            sites.Count is < 1 or > 10000 || string.IsNullOrWhiteSpace(sourceSiteId) ||
            sourceSiteId.Any(char.IsControl) || sourceSiteId.Length > 128 ||
            _onPage?.Invoke(row) != true ||
            !row.Active || row.StatusName != "على القوة" ||
            row.Revision < 1 || string.IsNullOrWhiteSpace(row.PersonId))
            return;
        var source = sites.Where(site => site.SiteId == sourceSiteId).ToArray();
        if (source.Length != 1 || source[0].Name != row.SiteName)
            return;
        Reset();
        _row = row;
        _actor = actor;
        _open = true;
        _external = external;
        _sourceSiteId = sourceSiteId;
        _sourceSiteName = source[0].Name;
        _sites = sites.Where(x => x.SiteId.Length > 0 &&
            x.SiteId != sourceSiteId).ToArray();
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null || _actor is not { } actor) return;
        var draft = CurrentDraft();
        if (draft is null) return;
        int generation = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _approvedDraft = null;
        _error = "";
        Notify();
        try
        {
            var result = await _backend.PreviewMovementAsync(actor, draft, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(result, draft);
            _preview = result;
            _approvedDraft = draft;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (generation == _generation)
                _error = "تعذرت معاينة النقل أو تغيرت بيانات الفرد أو موقعه؛ لم تحفظ أي حركة.";
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
        if (!CanConfirm || _backend is null || _preview is not { } preview ||
            _actor is not { } actor) return;
        int generation = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true;
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            var receipt = await _backend.CommitMovementAsync(
                actor, preview, operationId, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (receipt.IsExternal != preview.Draft.IsExternal ||
                receipt.BusinessDate != preview.BusinessDate ||
                receipt.DestinationName != preview.DestinationName ||
                receipt.Count != 1)
                throw new InvalidDataException("Canonical movement receipt mismatch.");
            _receipt = receipt;
            _preview = null;
            _approvedDraft = null;
        }
        catch
        {
            if (generation == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _preview = null;
                _approvedDraft = null;
                _error = "تعذر التأكد من نتيجة النقل؛ قد يكون تم الحفظ. راجع سجل الحركات والعمليات قبل محاولة أخرى؛ لن يُعاد الطلب تلقائيا.";
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
        bool completed = _receipt is not null;
        Reset();
        if (completed) CommittedAndClosed?.Invoke();
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
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _external = false;
        _sourceSiteId = "";
        _sourceSiteName = "";
        _destinationSiteId = "";
        _externalName = "";
        _error = "";
        _approvedDraft = null;
        _preview = null;
        _receipt = null;
        _sites = [];
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _row is { Active: true } row && row.StatusName == "على القوة" &&
        _onPage?.Invoke(row) == true;

    private NativePersonnelMovementDraft? CurrentDraft()
    {
        if (_row is null || !IsOriginalContextValid()) return null;
        if (_external)
        {
            string destination = _externalName.Trim();
            if (destination.Length is < 2 or > 150 || destination.Any(char.IsControl))
                return null;
            return new NativePersonnelMovementDraft(
                true, _row.PersonId, _row.Name, _row.ServiceNumberA,
                _row.Revision, _sourceSiteId, _sourceSiteName,
                "", destination);
        }
        if (_destinationSiteId.Length == 0 ||
            _destinationSiteId == _sourceSiteId ||
            !_sites.Any(site => site.SiteId == _destinationSiteId))
            return null;
        return new NativePersonnelMovementDraft(
            false, _row.PersonId, _row.Name, _row.ServiceNumberA,
            _row.Revision, _sourceSiteId, _sourceSiteName,
            _destinationSiteId, "");
    }

    private void ValidatePreview(NativePersonnelMovementPreview preview,
        NativePersonnelMovementDraft draft)
    {
        var today = DateOnly.FromDateTime(DateTime.Now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (preview.Draft != draft || preview.BusinessDate != today ||
            preview.PreviewToken.Length != 64 ||
            preview.PreviewToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            preview.Persons.Count != 1 ||
            (draft.IsExternal
                ? preview.DestinationName != draft.ExternalDestinationName
                : !_sites.Any(s => s.SiteId == draft.DestinationSiteId &&
                                  s.Name == preview.DestinationName)))
            throw new InvalidDataException("Invalid movement preview envelope.");

        var person = preview.Persons[0];
        if (person.PersonId != draft.PersonId ||
            person.Name != draft.PersonName ||
            person.ServiceNumberA != draft.ServiceNumberA ||
            person.Revision != draft.Revision ||
            person.SourceSiteId != draft.SourceSiteId ||
            person.SourceSiteName != draft.SourceSiteName ||
            person.SourceAssignmentId.Length is < 3 or > 128)
            throw new InvalidDataException("Movement identity changed after roster selection.");
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _approvedDraft = null;
        _preview = null;
        _error = "";
        Notify();
    }

    private void Notify()
    {
        foreach (string property in new[]
        {
            nameof(IsOpen), nameof(IsExternal), nameof(IsInternal), nameof(IsBusy),
            nameof(CanEdit), nameof(CanPreview), nameof(CanConfirm),
            nameof(HasPreview), nameof(HasUnverifiedCommit),
            nameof(Header), nameof(SelectedPersonName), nameof(SelectedPersonNumber),
            nameof(SourceSiteName), nameof(FormDestinationSiteId),
            nameof(FormExternalName), nameof(SiteOptions), nameof(Preview),
            nameof(Receipt), nameof(Error), nameof(ReviewMessage),
            nameof(CompletionMessage)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
