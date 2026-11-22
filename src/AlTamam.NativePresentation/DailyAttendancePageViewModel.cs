using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 6 first slice: native, read-only daily attendance. Reuses exactly the
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
    private bool _loading, _connected, _hasNext;
    private string _error = "", _selectedSite = "", _selectedStatus = "";
    private string _searchDraft = "", _searchCommitted = "", _day = "";
    private int _page = 1;
    private long _total;
    private IReadOnlyList<NativeDailyAttendanceRow> _rows = [];
    private IReadOnlyList<NativeDashboardColumn> _customColumns = [];
    private IReadOnlyList<NativeDashboardSite> _sites = [new("", "جميع المواقع")];
    private IReadOnlyList<NativeDailyStatusOption> _statuses = [new("", "جميع الحالات")];

    public DailyAttendancePageViewModel()
        : base(NativeScreen.DailyAttendance, "التمام اليومي")
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanRead);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""), _ => CanRead);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => CanRead && !IsLoading && Page > 1);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => CanRead && !IsLoading && HasNext);
    }

    private bool CanRead => _currentPrincipal?.Invoke() is not null && _reader is not null;
    public override bool IsDataConnected => _connected;
    public override string StatusMessage => IsLoading
        ? "جار قراءة كشف التمام اليومي من قاعدة البيانات المشفرة..."
        : IsDataConnected
            ? "كشف التمام اليومي الفعلي — عرض وبحث فقط. التحضير والتعديل يستكملان في المرحلة التالية."
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
            if (_searchDraft == value) return;
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
    public int Page => _page;
    public bool HasNext => _hasNext;
    public string PageLabel => $"الصفحة {_page} — إجمالي النتائج {_total}";

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    public void AttachReader(INativeDashboardReader reader, Func<NativeIdentity?> principal)
    {
        if (_reader is not null) throw new InvalidOperationException("Native daily roster reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _currentPrincipal = principal ?? throw new ArgumentNullException(nameof(principal));
    }

    /// <summary>Immediately remove personnel and revoke late responses on logout.</summary>
    public void Clear()
    {
        ++_version;
        _activeRead?.Cancel();
        _activeRead = null;
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
            nameof(SearchDraft), nameof(Page), nameof(SiteOptions), nameof(StatusOptions));
    }

    private void ClearSensitive(string error)
    {
        _loading = false;
        _connected = false;
        _error = error;
        _day = "";
        _total = 0;
        _hasNext = false;
        _rows = [];
        _customColumns = [];
        Changed(nameof(IsDataConnected), nameof(IsLoading), nameof(StatusMessage),
            nameof(LoadError), nameof(BusinessDateLabel), nameof(TotalRows),
            nameof(PageLabel), nameof(HasNext), nameof(Rows), nameof(CustomColumns));
        NotifyCommands();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
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
                    person.RowNumber, person.ServiceNumberA,
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
        if (!_hasNext || _loading) return;
        _page++;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
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
    int RowNumber, string ServiceNumberA, string Rank, string Name,
    string SiteName, string StatusName, IReadOnlyList<string> CustomCells);
