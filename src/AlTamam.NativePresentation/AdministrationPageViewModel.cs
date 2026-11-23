using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 8, slice 1: read-only native administration personnel register.
/// Source of truth: canonical SQLCipher PersonRosterService, queried by stable
/// PersonId. No create/edit/archive/transfer write is reachable here.
/// </summary>
public sealed class AdministrationPageViewModel : NativePageViewModel, INotifyPropertyChanged
{
    private INativePersonnelDirectoryReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _boundActor;
    private CancellationTokenSource? _pending, _profilePending;
    private int _generation, _profileGeneration;
    private bool _connected, _loading, _hasNext, _catalogueLoaded;
    private bool _profileOpen, _profileLoading;
    private string _error = "", _profileError = "", _site = "", _term = "", _draft = "";
    private int _status = 1, _page = 1;
    private long _total;
    private NativePersonnelProfile? _profile;
    private IReadOnlyList<NativeDashboardSite> _sites = [];
    private IReadOnlyList<NativeDashboardColumn> _columns = [];
    private IReadOnlyList<NativePersonnelDisplayRow> _rows = [];
    private static readonly IReadOnlyList<NativePersonnelStatusOption> Statuses =
    [
        new(1, "على القوة"), new(2, "منقول خارجيا"), new(3, "مفصول"),
        new(4, "مؤرشف"), new(0, "جميع الحالات")
    ];

