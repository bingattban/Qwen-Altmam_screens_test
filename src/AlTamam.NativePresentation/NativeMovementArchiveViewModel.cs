using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Historical movement archive only. Canonical SQLCipher records are immutable.
/// All paging/detail reads require the same live manager session and discard stale replies.
/// </summary>
public sealed class NativeMovementArchiveViewModel : INotifyPropertyChanged
{
    private INativeMovementArchiveBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending, _detailPending;
    private int _generation, _detailGeneration;
    private bool _open, _busy, _detailBusy, _hasNext;
    private string _draft = "", _term = "", _type = "all", _error = "", _detailError = "";
    private int _page = 1;
    private long _total;
    private IReadOnlyList<NativeMovementArchiveRow> _rows = [];
    private NativeMovementArchiveDetail? _detail;
    private static readonly IReadOnlyList<NativeMovementArchiveTypeOption> Kinds =
    [
        new("all", "كل التنقلات والعودة"), new("internal", "النقل الداخلي"),
        new("external", "النقل الخارجي"), new("return", "العودة")
    ];

    public NativeMovementArchiveViewModel()
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead && !_busy);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanRead && !_busy);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(), _ => CanRead && !_busy && _hasNext);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(), _ => CanRead && !_busy && _page > 1);
        DetailsCommand = new NativeCommand(x => {
            if (x is NativeMovementArchiveRow row) _ = OpenDetailsAsync(row);
        }, x => x is NativeMovementArchiveRow row && CanRead && !_busy && !_detailBusy &&
            _rows.Any(current => ReferenceEquals(current, row)));
        CloseDetailsCommand = new NativeCommand(_ => CloseDetails(), _ => _detail is not null || _detailBusy);
        CloseCommand = new NativeCommand(_ => Close(), _ => _open);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen => _open;
    public bool IsLoading => _busy;
    public bool IsDetailLoading => _detailBusy;
    public string LoadError => _error;
    public string DetailError => _detailError;
    public int Page => _page;
    public long Total => _total;
    public string PageLabel => $"الصفحة {_page} — إجمالي الحركات {_total}";
    public bool HasNext => _hasNext;
    public IReadOnlyList<NativeMovementArchiveRow> Rows => _rows;
    public NativeMovementArchiveDetail? SelectedDetail => _detail;
    public bool HasDetail => _detail is not null;
    public IReadOnlyList<NativeMovementArchiveTypeOption> TypeOptions => Kinds;
    public string SelectedType => _type;
    public string SearchDraft
    {
        get => _draft;
        set { if (_draft != value) { _draft = value ?? ""; Changed(nameof(SearchDraft)); } }
    }

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

    public void AttachBackend(INativeMovementArchiveBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null) throw new InvalidOperationException("Archive already attached.");
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
        if (!CanRead || _backend is null || _actor is not { } actor)
        {
            Close();
            return;
        }
        if (!ValidSearch(_term) || !ValidType(_type) || _page is < 1 or > 100000)
        {
            HideRows("مرشحات أرشيف التنقلات غير صالحة.");
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
            var filter = new NativeMovementArchiveFilter(_term, _type, _page);
            NativeMovementArchivePage page =
                await _backend.ReadMovementsAsync(actor, filter, linked.Token);
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
                HideRows("تعذر التحقق من أرشيف التنقلات؛ أُخفيت النتائج السابقة.");
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
        string search = (term ?? "").Trim();
        if (!ValidSearch(search)) { HideRows("بحث غير صالح؛ الحد الأقصى 100 حرف."); return; }
        if (!CanRead) { Close(); return; }
        _term = search;
        _draft = search;
        _page = 1;
        Changed();
        await RefreshAsync(ct);
    }

    public async Task SelectTypeAsync(string? type, CancellationToken ct = default)
    {
        if (!ValidType(type))
        {
            HideRows("نوع الحركة المطلوب غير معتمد.");
            return;
        }
        if (!CanRead) { Close(); return; }
        if (type == _type) return;
        _type = type!;
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

    public async Task OpenDetailsAsync(NativeMovementArchiveRow? row,
        CancellationToken ct = default)
    {
        if (!CanRead || _backend is null || _actor is not { } actor ||
            row is null || !_rows.Any(x => ReferenceEquals(x, row)) || _busy) return;
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
            NativeMovementArchiveDetail detail =
                await _backend.ReadMovementDetailsAsync(actor, row.MovementId, linked.Token);
            if (version != _detailGeneration || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            if (!_rows.Any(x => ReferenceEquals(x, row)) ||
                detail.MovementId != row.MovementId || detail.PersonId != row.PersonId ||
                detail.Name != row.Name || detail.ServiceNumberA != row.ServiceNumberA ||
                detail.Type != row.Type || detail.SourceSite != row.SourceSite ||
                detail.Destination != row.Destination ||
                detail.EffectiveDate != row.EffectiveDate ||
                detail.CommittedAtUtc != row.CommittedAtUtc ||
                string.IsNullOrWhiteSpace(detail.ActorUsername))
                throw new InvalidDataException("Forged or changed archive details.");
            _detail = detail;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _detailGeneration)
                _detailError = "تعذرت مطابقة تفاصيل الحركة مع القيد التاريخي المعروض.";
        }
        finally
        {
            if (version == _detailGeneration)
            {
                _detailBusy = false;
                _detailPending = null;
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
        _term = "";
        _type = "all";
        _draft = "";
        _page = 1;
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
    private static bool ValidType(string? kind) =>
        kind is "all" or "internal" or "external" or "return";

    private static void ValidatePage(NativeMovementArchivePage page,
        NativeMovementArchiveFilter filter)
    {
        if (page is null || page.Filter != filter || page.Total < 0 ||
            page.Rows is null || page.Rows.Count > filter.PageSize ||
            page.Rows.Any(x => x is null ||
                string.IsNullOrWhiteSpace(x.MovementId) || x.MovementId.Length > 128 ||
                string.IsNullOrWhiteSpace(x.PersonId) ||
                string.IsNullOrWhiteSpace(x.Name) ||
                x.Type is not ("داخلي" or "خارجي" or "عودة") ||
                x.EffectiveDate.Length != 10 || string.IsNullOrWhiteSpace(x.CommittedAtUtc)) ||
            page.Rows.Select(x => x.MovementId).Distinct(StringComparer.Ordinal).Count() != page.Rows.Count ||
            page.HasNext != ((long)filter.Page * filter.PageSize < page.Total) ||
            (page.Rows.Count == 0 && filter.Page == 1 && page.Total > 0) ||
            (page.Rows.Count > page.Total))
            throw new InvalidDataException("Malformed canonical movement archive.");
    }

    private void Changed(params string[] names)
    {
        if (names.Length == 0)
            names = [nameof(IsOpen), nameof(IsLoading), nameof(IsDetailLoading),
                nameof(LoadError), nameof(DetailError), nameof(Page),
                nameof(PageLabel), nameof(Total), nameof(HasNext), nameof(Rows),
                nameof(SelectedDetail), nameof(HasDetail), nameof(SearchDraft), nameof(SelectedType)];
        foreach (string name in names) PropertyChanged?.Invoke(this, new(name));
        foreach (var cmd in new[] { RefreshCommand, SearchCommand, NextPageCommand,
                                   PreviousPageCommand, DetailsCommand, CloseDetailsCommand,
                                   CloseCommand })
            if (cmd is NativeCommand native) native.NotifyAvailabilityChanged();
    }
}
