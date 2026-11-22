using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native monthly WPF grid, displaying ONLY archived SQLCipher attendance
/// snapshots and movement annotations. No client-side attendance calculation,
/// no writes, and no re-use of today's name as historical identity.
/// </summary>
public sealed class MonthlyAttendancePageViewModel : NativePageViewModel, INotifyPropertyChanged
{
    private INativeMonthlyAttendanceReader? _reader;
    private INativeMonthlyCellDetailsReader? _detailsReader;
    private CancellationTokenSource? _detailPending;
    private int _detailGeneration;
    private bool _detailOpen, _detailLoading;
    private string _detailError = "";
    private NativeMonthlyCellDetails? _selectedDetail;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _boundActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private string _period = CurrentMonth(), _site = "", _searchDraft = "", _term = "";
    private bool _loading, _connected, _hasNext, _catalogueLoaded;
    private string _error = "";
    private int _page = 1;
    private long _total;
    private IReadOnlyList<NativeMonthlyPeriodOption> _periods = [];
    private IReadOnlyList<NativeDashboardSite> _sites = [];
    private IReadOnlyList<NativeDashboardColumn> _extraColumns = [];
    private IReadOnlyList<NativeMonthlyDisplayRow> _rows = [];
    private IReadOnlyList<int> _days = [];

