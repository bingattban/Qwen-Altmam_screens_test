using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native daily attendance with explicit reviewed mutations. Reuses exactly the
/// already-authorized SQLCipher DashboardSummary / QueryDailyRoster service.
/// Changing site/status/search/page never opens a write or changes attendance.
/// </summary>
public sealed class DailyAttendancePageViewModel : NativePageViewModel, INotifyPropertyChanged
{
    private INativeDashboardReader? _reader;
    private Func<NativeIdentity?>? _currentPrincipal;
    private NativeIdentity? _boundPrincipal;
    private CancellationTokenSource? _activeRead;
    private int _version;
    private DateOnly? _loadedDate;
    private bool _reviewAttached;
    private bool _loading, _connected, _hasNext;
    private string _error = "", _selectedSite = "", _selectedStatus = "";
    private string _searchDraft = "", _searchCommitted = "", _day = "";
    private int _page = 1;
    private long _total;
    private IReadOnlyList<NativeDailyAttendanceRow> _rows = [];
    private IReadOnlyList<NativeDailyAttendanceRow> _selected = [];
    private string _selectionError = "";
    public NativeAttendanceReviewViewModel Review { get; } = new();
    private IReadOnlyList<NativeDashboardColumn> _customColumns = [];
    private IReadOnlyList<NativeDashboardSite> _sites = [new("", "جميع المواقع")];
    private IReadOnlyList<NativeDailyStatusOption> _statuses = [new("", "جميع الحالات")];