    public AdministrationPageViewModel() : base(NativeScreen.Administration, "الإدارة")
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanRead);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""), _ => CanRead);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => CanRead && !_loading && _page > 1);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => CanRead && !_loading && _hasNext);
        OpenProfileCommand = new NativeCommand(
            argument => { if (argument is NativePersonnelDisplayRow row) _ = OpenProfileAsync(row); },
            argument => argument is NativePersonnelDisplayRow row && CanOpenProfile(row));
        CloseProfileCommand = new NativeCommand(_ => CloseProfile(), _ => _profileOpen);
        Mutation = new NativePersonnelMutationViewModel();
        Movement = new NativePersonnelTransferViewModel();
        Archive = new NativeMovementArchiveViewModel();
        Separation = new NativePersonnelSeparationViewModel();
        SeparationArchive = new NativeSeparationArchiveViewModel();
        Return = new NativeArchivedReturnViewModel();
        SiteCatalog = new NativeSiteCatalogViewModel();
        StatusCatalog = new NativeStatusCatalogViewModel();
        OpenSiteCatalogCommand = new NativeCommand(
            _ => _ = SiteCatalog.OpenAsync(),
            _ => CanRead && _connected && !_loading && !_profileOpen &&
                 _reader is INativeSiteCatalogBackend &&
                 !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen &&
                 !Separation.IsOpen && !SeparationArchive.IsOpen &&
                 !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen);
        OpenArchivedReturnCommand = new NativeCommand(
            argument => {
                if (argument is NativePersonnelDisplayRow row && CanOpenArchivedReturn(row))
                    Return.Open(row, _sites);
            },
            argument => argument is NativePersonnelDisplayRow row && CanOpenArchivedReturn(row));
        OpenStatusCatalogCommand = new NativeCommand(
            _ => _ = StatusCatalog.OpenAsync(),
            _ => CanRead && _connected && !_loading && !_profileOpen &&
                 _reader is INativeStatusCatalogBackend &&
                 !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen &&
                 !Separation.IsOpen && !SeparationArchive.IsOpen &&
                 !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen);
        OpenSeparationArchiveCommand = new NativeCommand(
            _ => _ = SeparationArchive.OpenAsync(_sites),
            _ => CanRead && _connected && !_loading && !_profileOpen &&
                 !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen &&
                 !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen);
        OpenSeparationCommand = new NativeCommand(
            argument => {
                if (argument is NativePersonnelDisplayRow row && CanOpenSeparation(row))
                    Separation.Open(row, _site, _sites);
            },
            argument => argument is NativePersonnelDisplayRow row && CanOpenSeparation(row));
        OpenMovementArchiveCommand = new NativeCommand(
            _ => _ = Archive.OpenAsync(),
            _ => CanRead && _connected && !_loading && !_profileOpen &&
                 !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen);
        OpenInternalTransferCommand = new NativeCommand(
            argument => {
                if (argument is NativePersonnelDisplayRow person && CanOpenMovement(person))
                    Movement.Open(person, _site, _sites, false);
            },
            argument => argument is NativePersonnelDisplayRow person && CanOpenMovement(person));
        OpenExternalTransferCommand = new NativeCommand(
            argument => {
                if (argument is NativePersonnelDisplayRow person && CanOpenMovement(person))
                    Movement.Open(person, _site, _sites, true);
            },
            argument => argument is NativePersonnelDisplayRow person && CanOpenMovement(person));
        OpenCreateCommand = new NativeCommand(
            _ => { CloseProfile(); Mutation.OpenCreate(_sites); },
            _ => _connected && !_loading && CanRead && !_profileOpen &&
                 !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen);
        OpenEditCommand = new NativeCommand(
            row => {
                if (row is NativePersonnelDisplayRow person && CanOpenEdit(person))
                {
                    CloseProfile();
                    Mutation.OpenEdit(person);
                }
            },
            row => row is NativePersonnelDisplayRow person && CanOpenEdit(person));
    }

    private bool CanRead =>
        _reader is not null && _liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public override bool IsDataConnected => _connected;
    public override string StatusMessage => _loading
        ? "جار قراءة سجل الأفراد المعتمد من قاعدة البيانات المشفرة..."
        : _connected
            ? "سجل الأفراد من SQLCipher. الإضافة والتعديل والنقل والفصل تتطلب معاينة وتأكيداً؛ أرشيف الفصل متاح للقراءة، والعودة بهوية الفرد الأصلية تتطلب معاينة مستقلة وإقرارا صريحا؛ إدارة المواقع والحالات تتطلب مراجعة منفصلة."
            : "إدارة الأفراد لم تتصل بالبيانات بعد أو لا تتوفر صلاحية الإدارة.";
    public bool IsLoading => _loading;
    public string LoadError => _error;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public IReadOnlyList<NativePersonnelStatusOption> StatusOptions => Statuses;
    public IReadOnlyList<NativePersonnelDisplayRow> Rows => _rows;
    public IReadOnlyList<NativeDashboardColumn> CustomColumns => _columns;
    public long TotalRows => _total;
    public bool HasNext => _hasNext;
    public int Page => _page;
    public string PageLabel => $"الصفحة {_page} — إجمالي الأفراد {_total}";
    public string SelectedSiteId => _site;
    public int SelectedStatus => _status;
    public string SearchDraft
    {
        get => _draft;
        set
        {
            if (_draft == value) return;
            _draft = value ?? "";
            Changed(nameof(SearchDraft));
        }
    }
    public bool IsProfileOpen => _profileOpen;
    public bool IsProfileLoading => _profileLoading;
    public string ProfileError => _profileError;
    public NativePersonnelProfile? SelectedProfile => _profile;
    public NativePersonnelMutationViewModel Mutation { get; }
    public NativePersonnelTransferViewModel Movement { get; }
    public NativeMovementArchiveViewModel Archive { get; }
    public NativePersonnelSeparationViewModel Separation { get; }
    public NativeSeparationArchiveViewModel SeparationArchive { get; }
    public NativeArchivedReturnViewModel Return { get; }
    public NativeSiteCatalogViewModel SiteCatalog { get; }
    public NativeStatusCatalogViewModel StatusCatalog { get; }

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand OpenProfileCommand { get; }
    public ICommand CloseProfileCommand { get; }
    public ICommand OpenCreateCommand { get; }
    public ICommand OpenEditCommand { get; }
    public ICommand OpenInternalTransferCommand { get; }
    public ICommand OpenExternalTransferCommand { get; }
    public ICommand OpenMovementArchiveCommand { get; }
    public ICommand OpenSeparationCommand { get; }
    public ICommand OpenSeparationArchiveCommand { get; }
    public ICommand OpenArchivedReturnCommand { get; }
    public ICommand OpenSiteCatalogCommand { get; }
    public ICommand OpenStatusCatalogCommand { get; }

    public void AttachReader(INativePersonnelDirectoryReader reader,
        Func<NativeIdentity?> liveActor)
    {
        if (_reader is not null) throw new InvalidOperationException("Personnel reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        if (reader is INativePersonnelMutationBackend mutationBackend)
        {
            Mutation.AttachBackend(mutationBackend, () => _liveActor?.Invoke(),
                row => _connected && !_loading && !_profileOpen &&
                    _boundActor is { } actor && _liveActor?.Invoke() == actor &&
                    _rows.Any(current => ReferenceEquals(current, row)));
            Mutation.CommittedAndClosed += () => _ = RefreshAsync();
        }
        if (reader is INativePersonnelMovementBackend movementBackend)
        {
            Movement.AttachBackend(movementBackend, () => _liveActor?.Invoke(),
                row => _connected && !_loading && !_profileOpen &&
                    _boundActor is { } actor && _liveActor?.Invoke() == actor &&
                    _rows.Any(current => ReferenceEquals(current, row)));
            Movement.CommittedAndClosed += () => _ = RefreshAsync();
        }
        if (reader is INativeMovementArchiveBackend archiveBackend)
            Archive.AttachBackend(archiveBackend, () => _liveActor?.Invoke());
        if (reader is INativePersonnelSeparationBackend separationBackend)
        {
            Separation.AttachBackend(separationBackend, () => _liveActor?.Invoke(),
                row => _connected && !_loading && !_profileOpen &&
                    _boundActor is { } actor && _liveActor?.Invoke() == actor &&
                    _rows.Any(current => ReferenceEquals(current, row)));
            Separation.CommittedAndClosed += () => _ = RefreshAsync();
        }
        if (reader is INativeSeparationArchiveBackend separationArchiveBackend)
            SeparationArchive.AttachBackend(separationArchiveBackend,
                () => _liveActor?.Invoke());
        if (reader is INativeArchivedReturnBackend returnBackend)
        {
            Return.AttachBackend(returnBackend, () => _liveActor?.Invoke(),
                row => _connected && !_loading && !_profileOpen &&
                    _boundActor is { } actor && _liveActor?.Invoke() == actor &&
                    _rows.Any(current => ReferenceEquals(current, row)));
            Return.CommittedAndClosed += () => _ = RefreshAsync();
        }
        if (reader is INativeSiteCatalogBackend siteBackend)
        {
            SiteCatalog.AttachBackend(siteBackend, () => _liveActor?.Invoke());
            SiteCatalog.CommittedAndClosed += () => _ = RefreshAsync();
            if (reader is INativeSiteRetirementBackend retirementBackend)
                SiteCatalog.AttachRetirementBackend(retirementBackend);
        }
        if (reader is INativeStatusCatalogBackend statusBackend)
        {
            StatusCatalog.AttachBackend(statusBackend, () => _liveActor?.Invoke());
            StatusCatalog.CommittedAndClosed += () => _ = RefreshAsync();
        }
        UpdateCommands();
    }

    public void Clear()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _boundActor = null;
        _catalogueLoaded = false;
        _site = "";
        _term = "";
        _draft = "";
        _page = 1;
        _status = 1;
        _sites = [];
        HideRows("");
        Changed(nameof(SiteOptions), nameof(SearchDraft),
            nameof(SelectedSiteId), nameof(SelectedStatus), nameof(Page));
    }

    private void HideRows(string error)
    {
        CloseProfile();
        Mutation.ForceCancel();
        Movement.ForceCancel();
        Archive.Close();
        Separation.ForceCancel();
        SeparationArchive.Close();
        Return.ForceCancel();
        SiteCatalog.ForceCancel();
        StatusCatalog.ForceCancel();
        _connected = false;
        _loading = false;
        _error = error;
        _hasNext = false;
        _total = 0;
        _rows = [];
        _columns = [];
        Changed(nameof(IsDataConnected), nameof(StatusMessage), nameof(LoadError),
            nameof(IsLoading), nameof(HasNext), nameof(TotalRows), nameof(Rows),
            nameof(CustomColumns), nameof(PageLabel));
        UpdateCommands();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var actor = _liveActor?.Invoke();
        if (_reader is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
        {
            Clear();
            return;
        }
        if (_boundActor is not null && _boundActor != actor)
        {
            Clear();
            HideRows("تغيرت جلسة المستخدم؛ أعد تسجيل الدخول قبل عرض سجلات الأفراد.");
            return;
        }
        _boundActor ??= actor;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        HideRows("");
        _loading = true;
        Changed(nameof(IsLoading), nameof(StatusMessage));
        try
        {
            if (!_catalogueLoaded)
            {
                var catalogue = await _reader.ReadPersonnelCatalogueAsync(actor, linked.Token);
                if (version != _generation || linked.IsCancellationRequested ||
                    _liveActor?.Invoke() != actor) return;
                if (catalogue.Sites.Count > 10000 ||
                    catalogue.Sites.Any(s => string.IsNullOrWhiteSpace(s.SiteId) ||
                        string.IsNullOrWhiteSpace(s.Name) || s.SiteId.Length > 128) ||
                    catalogue.Sites.Select(s => s.SiteId)
                        .Distinct(StringComparer.Ordinal).Count() != catalogue.Sites.Count)
                    throw new InvalidDataException("Invalid SQLCipher personnel sites.");
                _sites = [new("", "جميع المواقع"), .. catalogue.Sites];
                _catalogueLoaded = true;
                Changed(nameof(SiteOptions));
            }

            if (_status is < 0 or > 4 ||
                (_site.Length > 0 && !_sites.Any(x => x.SiteId == _site)) ||
                _term.Length > 100 || _term.Any(char.IsControl) ||
                _page is < 1 or > 100000)
                throw new InvalidDataException("Invalid personnel filter.");
            var filter = new NativePersonnelFilter(_site, _term, _status, _page);
            var page = await _reader.ReadPageAsync(actor, filter, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                _liveActor?.Invoke() != actor) return;
            Validate(page, filter);

            int rank = -1;
            for (int i = 0; i < page.CustomColumns.Count; i++)
                if (page.CustomColumns[i].Caption.Trim() is "الرتبة" or "رتبة")
                {
                    rank = i; break;
                }
            _rows = page.Rows.Select(person => new NativePersonnelDisplayRow(
                person.RowNumber, person.PersonId, person.ServiceNumberA,
                rank < 0 ? "" : person.CustomCells[rank],
                person.Name, person.SiteName, StatusName(person.OrganizationalStatus),
                person.Active, person.Revision,
                person.CustomCells.Where((_, index) => index != rank).ToArray())).ToArray();
            _columns = page.CustomColumns.Where((_, index) => index != rank).ToArray();
            _total = page.TotalRows;
            _hasNext = page.HasNext;
            _loading = false;
            _connected = true;
            _error = "";
            Changed(nameof(Rows), nameof(CustomColumns), nameof(TotalRows),
                nameof(HasNext), nameof(IsDataConnected), nameof(IsLoading),
                nameof(LoadError), nameof(PageLabel), nameof(StatusMessage));
            UpdateCommands();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested || ct.IsCancellationRequested)
        {
            if (version == _generation) HideRows("");
        }
        catch
        {
            if (version == _generation)
                HideRows("تعذرت قراءة سجلات الأفراد المعتمدة؛ أُخفيت النتائج السابقة حماية للبيانات.");
        }
        finally
        {
            if (version == _generation) _pending = null;
        }
    }

    public async Task SearchAsync(string? term, CancellationToken ct = default)
    {
        string normalized = (term ?? "").Trim();
        if (normalized.Length > 100 || normalized.Any(char.IsControl))
        {
            Invalidate("البحث لا يقبل أكثر من 100 حرف أو أحرف التحكم.");
            return;
        }
        _term = normalized;
        _draft = normalized;
        _page = 1;
        Changed(nameof(SearchDraft), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SelectSiteAsync(string? siteId, CancellationToken ct = default)
    {
        siteId ??= "";
        if (siteId.Length > 0 && !_sites.Any(x => x.SiteId == siteId))
        {
            Invalidate("الموقع المختار ليس ضمن المواقع المعتمدة.");
            return;
        }
        if (siteId == _site) return;
        _site = siteId;
        _page = 1;
        Changed(nameof(SelectedSiteId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SelectStatusAsync(int status, CancellationToken ct = default)
    {
        if (status is < 0 or > 4)
        {
            Invalidate("حالة الفرد المطلوبة غير معروفة.");
            return;
        }
        if (status == _status) return;
        _status = status;
        _page = 1;
        Changed(nameof(SelectedStatus), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (_loading || !_hasNext || _page >= 100000) return;
        ++_page;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (_loading || _page <= 1) return;
        --_page;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    private bool CanOpenArchivedReturn(NativePersonnelDisplayRow? row) =>
        row is not null && !row.Active &&
        row.StatusName is ("منقول خارجيا" or "مفصول" or "مؤرشف") &&
        row.Revision >= 1 &&
        _status is 0 or 2 or 3 or 4 &&
        _connected && !_loading && !_profileOpen &&
        !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen &&
        !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen &&
        _reader is INativeArchivedReturnBackend &&
        _rows.Any(current => ReferenceEquals(current, row)) &&
        _boundActor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private bool CanOpenSeparation(NativePersonnelDisplayRow? row) =>
        row is not null && row.Active && row.StatusName == "على القوة" &&
        row.Revision >= 1 && _status == 1 && _site.Length > 0 &&
        _sites.Count(site => site.SiteId == _site && site.Name == row.SiteName) == 1 &&
        _connected && !_loading && !_profileOpen &&
        !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen &&
        _reader is INativePersonnelSeparationBackend &&
        _rows.Any(current => ReferenceEquals(current, row)) &&
        _boundActor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private bool CanOpenMovement(NativePersonnelDisplayRow? row) =>
        row is not null && row.Active && row.StatusName == "على القوة" &&
        row.Revision >= 1 &&
        _status == 1 && _site.Length > 0 &&
        _sites.Count(site => site.SiteId == _site &&
                             site.Name == row.SiteName) == 1 &&
        _connected && !_loading && !_profileOpen &&
        !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen &&
        _reader is INativePersonnelMovementBackend &&
        _rows.Any(current => ReferenceEquals(current, row)) &&
        _boundActor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private bool CanOpenEdit(NativePersonnelDisplayRow? row) =>
        row is not null && row.Active && row.Revision >= 1 &&
        _connected && !_loading && !_profileOpen &&
        !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen &&
        _reader is INativePersonnelMutationBackend &&
        _rows.Any(current => ReferenceEquals(current, row)) &&
        _boundActor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    private bool CanOpenProfile(NativePersonnelDisplayRow? row) =>
        _connected && !_loading && !_profileOpen &&
        !Mutation.IsOpen && !Movement.IsOpen && !Archive.IsOpen && !Separation.IsOpen && !SeparationArchive.IsOpen && !Return.IsOpen && !SiteCatalog.IsOpen && !StatusCatalog.IsOpen &&
        _reader is not null && row is not null &&
        _rows.Any(current => ReferenceEquals(current, row)) &&
        _boundActor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);

    public async Task OpenProfileAsync(NativePersonnelDisplayRow? row,
        CancellationToken ct = default)
    {
        if (_boundActor is not null && _liveActor?.Invoke() != _boundActor)
        {
            Clear(); // cross-session personnel must be scrubbed immediately
            return;
        }
        if (!CanOpenProfile(row) || _reader is null || row is null ||
            _boundActor is not { } actor)
            return;
        int version = ++_profileGeneration;
        _profilePending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _profilePending = linked;
        _profileOpen = true;
        _profileLoading = true;
        _profileError = "";
        _profile = null;
        NotifyProfile();
        try
        {
            NativePersonnelProfile profile =
                await _reader.ReadProfileAsync(actor, row.PersonId, linked.Token);
            if (version != _profileGeneration || linked.IsCancellationRequested) return;
            if (_liveActor?.Invoke() != actor ||
                !_connected || !_rows.Any(x => ReferenceEquals(x, row)))
            {
                Clear();
                return;
            }
            if (profile.PersonId != row.PersonId ||
                profile.Name != row.Name ||
                profile.ServiceNumberA != row.ServiceNumberA ||
                profile.Revision != row.Revision ||
                profile.OrganizationalStatus is < 1 or > 4 ||
                profile.IdentityHistory.Count > 50)
                throw new InvalidDataException("Stale or forged personnel profile.");
            _profile = profile;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _profileGeneration)
            {
                _profile = null;
                _profileError = "تعذرت قراءة الملف الفعلي للفرد؛ أعد تحديث الكشف قبل المحاولة.";
            }
        }
        finally
        {
            if (version == _profileGeneration)
            {
                _profilePending = null;
                _profileLoading = false;
                NotifyProfile();
            }
        }
    }

    public void CloseProfile()
    {
        ++_profileGeneration;
        _profilePending?.Cancel();
        _profilePending = null;
        _profileOpen = false;
        _profileLoading = false;
        _profileError = "";
        _profile = null;
        NotifyProfile();
    }

    private void Invalidate(string error)
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        HideRows(error);
    }

    private static string StatusName(int status) => status switch
    {
        1 => "على القوة", 2 => "منقول خارجيا",
        3 => "مفصول", 4 => "مؤرشف",
        _ => "غير معروف"
    };

    private static void Validate(NativePersonnelSnapshot page, NativePersonnelFilter filter)
    {
        if (page.SiteId != filter.SiteId ||
            page.SearchTerm != filter.SearchTerm ||
            page.OrganizationalStatus != filter.OrganizationalStatus ||
            page.Page != filter.Page || page.TotalRows < 0 ||
            page.Rows.Count > filter.PageSize || page.Rows.Count > page.TotalRows ||
            page.HasNext != ((long)filter.Page * filter.PageSize < page.TotalRows) ||
            page.CustomColumns.Count > 100 ||
            page.CustomColumns.Select(x => x.ColumnId)
                .Distinct(StringComparer.Ordinal).Count() != page.CustomColumns.Count ||
            page.Rows.Any(row =>
                row.PersonId.Length is < 1 or > 128 ||
                string.IsNullOrWhiteSpace(row.Name) ||
                string.IsNullOrWhiteSpace(row.ServiceNumberA) ||
                row.OrganizationalStatus is < 1 or > 4 ||
                row.Revision < 0 || row.CustomCells.Count != page.CustomColumns.Count) ||
            page.Rows.Select(row => row.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != page.Rows.Count)
            throw new InvalidDataException("Canonical personnel page mismatch.");
    }

    private void NotifyProfile()
    {
        Changed(nameof(IsProfileOpen), nameof(IsProfileLoading),
            nameof(ProfileError), nameof(SelectedProfile));
        ((NativeCommand)OpenProfileCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseProfileCommand).NotifyAvailabilityChanged();
    }

    private void UpdateCommands()
    {
        foreach (ICommand command in new[] {
            RefreshCommand, SearchCommand, ClearSearchCommand,
            PreviousPageCommand, NextPageCommand, OpenProfileCommand,
            OpenCreateCommand, OpenEditCommand,
            OpenInternalTransferCommand, OpenExternalTransferCommand,
            OpenMovementArchiveCommand, OpenSeparationCommand,
            OpenSeparationArchiveCommand, OpenArchivedReturnCommand,
            OpenSiteCatalogCommand, OpenStatusCatalogCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }

    private void Changed(params string[] names)
    {
        foreach (string name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