    public MonthlyAttendancePageViewModel()
        : base(NativeScreen.MonthlyAttendance, "التمام الشهري")
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => CanRead);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""), _ => CanRead);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => CanRead && !_loading && _page > 1);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => CanRead && !_loading && _hasNext);
        CloseCellDetailsCommand = new NativeCommand(_ => CloseCellDetails(),
            _ => IsCellDetailsOpen);
    }

    private bool CanRead => _reader is not null && _liveActor?.Invoke() is not null;
    public override bool IsDataConnected => _connected;
    public override string StatusMessage => IsLoading
        ? "جار قراءة كشف التمام الشهري المعتمد من قاعدة البيانات المشفرة..."
        : IsDataConnected
            ? "عرض أرشيف الشهر الفعلي: خلايا الحضور والتنقلات من سجلاتها التاريخية. الرتبة والحقول الإضافية تعرض القيم الحالية فقط."
            : "لم يتم تحميل أرشيف الشهر بعد؛ الشاشة للقراءة فقط ولا تنشئ أو تغير الحضور من هذا الجدول.";
    public bool IsLoading => _loading;
    public bool IsCellDetailsOpen => _detailOpen;
    public bool IsCellDetailsLoading => _detailLoading;
    public string CellDetailsError => _detailError;
    public NativeMonthlyCellDetails? SelectedCellDetails => _selectedDetail;
    public string SelectedCellDateLabel =>
        _selectedDetail?.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public string LoadError => _error;
    public string RankColumnLabel => "الرتبة";
    public string SelectedPeriod => _period;
    public string SelectedSiteId => _site;
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
    public IReadOnlyList<NativeMonthlyPeriodOption> Periods => _periods;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public IReadOnlyList<NativeDashboardColumn> CustomColumns => _extraColumns;
    public IReadOnlyList<NativeMonthlyDisplayRow> Rows => _rows;
    public IReadOnlyList<int> Days => _days;
    public long TotalRows => _total;
    public int Page => _page;
    public bool HasNext => _hasNext;
    public string PageLabel => $"الشهر {_period} — الصفحة {_page} — إجمالي النتائج {_total}";
    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand CloseCellDetailsCommand { get; }

    private static string CurrentMonth() =>
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static bool ValidPeriod(string? period) =>
        period is { Length: 7 } &&
        DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
        date.Year is >= 1900 and <= 2200 &&
        string.CompareOrdinal(period, CurrentMonth()) <= 0;

    public void AttachReader(INativeMonthlyAttendanceReader reader,
        Func<NativeIdentity?> liveActor)
    {
        if (_reader is not null) throw new InvalidOperationException("Monthly reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _detailsReader = reader as INativeMonthlyCellDetailsReader;
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public async Task OpenCellDetailsAsync(NativeMonthlyDisplayRow? row, int day,
        CancellationToken ct = default)
    {
        if (!_connected || _loading || _detailOpen ||
            _detailsReader is null || row is null ||
            !_rows.Any(item => ReferenceEquals(item, row)) ||
            day < 1 || day > _days.Count ||
            !DateOnly.TryParseExact(_period + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstDate))
            return;

        NativeIdentity? actor = _liveActor?.Invoke();
        if (actor is null || actor != _boundActor)
        {
            Clear(); // do not expose stale monthly personnel across accounts
            return;
        }
        DateOnly date = firstDate.AddDays(day - 1);
        if (date > DateOnly.FromDateTime(DateTime.Now)) return;

        int version = ++_detailGeneration;
        _detailPending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _detailPending = linked;
        _detailOpen = true;
        _detailLoading = true;
        _detailError = "";
        _selectedDetail = null;
        NotifyDetail();
        try
        {
            NativeMonthlyCellDetails result = await _detailsReader.ReadCellDetailsAsync(
                actor, new NativeMonthlyCellRequest(row.PersonId, date, _site),
                linked.Token);
            if (version != _detailGeneration || linked.IsCancellationRequested) return;
            if (_liveActor?.Invoke() != actor ||
                !ReferenceEquals(_boundActor, actor) &&
                _boundActor != actor)
            {
                Clear();
                return;
            }
            if (!_connected || !_rows.Any(x => ReferenceEquals(x, row)) ||
                result.PersonId != row.PersonId || result.BusinessDate != date ||
                string.IsNullOrWhiteSpace(result.PersonName) ||
                string.IsNullOrWhiteSpace(result.ServiceNumberA) ||
                string.IsNullOrWhiteSpace(result.SiteName) ||
                result.Revisions.Count > 100)
                throw new InvalidDataException("Canonical monthly historical detail mismatch.");
            _selectedDetail = result;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _detailGeneration)
            {
                _selectedDetail = null;
                _detailError = "تعذرت قراءة تفاصيل اليوم المعتمدة؛ لم تُعرض بيانات قديمة أو تقديرية.";
            }
        }
        finally
        {
            if (version == _detailGeneration)
            {
                _detailPending = null;
                _detailLoading = false;
                NotifyDetail();
            }
        }
    }

    public void CloseCellDetails()
    {
        ++_detailGeneration;
        _detailPending?.Cancel();
        _detailPending = null;
        _selectedDetail = null;
        _detailError = "";
        _detailOpen = false;
        _detailLoading = false;
        NotifyDetail();
    }

    private void NotifyDetail()
    {
        Changed(nameof(IsCellDetailsOpen), nameof(IsCellDetailsLoading),
            nameof(CellDetailsError), nameof(SelectedCellDetails),
            nameof(SelectedCellDateLabel));
        ((NativeCommand)CloseCellDetailsCommand).NotifyAvailabilityChanged();
    }

    public void Clear()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _boundActor = null;
        _catalogueLoaded = false;
        _period = CurrentMonth();
        _site = "";
        _term = "";
        _searchDraft = "";
        _page = 1;
        _periods = [];
        _sites = [];
        HideRows("");
        Changed(nameof(Periods), nameof(SiteOptions),
            nameof(SelectedPeriod), nameof(SelectedSiteId),
            nameof(SearchDraft), nameof(Page));
    }

    private void HideRows(string message)
    {
        CloseCellDetails();
        _connected = false;
        _loading = false;
        _error = message;
        _total = 0;
        _hasNext = false;
        _rows = [];
        _days = [];
        _extraColumns = [];
        Changed(nameof(IsDataConnected), nameof(IsLoading), nameof(StatusMessage),
            nameof(LoadError), nameof(TotalRows), nameof(Rows), nameof(Days),
            nameof(CustomColumns), nameof(PageLabel), nameof(HasNext));
        UpdateCommands();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_reader is null || actor is null) { Clear(); return; }
        if (_boundActor is not null && _boundActor != actor)
        {
            Clear();
            HideRows("تغيرت جلسة المستخدم؛ أعد تسجيل الدخول قبل قراءة الأرشيف.");
            return;
        }
        _boundActor ??= actor;
        int request = ++_generation;
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
                NativeMonthlyCatalogue catalogue =
                    await _reader.ReadCatalogueAsync(actor, linked.Token);
                if (request != _generation || linked.IsCancellationRequested ||
                    _liveActor?.Invoke() != actor) return;

                if (catalogue.Periods.Count > 5000 ||
                    catalogue.Sites.Count > 10000 ||
                    catalogue.Periods.Any(p => !ValidPeriod(p)) ||
                    catalogue.Periods.Distinct(StringComparer.Ordinal).Count() != catalogue.Periods.Count ||
                    catalogue.Sites.Any(s => string.IsNullOrWhiteSpace(s.SiteId)) ||
                    catalogue.Sites.Select(s => s.SiteId)
                        .Distinct(StringComparer.Ordinal).Count() != catalogue.Sites.Count)
                    throw new InvalidDataException("Untrusted archived monthly catalogue.");

                _periods =
                [
                    .. catalogue.Periods.Append(CurrentMonth())
                        .Distinct(StringComparer.Ordinal)
                        .OrderByDescending(x => x, StringComparer.Ordinal)
                        .Select(x => new NativeMonthlyPeriodOption(x, x))
                ];
                _sites = [new("", "جميع المواقع"), .. catalogue.Sites];
                _catalogueLoaded = true;
                Changed(nameof(Periods), nameof(SiteOptions));
            }

            if (!ValidPeriod(_period) || !_periods.Any(x => x.Period == _period) ||
                (_site.Length > 0 && !_sites.Any(x => x.SiteId == _site)) ||
                _term.Length > 100 || _term.Any(char.IsControl))
                throw new InvalidDataException("Invalid monthly archive filter.");

            var filter = new NativeMonthlyFilter(_period, _site, _term, _page, 100);
            NativeMonthlySnapshot result = await _reader.ReadMonthAsync(actor, filter, linked.Token);
            if (request != _generation || linked.IsCancellationRequested ||
                _liveActor?.Invoke() != actor) return;
            Validate(result, filter);
            int rankIndex = -1;
            for (int i = 0; i < result.CustomColumns.Count; i++)
                if (result.CustomColumns[i].Caption.Trim() is "الرتبة" or "رتبة")
                {
                    rankIndex = i;
                    break;
                }

            _rows = result.Rows.Select(row => new NativeMonthlyDisplayRow(
                row.RowNumber, row.PersonId, row.ServiceNumberA,
                rankIndex < 0 ? "" : row.CustomCells[rankIndex],
                row.Name, row.SiteName,
                row.CustomCells.Where((_, i) => i != rankIndex).ToArray(),
                row.DayCells.Select(cell => new NativeMonthlyCellDisplay(
                    cell?.Symbol ?? "", cell?.Description ?? "")).ToArray())).ToArray();
            _extraColumns = result.CustomColumns
                .Where((_, i) => i != rankIndex).ToArray();
            _days = Enumerable.Range(1, result.DaysInMonth).ToArray();
            _total = result.TotalRows;
            _hasNext = result.HasNext;
            _connected = true;
            _loading = false;
            _error = "";
            Changed(nameof(Rows), nameof(Days), nameof(CustomColumns),
                nameof(TotalRows), nameof(HasNext), nameof(PageLabel),
                nameof(IsDataConnected), nameof(IsLoading), nameof(LoadError),
                nameof(StatusMessage));
            UpdateCommands();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested || ct.IsCancellationRequested)
        {
            if (request == _generation) HideRows("");
        }
        catch
        {
            if (request == _generation)
                HideRows("تعذرت قراءة أرشيف الشهر المعتمد؛ تم إخفاء النتائج القديمة حماية للبيانات.");
        }
        finally
        {
            if (request == _generation) _pending = null;
        }
    }

    public async Task SelectPeriodAsync(string? period, CancellationToken ct = default)
    {
        if (!ValidPeriod(period) || !_periods.Any(p => p.Period == period))
        {
            Invalidate("الشهر المحدد خارج الأرشيف المعتمد أو في المستقبل.");
            return;
        }
        if (_period == period) return;
        _period = period!;
        _page = 1;
        Changed(nameof(SelectedPeriod), nameof(PageLabel), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SelectSiteAsync(string? siteId, CancellationToken ct = default)
    {
        siteId ??= "";
        if (siteId.Length > 0 && !_sites.Any(s => s.SiteId == siteId))
        {
            Invalidate("الموقع المحدد غير معتمد لعرض الأرشيف.");
            return;
        }
        if (_site == siteId) return;
        _site = siteId;
        _page = 1;
        Changed(nameof(SelectedSiteId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SearchAsync(string? search, CancellationToken ct = default)
    {
        string term = (search ?? "").Trim();
        if (term.Length > 100 || term.Any(char.IsControl))
        {
            Invalidate("البحث الشهري لا يتجاوز 100 حرف ولا يقبل أحرف التحكم.");
            return;
        }
        _term = term;
        _searchDraft = term;
        _page = 1;
        Changed(nameof(SearchDraft), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (!_hasNext || _loading || _page == 100000) return;
        ++_page;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (_page <= 1 || _loading) return;
        --_page;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    private void Invalidate(string error)
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        HideRows(error);
    }

    private static void Validate(NativeMonthlySnapshot s, NativeMonthlyFilter filter)
    {
        string[] tokens = filter.Period.Split('-');
        int expectedDays = DateTime.DaysInMonth(int.Parse(tokens[0], CultureInfo.InvariantCulture),
            int.Parse(tokens[1], CultureInfo.InvariantCulture));
        if (s.Period != filter.Period || s.SiteId != filter.SiteId ||
            s.Page != filter.Page || s.DaysInMonth != expectedDays ||
            s.TotalRows < 0 || s.Rows.Count > filter.PageSize ||
            s.TotalRows < s.Rows.Count || s.HasNext !=
                ((long)filter.Page * filter.PageSize < s.TotalRows) ||
            s.Rows.Any(row => string.IsNullOrWhiteSpace(row.PersonId) ||
                row.CustomCells.Count != s.CustomColumns.Count ||
                row.DayCells.Count != expectedDays) ||
            s.Rows.Select(row => row.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != s.Rows.Count ||
            s.CustomColumns.Select(c => c.ColumnId)
                .Distinct(StringComparer.Ordinal).Count() != s.CustomColumns.Count)
            throw new InvalidDataException("Canonical monthly archive page mismatch.");
    }

    private void UpdateCommands()
    {
        foreach (ICommand command in new[] {
            RefreshCommand, SearchCommand, ClearSearchCommand,
            PreviousPageCommand, NextPageCommand
        })
            if (command is NativeCommand native) native.NotifyAvailabilityChanged();
    }

    private void Changed(params string[] properties)
    {
        foreach (string property in properties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