    public DailyAttendancePageViewModel()
        : base(NativeScreen.DailyAttendance, "التمام اليومي")
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanChangeRosterScope);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanChangeRosterScope);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""), _ => CanChangeRosterScope);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => CanChangeRosterScope && !IsLoading && Page > 1);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => CanChangeRosterScope && !IsLoading && HasNext);
        ReviewPersonCommand = new NativeCommand(
            parameter =>
            {
                if (parameter is NativeDailyAttendanceRow row)
                    _ = OpenReviewAsync(row);
            },
            parameter => parameter is NativeDailyAttendanceRow row && CanReviewPerson(row));
        ReviewSelectedCommand = new NativeCommand(_ => _ = OpenSelectedReviewAsync(),
            _ => CanReviewSelected());
        Review.Committed += receipt => { _ = RefreshAfterVerifiedCommitAsync(); };
        Review.PropertyChanged += (_, _) =>
        {
            Changed(nameof(CanChangeRosterScope));
            NotifyCommands();
        };
    }

    private bool CanRead => _currentPrincipal?.Invoke() is not null && _reader is not null;
    public bool CanChangeRosterScope => CanRead && !Review.IsOpen &&
        !Review.IsBusy && !Review.IsCommitting && !Review.HasUnverifiedCommit;
    public override bool IsDataConnected => _connected;
    public override string StatusMessage => IsLoading
        ? "جار قراءة كشف التمام اليومي من قاعدة البيانات المشفرة..."
        : IsDataConnected
            ? "كشف التمام اليومي الفعلي — اختر فردا لمعاينة تغيير حالته؛ لا تعديل دون تأكيد صريح."
            : "لم يتم تحميل كشف التمام اليومي المعتمد بعد؛ افتح الصفحة أو اضغط تحديث البيانات.";
    public bool IsLoading => _loading;
    public string LoadError => _error;
    public string BusinessDateLabel => _day;
    public string RankColumnLabel => "الرتبة";
    public string SearchDraft
    {
        get => _searchDraft;
        set
        {
            if (!CanChangeRosterScope || _searchDraft == value) return;
            _searchDraft = value ?? "";
            Changed(nameof(SearchDraft));
        }
    }
    public string SelectedSiteId => _selectedSite;
    public string SelectedStatusId => _selectedStatus;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public IReadOnlyList<NativeDailyStatusOption> StatusOptions => _statuses;
    public IReadOnlyList<NativeDashboardColumn> CustomColumns => _customColumns;
    public IReadOnlyList<NativeDailyAttendanceRow> Rows => _rows;
    public long TotalRows => _total;
    public int SelectedCount => _selected.Count;
    public string SelectedLabel => $"المحددون في الصفحة: {_selected.Count} من 100";
    public string SelectionError => _selectionError;
    public int Page => _page;
    public bool HasNext => _hasNext;
    public string PageLabel => $"الصفحة {_page} — إجمالي النتائج {_total}";

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand ReviewPersonCommand { get; }
    public ICommand ReviewSelectedCommand { get; }

    public void AttachReader(INativeDashboardReader reader, Func<NativeIdentity?> principal)
    {
        if (_reader is not null) throw new InvalidOperationException("Native daily roster reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _currentPrincipal = principal ?? throw new ArgumentNullException(nameof(principal));
        if (reader is INativeAttendanceReviewBackend backend)
        {
            Review.AttachBackend(backend, principal);
            _reviewAttached = true;
        }
    }

    private bool CanReviewPerson(NativeDailyAttendanceRow? row)
    {
        var live = _currentPrincipal?.Invoke();
        return row is not null && _reviewAttached && _connected && !_loading &&
            !Review.IsOpen && _loadedDate == DateOnly.FromDateTime(DateTime.Now) &&
            live is not null && live == _boundPrincipal &&
            _rows.Any(item => ReferenceEquals(item, row)) &&
            !string.IsNullOrWhiteSpace(row.PersonId);
    }

    // The original audited SQLCipher preview/commit implementation is the
    // ONLY writer. Never trust a row supplied outside the current live page.
    public Task OpenReviewAsync(NativeDailyAttendanceRow row, CancellationToken ct = default)
    {
        if (!CanReviewPerson(row)) return Task.CompletedTask;
        return Review.OpenPersonsAsync(_loadedDate!.Value, _selectedSite, [row.PersonId], ct);
    }

    /// <summary>
    /// The WPF DataGrid owns only the visual selection. This model accepts
    /// references from the CURRENT authenticated page, not forged IDs, copies,
    /// stale pages or duplicate members. Selection never writes to SQLCipher.
    /// </summary>
    public bool SetSelectedRows(IReadOnlyList<NativeDailyAttendanceRow>? selection)
    {
        // Never replace an already previewed/possibly committed intent.
        if (Review.IsOpen || Review.IsBusy || Review.IsCommitting ||
            Review.HasUnverifiedCommit) return false;

        NativeIdentity? live = _currentPrincipal?.Invoke();
        if (!_connected || _loading || !_reviewAttached || live is null ||
            live != _boundPrincipal || _loadedDate != DateOnly.FromDateTime(DateTime.Now))
        {
            ResetSelection("");
            return false;
        }

        if (selection is null || selection.Count > 100 ||
            selection.Any(row => row is null ||
                !_rows.Any(item => ReferenceEquals(item, row))) ||
            selection.Select(row => row.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != selection.Count)
        {
            ResetSelection("التحديد غير صالح أو يتجاوز 100 فرد؛ أعد التحديد من الكشف الحالي.");
            return false;
        }

        _selected = selection.ToArray();
        _selectionError = "";
        NotifySelection();
        return true;
    }

    private bool CanReviewSelected() =>
        _selected.Count is >= 1 and <= 100 &&
        _selected.All(row => CanReviewPerson(row));

    public Task OpenSelectedReviewAsync(CancellationToken ct = default)
    {
        if (!CanReviewSelected()) return Task.CompletedTask;
        return Review.OpenPersonsAsync(_loadedDate!.Value, _selectedSite,
            _selected.Select(row => row.PersonId).ToArray(), ct);
    }

    private void ResetSelection(string error)
    {
        _selected = [];
        _selectionError = error;
        NotifySelection();
    }

    private void NotifySelection()
    {
        Changed(nameof(SelectedCount), nameof(SelectedLabel), nameof(SelectionError));
        ((NativeCommand)ReviewSelectedCommand).NotifyAvailabilityChanged();
    }

    /// <summary>Immediately remove personnel and revoke late responses on logout.</summary>
    public void Clear()
    {
        ++_version;
        _activeRead?.Cancel();
        _activeRead = null;
        Review.ForceCancel();
        _boundPrincipal = null;
        _selectedSite = "";
        _selectedStatus = "";
        _searchDraft = "";
        _searchCommitted = "";
        _page = 1;
        _sites = [new("", "جميع المواقع")];
        _statuses = [new("", "جميع الحالات")];
        ClearSensitive("");
        Changed(nameof(SelectedSiteId), nameof(SelectedStatusId),
            nameof(SearchDraft), nameof(Page), nameof(SiteOptions), nameof(StatusOptions),
            nameof(CanChangeRosterScope));
    }

    private void ClearSensitive(string error)
    {
        _loading = false;
        _connected = false;
        _error = error;
        _day = "";
        _loadedDate = null;
        _total = 0;
        _hasNext = false;
        _rows = [];
        _customColumns = [];
        ResetSelection("");
        Changed(nameof(IsDataConnected), nameof(IsLoading), nameof(StatusMessage),
            nameof(LoadError), nameof(BusinessDateLabel), nameof(TotalRows),
            nameof(PageLabel), nameof(HasNext), nameof(Rows), nameof(CustomColumns));
        NotifyCommands();
    }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        // Logout and replacement must still clear sensitive data, even with a review open.
        if (_currentPrincipal?.Invoke() is null ||
            (_boundPrincipal is not null && _boundPrincipal != _currentPrincipal?.Invoke()))
            return RefreshCoreAsync(ct);
        return CanChangeRosterScope ? RefreshCoreAsync(ct) : Task.CompletedTask;
    }

    private Task RefreshAfterVerifiedCommitAsync() =>
        Review.IsCompleted && !Review.HasUnverifiedCommit
            ? RefreshCoreAsync(CancellationToken.None) : Task.CompletedTask;

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        NativeIdentity? principal = _currentPrincipal?.Invoke();
        if (principal is null || _reader is null) { Clear(); return; }
        if (_boundPrincipal is not null && _boundPrincipal != principal)
        {
            Clear();
            ClearSensitive("تغيرت هوية الجلسة؛ سجّل الدخول مجددا.");
            return;
        }
        _boundPrincipal ??= principal;
        int request = ++_version;
        _activeRead?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeRead = linked;
        var filter = new NativeDashboardFilter(
            DateOnly.FromDateTime(DateTime.Now), _selectedSite, _selectedStatus,
            _searchCommitted, _page, 100);

        ClearSensitive("");
        _loading = true;
        Changed(nameof(IsLoading), nameof(StatusMessage));
        NotifyCommands();
        try
        {
            var snapshot = await _reader.ReadAsync(principal, filter, linked.Token);
            if (request != _version || linked.IsCancellationRequested ||
                _currentPrincipal?.Invoke() != principal) return;
            Validate(snapshot, filter);
            int rankIndex = -1;
            for (int i = 0; i < snapshot.CustomColumns.Count; i++)
                if (snapshot.CustomColumns[i].Caption.Trim() is "الرتبة" or "رتبة")
                {
                    rankIndex = i;
                    break;
                }

            _rows = snapshot.Rows.Select(person =>
                new NativeDailyAttendanceRow(
                    person.PersonId, person.RowNumber, person.ServiceNumberA,
                    rankIndex < 0 ? "" : person.CustomCells[rankIndex],
                    person.Name, person.SiteName, person.StatusName,
                    person.CustomCells.Where((_, i) => i != rankIndex).ToArray())).ToArray();
            _customColumns = snapshot.CustomColumns
                .Where((_, i) => i != rankIndex).ToArray();
            _sites = [new("", "جميع المواقع"), .. snapshot.Sites];
            _statuses =
            [
                new("", "جميع الحالات"),
                .. snapshot.StatusCounts.Select(x => new NativeDailyStatusOption(x.StatusId, x.Name)),
                new("__UNPREPARED__", "غير المحضرين"),
                new("__AUTO_PREPARED__", "المحضرون تلقائيا"),
                new("__TRANSFER_DAY__", "المنقولون اليوم")
            ];
            _total = snapshot.TotalRows;
            _hasNext = snapshot.HasNext;
            _day = snapshot.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _loadedDate = snapshot.BusinessDate;
            _connected = true;
            _error = "";
            _loading = false;
            Changed(nameof(SiteOptions), nameof(StatusOptions));
            Changed(nameof(IsDataConnected), nameof(StatusMessage), nameof(IsLoading),
                nameof(LoadError), nameof(BusinessDateLabel),
                nameof(Rows), nameof(CustomColumns), nameof(TotalRows),
                nameof(PageLabel), nameof(HasNext));
            NotifyCommands();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested || ct.IsCancellationRequested)
        {
            if (request == _version) ClearSensitive("");
        }
        catch
        {
            if (request == _version)
                ClearSensitive("تعذر قراءة كشف التمام اليومي المعتمد؛ لا تُعرض بيانات قديمة أو تقديرية.");
        }
        finally
        {
            if (request == _version) _activeRead = null;
        }
    }

    public async Task SelectSiteAsync(string siteId, CancellationToken ct = default)
    {
        if (!CanChangeRosterScope) return;
        siteId ??= "";
        if (siteId != "" && !_sites.Any(x => x.SiteId == siteId))
        {
            Invalidate("الموقع المحدد غير موجود في المواقع المعتمدة.");
            return;
        }
        if (_selectedSite == siteId) return;
        _selectedSite = siteId;
        _page = 1;
        Changed(nameof(SelectedSiteId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SelectStatusAsync(string statusId, CancellationToken ct = default)
    {
        if (!CanChangeRosterScope) return;
        statusId ??= "";
        if (statusId != "" && !_statuses.Any(x => x.StatusId == statusId))
        {
            Invalidate("الحالة المحددة غير معتمدة لهذا الكشف.");
            return;
        }
        if (_selectedStatus == statusId) return;
        _selectedStatus = statusId;
        _page = 1;
        Changed(nameof(SelectedStatusId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SearchAsync(string search, CancellationToken ct = default)
    {
        if (!CanChangeRosterScope) return;
        string term = (search ?? "").Trim();
        if (term.Length > 100 || term.Any(char.IsControl))
        {
            Invalidate("البحث لا يتجاوز 100 حرف ولا يسمح بأحرف التحكم.");
            return;
        }
        _searchDraft = term;
        _searchCommitted = term;
        _page = 1;
        Changed(nameof(SearchDraft), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (!CanChangeRosterScope) return;
        if (!_hasNext || _loading) return;
        _page++;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (!CanChangeRosterScope) return;
        if (_page <= 1 || _loading) return;
        _page--;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    private void Invalidate(string message)
    {
        ++_version;
        _activeRead?.Cancel();
        _activeRead = null;
        ClearSensitive(message);
    }

    private static void Validate(NativeDashboardSnapshot snapshot, NativeDashboardFilter filter)
    {
        if (snapshot.BusinessDate != filter.BusinessDate || snapshot.SiteId != filter.SiteId ||
            snapshot.Page != filter.Page || snapshot.TotalRows < 0 ||
            snapshot.Rows.Count > filter.PageSize || snapshot.TotalRows < snapshot.Rows.Count ||
            snapshot.HasNext != ((long)filter.Page * filter.PageSize < snapshot.TotalRows) ||
            snapshot.Rows.Any(row => row.CustomCells.Count != snapshot.CustomColumns.Count ||
                                     string.IsNullOrWhiteSpace(row.PersonId)) ||
            snapshot.Rows.Select(row => row.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != snapshot.Rows.Count ||
            snapshot.Sites.Any(site => string.IsNullOrWhiteSpace(site.SiteId)) ||
            snapshot.CustomColumns.Any(col => string.IsNullOrWhiteSpace(col.ColumnId)) ||
            snapshot.StatusCounts.Any(status => status.Count < 0))
            throw new InvalidDataException("The canonical daily roster response is inconsistent.");
    }

    private void NotifyCommands()
    {
        ((NativeCommand)RefreshCommand).NotifyAvailabilityChanged();
        ((NativeCommand)SearchCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ClearSearchCommand).NotifyAvailabilityChanged();
        ((NativeCommand)PreviousPageCommand).NotifyAvailabilityChanged();
        ((NativeCommand)NextPageCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ReviewPersonCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ReviewSelectedCommand).NotifyAvailabilityChanged();
    }

    private void Changed(params string[] properties)
    {
        foreach (string property in properties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record NativeDailyStatusOption(string StatusId, string DisplayName);

public sealed record NativeDailyAttendanceRow(
    string PersonId, int RowNumber, string ServiceNumberA, string Rank, string Name,
    string SiteName, string StatusName, IReadOnlyList<string> CustomCells);
