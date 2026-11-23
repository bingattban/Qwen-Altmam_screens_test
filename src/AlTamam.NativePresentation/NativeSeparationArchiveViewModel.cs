using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Immutable historical dismissals via original SQLCipher PersonnelSeparationService.
/// Manager/nonce checked at every read; stale pages and forged detail are never shown.
/// </summary>
public sealed class NativeSeparationArchiveViewModel : INotifyPropertyChanged
{
    private INativeSeparationArchiveBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending, _detailPending;
    private int _generation, _detailGeneration;
    private bool _open, _busy, _detailBusy, _hasNext;
    private string _draft = "", _term = "", _siteId = "", _error = "", _detailError = "";
    private int _page = 1;
    private long _total;
    private IReadOnlyList<NativeDashboardSite> _sites = [new("", "جميع المواقع")];
    private IReadOnlyList<NativeSeparationArchiveRow> _rows = [];
    private NativeSeparationArchiveDetail? _detail;

    public NativeSeparationArchiveViewModel()
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead && !_busy);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanRead && !_busy);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => CanRead && !_busy && _hasNext);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => CanRead && !_busy && _page > 1);
        DetailsCommand = new NativeCommand(x => {
            if (x is NativeSeparationArchiveRow row) _ = OpenDetailsAsync(row);
        }, x => x is NativeSeparationArchiveRow row && CanRead && !_busy && !_detailBusy &&
            _rows.Any(current => ReferenceEquals(current, row)));
        CloseDetailsCommand = new NativeCommand(_ => CloseDetails(),
            _ => _detail is not null || _detailBusy);
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public bool IsDetailLoading => _detailBusy;
    public bool HasDetail => _detail is not null;
    public string LoadError => _error;
    public string DetailError => _detailError;
    public string SearchDraft
    {
        get => _draft;
        set { if (_draft != value) { _draft = value ?? ""; Changed(nameof(SearchDraft)); } }
    }
    public string SelectedSiteId => _siteId;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public int Page => _page;
    public long Total => _total;
    public string PageLabel => $"الصفحة {_page} — إجمالي حالات الفصل {_total}";
    public bool HasNext => _hasNext;
    public IReadOnlyList<NativeSeparationArchiveRow> Rows => _rows;
    public NativeSeparationArchiveDetail? SelectedDetail => _detail;

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand DetailsCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand CloseCommand { get; }

    private bool CanRead => _open && _backend is not null &&
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    public void AttachBackend(INativeSeparationArchiveBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Separation archive backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public async Task OpenAsync(
        IReadOnlyList<NativeDashboardSite>? sites = null, CancellationToken ct = default)
    {
        if (_open || _backend is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)) return;
        Close();
        if (sites is not null)
        {
            if (sites.Count is < 1 or > 10001 ||
                sites.Any(x => x is null || x.SiteId is null || x.Name is null ||
                    x.SiteId.Length > 128 || x.SiteId.Any(char.IsControl) ||
                    x.Name.Length is < 1 or > 200) ||
                sites.Select(x => x.SiteId).Distinct(StringComparer.Ordinal).Count() != sites.Count)
                return;
            _sites = sites.ToArray();
        }
        _actor = actor;
        _open = true;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CanRead || _backend is null || _actor is not { } actor)
        {
            Close();
            return;
        }
        if (!ValidSearch(_term) || !ValidSite(_siteId) || _page is < 1 or > 100000)
        {
            HideRows("مرشحات أرشيف الفصل غير صالحة.");
            return;
        }
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        HideRows("");
        Changed();
        try
        {
            var filter = new NativeSeparationArchiveFilter(_term, _siteId, _page);
            NativeSeparationArchivePage page =
                await _backend.ReadSeparationsAsync(actor, filter, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            ValidatePage(page, filter);
            _rows = page.Rows.ToArray();
            _total = page.Total;
            _hasNext = page.HasNext;
            _error = "";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                HideRows("تعذرت قراءة أرشيف الفصل المعتمد؛ أُخفيت النتائج السابقة.");
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

    public async Task SearchAsync(string? term, CancellationToken ct = default)
    {
        string normalized = (term ?? "").Trim();
        if (!ValidSearch(normalized))
        {
            HideRows("البحث غير صالح؛ الحد الأقصى 100 حرف.");
            return;
        }
        if (!CanRead) { Close(); return; }
        _term = normalized;
        _draft = normalized;
        _page = 1;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task SelectSiteAsync(string? siteId, CancellationToken ct = default)
    {
        if (!ValidSite(siteId))
        {
            HideRows("معرف الموقع المطلوب غير صالح.");
            return;
        }
        if (!CanRead) { Close(); return; }
        if (_siteId == siteId) return;
        _siteId = siteId!;
        _page = 1;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (!CanRead || _busy || !_hasNext || _page >= 100000) return;
        ++_page;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (!CanRead || _busy || _page <= 1) return;
        --_page;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task OpenDetailsAsync(NativeSeparationArchiveRow? row,
        CancellationToken ct = default)
    {
        if (!CanRead || _backend is null || _actor is not { } actor ||
            row is null || !_rows.Any(x => ReferenceEquals(x, row)) ||
            _busy || _detailBusy)
            return;
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
            NativeSeparationArchiveDetail detail =
                await _backend.ReadSeparationDetailsAsync(actor, row.MovementId, linked.Token);
            if (version != _detailGeneration || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            if (!_rows.Any(x => ReferenceEquals(x, row)) ||
                detail is null ||
                detail.MovementId != row.MovementId ||
                detail.PersonId != row.PersonId ||
                detail.Name != row.Name ||
                detail.ServiceNumberA != row.ServiceNumberA ||
                detail.SiteName != row.SiteName ||
                detail.Reason != row.Reason ||
                detail.EffectiveDate != row.EffectiveDate ||
                detail.CommittedAtUtc != row.CommittedAtUtc ||
                detail.ActorUserId != row.ActorUserId)
                throw new InvalidDataException("Canonical separation detail differs from frozen row.");
            _detail = detail;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _detailGeneration)
                _detailError = "تفاصيل الفصل لا تطابق سجل الفصل الأصلي.";
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

    public void CloseDetails()
    {
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
        _open = false;
        _busy = false;
        _actor = null;
        _draft = "";
        _term = "";
        _siteId = "";
        _page = 1;
        _sites = [new("", "جميع المواقع")];
        HideRows("");
        Changed();
    }

    private void HideRows(string error)
    {
        CloseDetails();
        _rows = [];
        _total = 0;
        _hasNext = false;
        _error = error;
        Changed();
    }

    private static bool ValidSearch(string? term) =>
        term is not null && term.Length <= 100 && !term.Any(char.IsControl);
    private static bool ValidSite(string? site) =>
        site is not null && site.Length <= 128 && !site.Any(char.IsControl);

    private static void ValidatePage(NativeSeparationArchivePage page,
        NativeSeparationArchiveFilter filter)
    {
        if (page is null || page.Filter != filter || page.Total < 0 ||
            page.Rows is null || page.Rows.Count > filter.PageSize ||
            page.Rows.Any(x => x is null ||
                x.MovementId is not { Length: >= 3 and <= 128 } ||
                string.IsNullOrWhiteSpace(x.PersonId) ||
                string.IsNullOrWhiteSpace(x.Name) ||
                string.IsNullOrWhiteSpace(x.ServiceNumberA) ||
                string.IsNullOrWhiteSpace(x.SiteName) ||
                string.IsNullOrWhiteSpace(x.Reason) ||
                x.EffectiveDate.Length != 10 ||
                string.IsNullOrWhiteSpace(x.CommittedAtUtc) ||
                string.IsNullOrWhiteSpace(x.ActorUserId)) ||
            page.Rows.Select(x => x.MovementId)
                .Distinct(StringComparer.Ordinal).Count() != page.Rows.Count ||
            page.HasNext != ((long)filter.Page * filter.PageSize < page.Total) ||
            (filter.Page == 1 && page.Rows.Count == 0 && page.Total > 0) ||
            page.Rows.Count > page.Total)
            throw new InvalidDataException("Malformed canonical separation history.");
    }

    private void Changed(params string[] names)
    {
        if (names.Length == 0)
            names = [
                nameof(IsOpen), nameof(IsLoading), nameof(IsDetailLoading),
                nameof(HasDetail), nameof(LoadError), nameof(DetailError),
                nameof(SearchDraft), nameof(SelectedSiteId), nameof(SiteOptions),
                nameof(Page), nameof(Total), nameof(PageLabel), nameof(HasNext),
                nameof(Rows), nameof(SelectedDetail)
            ];
        foreach (string name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand command in new[]
        {
            RefreshCommand, SearchCommand, NextPageCommand, PreviousPageCommand,
            DetailsCommand, CloseDetailsCommand, CloseCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
