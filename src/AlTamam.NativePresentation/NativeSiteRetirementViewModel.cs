using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// A site retirement is a potentially many-person dated assignment change.
/// Require current immutable SiteId and revision, canonical read-only count,
/// visibly disclosed SITE-UNSPECIFIED destination, distinct human consent,
/// and ONE original atomic SQLCipher commit. Never guess or auto-resend.
/// </summary>
public sealed class NativeSiteRetirementViewModel : INotifyPropertyChanged
{
    private INativeSiteRetirementBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeSiteRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private NativeSiteRow? _row;
    private NativeSiteRetirementPreview? _preview;
    private NativeSiteRetirementReceipt? _receipt;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _impactConfirmed;
    private string _error = "";

    public NativeSiteRetirementViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted &&
        IsOriginalContextValid();
    public bool CanPreview => CanEdit && _backend is not null;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanConfirm => CanEdit && _impactConfirmed &&
        _preview is { Eligible: true } candidate &&
        PreviewMatchesOriginalRow(candidate);
    public string Error => _error;
    public NativeSiteRetirementPreview? Preview => _preview;
    public NativeSiteRetirementReceipt? Receipt => _receipt;
    public string SourceSiteName => _row?.Name ?? "";
    public string SourceSiteId => _row?.SiteId ?? "";
    public long SourceRevision => _row?.Revision ?? 0;
    public string ImpactNote => _preview is not { } p ? "" :
        $"الموقع: «{p.SiteName}» ({p.SiteId})، الإصدار {p.Revision}. " +
        $"سيُوقف الموقع وتُنقل انتسابات {p.ActiveCount} فردا نشطا إلى الموقع «غير محدد» " +
        $"({p.DestinationSiteId}) اعتبارا من {p.EffectiveDate} فقط. " +
        "لن يتغير رقم الفرد أو هويته أو تحضيره وسجلاته السابقة.";
    public string CompletionNote => _receipt is not { } r ? "" :
        $"تم إيقاف الموقع «{r.SiteName}» ونقل {r.ReassignedCount} فردا " +
        $"إلى «غير محدد» بتاريخ {r.EffectiveDate} مع حفظ التاريخ السابق.";

    public bool ImpactConfirmed
    {
        get => _impactConfirmed;
        set
        {
            if (!CanEdit || _preview is not { Eligible: true } ||
                _impactConfirmed == value)
                return;
            _impactConfirmed = value;
            Notify();
        }
    }

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativeSiteRetirementBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativeSiteRow, bool> currentSite)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Site retirement backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = currentSite ?? throw new ArgumentNullException(nameof(currentSite));
    }

    public void Open(NativeSiteRow? site)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_open || _backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _uncertainActor == actor ||
            site is not { Active: true, Revision: >= 1 } ||
            site.SiteId is not { Length: >= 3 and <= 128 } ||
            site.SiteId == "SITE-UNSPECIFIED" ||
            site.SiteId.Any(char.IsControl) ||
            site.Name is not { Length: >= 2 and <= 100 } ||
            site.Name.Any(char.IsControl) ||
            _onPage?.Invoke(site) != true)
            return;
        Reset();
        _row = site;
        _actor = actor;
        _open = true;
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null ||
            _actor is not { } actor || _row is not { } site) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _receipt = null;
        _impactConfirmed = false;
        _error = "";
        Notify();
        try
        {
            NativeSiteRetirementPreview result =
                await _backend.PreviewRetirementAsync(
                    actor, site.SiteId, site.Revision, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(result);
            _preview = result;
            if (!result.Eligible)
                _error = result.BlockReason;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _preview = null;
                _impactConfirmed = false;
                _error = "تعذرت معاينة موقع الإيقاف أو تغير عدد الأفراد/إصدار الموقع. أعد تحديث قائمة المواقع.";
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
        if (!CanConfirm || _backend is null ||
            _actor is not { } actor || _preview is not { } preview)
            return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // BEFORE any await of a possibly durable write.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            NativeSiteRetirementReceipt result =
                await _backend.CommitRetirementAsync(actor, preview,
                    operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (result is null || result.SiteId != preview.SiteId ||
                result.SiteName != preview.SiteName ||
                result.Revision != preview.Revision + 1 ||
                result.ReassignedCount != preview.ActiveCount ||
                result.EffectiveDate != preview.EffectiveDate ||
                result.DestinationSiteId != "SITE-UNSPECIFIED" ||
                result.Active)
                throw new InvalidDataException("Canonical retirement receipt mismatch.");
            _receipt = result;
            _preview = null;
            _impactConfirmed = false;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _receipt = null;
                _preview = null;
                _impactConfirmed = false;
                _error = "قد يكون الموقع قد أوقف بالفعل رغم فقدان الرد. راجع المواقع والأفراد وسجل العمليات أولا؛ لا إعادة تنفيذ تلقائية.";
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

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _impactConfirmed = false;
        _actor = null;
        _row = null;
        _preview = null;
        _receipt = null;
        _error = "";
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _row is { Active: true, Revision: >= 1 } site &&
        site.SiteId != "SITE-UNSPECIFIED" &&
        _onPage?.Invoke(site) == true;

    private bool PreviewMatchesOriginalRow(NativeSiteRetirementPreview p) =>
        _row is { } site && p.SiteId == site.SiteId &&
        p.SiteName == site.Name && p.Revision == site.Revision &&
        p.ActiveCount >= 0 && p.EffectiveDate ==
            DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
        p.DestinationSiteId == "SITE-UNSPECIFIED" &&
        p.DestinationSiteName == "غير محدد" &&
        p.BlockReason == "" && p.Revision < long.MaxValue;

    private void ValidatePreview(NativeSiteRetirementPreview? p)
    {
        if (p is null || !IsOriginalContextValid() ||
            !PreviewMatchesOriginalRow(p) && p.Eligible ||
            p.SiteId != _row?.SiteId || p.SiteName != _row.Name ||
            p.Revision != _row.Revision || p.ActiveCount < 0 ||
            p.Revision == long.MaxValue ||
            p.DestinationSiteId != "SITE-UNSPECIFIED" ||
            p.DestinationSiteName != "غير محدد" ||
            p.EffectiveDate !=
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            (p.Eligible && p.BlockReason != "") ||
            (!p.Eligible && string.IsNullOrWhiteSpace(p.BlockReason)))
            throw new InvalidDataException("Missing/stale original retirement count.");
    }

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit),
            nameof(CanPreview), nameof(HasPreview),
            nameof(HasUnverifiedCommit), nameof(CanConfirm),
            nameof(Error), nameof(Preview), nameof(Receipt),
            nameof(SourceSiteName), nameof(SourceSiteId),
            nameof(SourceRevision), nameof(ImpactNote),
            nameof(CompletionNote), nameof(ImpactConfirmed)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
