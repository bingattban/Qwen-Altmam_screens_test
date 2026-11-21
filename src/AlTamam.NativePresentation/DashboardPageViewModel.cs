using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// The actual read-only native Dashboard. All counts come from canonical
/// SQLCipher C# services, never from a local row tally or placeholder data.
/// An in-flight request cannot publish after logout, token replacement or
/// filter change: both the real reader and this ViewModel check authority.
/// </summary>
public sealed class DashboardPageViewModel : NativePageViewModel, INotifyPropertyChanged
{
    private INativeDashboardReader? _reader;
    private Func<NativeIdentity?>? _currentPrincipal;
    private NativeIdentity? _boundPrincipal;
    private int _requestVersion;
    private CancellationTokenSource? _activeRead;
    private bool _isLoading;
    private bool _isDataConnected;
    private string _loadError = "";
    private string _selectedSiteId = "";
    private string _selectedStatusId = "";
    private string _committedSearch = "";
    private string _searchDraft = "";
    private int _page = 1;
    private long _totalRows;
    private long _totalForce;
    private string _businessDateLabel = "";
    private bool _hasNext;
    private IReadOnlyList<NativeDashboardCounter> _counters = [];
    private IReadOnlyList<NativeDashboardRow> _rows = [];
    private IReadOnlyList<NativeDashboardColumn> _customColumns = [];
    private IReadOnlyList<NativeDashboardSite> _siteOptions = [new("", "جميع المواقع")];

    public DashboardPageViewModel() : base(NativeScreen.Dashboard, "الرئيسية")
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => _currentPrincipal?.Invoke() is not null);
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft), _ => _currentPrincipal?.Invoke() is not null);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""), _ => _currentPrincipal?.Invoke() is not null);
        SelectStatusCommand = new NativeCommand(
            parameter => { if (parameter is string id) _ = SelectStatusAsync(id); },
            parameter => parameter is string && _currentPrincipal?.Invoke() is not null);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(), _ => Page > 1 && !IsLoading);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(), _ => HasNext && !IsLoading);
    }

    public override bool IsDataConnected => _isDataConnected;
    public override string StatusMessage => IsLoading
        ? "جار قراءة بيانات التمام اليومي من قاعدة البيانات المشفرة..."
        : IsDataConnected
            ? "البيانات الفعلية من SQLCipher — للعرض فقط، والتحضير والتعديل ضمن مراحل لاحقة."
            : "لم يتم تحميل بيانات القوة؛ استخدم تحديث البيانات بعد تسجيل الدخول.";
    public bool IsLoading => _isLoading;
    public string LoadError => _loadError;
    public string BusinessDateLabel => _businessDateLabel;
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
    public string SelectedSiteId => _selectedSiteId;
    public string SelectedStatusId => _selectedStatusId;
    public long TotalForce => _totalForce;
    public long TotalRows => _totalRows;
    public int Page => _page;
    public bool HasNext => _hasNext;
    public string PageLabel => $"الصفحة {_page} — النتائج {_totalRows}";
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _siteOptions;
    public IReadOnlyList<NativeDashboardCounter> Counters => _counters;
    public IReadOnlyList<NativeDashboardRow> Rows => _rows;
    public IReadOnlyList<NativeDashboardColumn> CustomColumns => _customColumns;

    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand SelectStatusCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }

    public void AttachReader(INativeDashboardReader reader, Func<NativeIdentity?> currentPrincipal)
    {
        if (_reader is not null) throw new InvalidOperationException("Native dashboard reader already initialized.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _currentPrincipal = currentPrincipal ?? throw new ArgumentNullException(nameof(currentPrincipal));
    }

    /// <summary>
    /// Called on logout and session expiry. It cancels requests and clears ALL
    /// personnel and counters before another account can be authenticated.
    /// </summary>
    public void Clear()
    {
        ++_requestVersion;
        _activeRead?.Cancel();
        _activeRead = null;
        _boundPrincipal = null;
        _selectedSiteId = "";
        _selectedStatusId = "";
        _committedSearch = "";
        _searchDraft = "";
        _page = 1;
        _siteOptions = [new("", "جميع المواقع")];
        ClearSensitivePresentation("");
        Changed(nameof(SelectedSiteId), nameof(SelectedStatusId), nameof(SearchDraft), nameof(SiteOptions));
    }

    private void ClearSensitivePresentation(string error)
    {
        _isDataConnected = false;
        _isLoading = false;
        _loadError = error;
        _totalForce = 0;
        _totalRows = 0;
        _hasNext = false;
        _businessDateLabel = "";
        _counters = [];
        _rows = [];
        _customColumns = [];
        ChangedVisible();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        NativeIdentity? principal = _currentPrincipal?.Invoke();
        if (principal is null || _reader is null)
        {
            Clear();
            return;
        }
        // Never carry data across a silently substituted session. A fresh
        // account requires an explicit shell logout/login and Clear().
        if (_boundPrincipal is not null && _boundPrincipal != principal)
        {
            Clear();
            ClearSensitivePresentation("تغيرت هوية الجلسة؛ يرجى تسجيل الدخول من جديد.");
            return;
        }
        _boundPrincipal ??= principal;

        ++_requestVersion;
        int version = _requestVersion;
        _activeRead?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeRead = linked;
        var date = DateOnly.FromDateTime(DateTime.Now);
        var requested = new NativeDashboardFilter(date, _selectedSiteId, _selectedStatusId,
            _committedSearch, _page, 100);
        // Wipe stale person's data BEFORE any new request, even on an error.
        ClearSensitivePresentation("");
        _isLoading = true;
        Changed(nameof(IsLoading), nameof(StatusMessage));
        try
        {
            NativeDashboardSnapshot result = await _reader.ReadAsync(principal, requested, linked.Token);
            if (version != _requestVersion || linked.IsCancellationRequested ||
                _currentPrincipal?.Invoke() != principal)
                return;
            Validate(result, requested);

            _isLoading = false;
            _isDataConnected = true;
            _totalForce = result.TotalForce;
            _totalRows = result.TotalRows;
            _hasNext = result.HasNext;
            _businessDateLabel = result.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _counters = CreateCounters(result);
            _rows = result.Rows;
            _customColumns = result.CustomColumns;
            _siteOptions = [new("", "جميع المواقع"), .. result.Sites];
            ChangedVisible();
            Changed(nameof(SiteOptions));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested || ct.IsCancellationRequested)
        {
            if (version == _requestVersion)
                ClearSensitivePresentation("");
        }
        catch
        {
            if (version == _requestVersion)
                ClearSensitivePresentation("تعذر قراءة البيانات الحقيقية؛ لم يتم عرض نتائج قديمة أو افتراضية.");
        }
        finally
        {
            if (version == _requestVersion) _activeRead = null;
        }
    }

    private void InvalidateWithError(string message)
    {
        ++_requestVersion;
        _activeRead?.Cancel();
        _activeRead = null;
        ClearSensitivePresentation(message);
    }

    public async Task SelectSiteAsync(string siteId, CancellationToken ct = default)
    {
        siteId ??= "";
        if (siteId != "" && !_siteOptions.Any(x => x.SiteId == siteId))
        {
            InvalidateWithError("الموقع المحدد غير موجود في قائمة المواقع المعتمدة.");
            return;
        }
        if (_selectedSiteId == siteId) return;
        _selectedSiteId = siteId;
        _page = 1;
        Changed(nameof(SelectedSiteId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SelectStatusAsync(string statusId, CancellationToken ct = default)
    {
        statusId ??= "";
        if (statusId != "" && !_counters.Any(x => x.StatusId == statusId))
        {
            InvalidateWithError("الحالة المحددة غير موجودة ضمن الحالات الفعلية.");
            return;
        }
        if (_selectedStatusId == statusId) return;
        _selectedStatusId = statusId;
        _page = 1;
        Changed(nameof(SelectedStatusId), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task SearchAsync(string searchTerm, CancellationToken ct = default)
    {
        string term = (searchTerm ?? "").Trim();
        if (term.Length > 100 || term.Any(char.IsControl))
        {
            InvalidateWithError("نص البحث لا يتجاوز 100 حرف ولا يقبل أحرف التحكم.");
            return;
        }
        _searchDraft = term;
        _committedSearch = term;
        _page = 1;
        Changed(nameof(SearchDraft), nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (!_hasNext || _isLoading) return;
        _page++;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (_page <= 1 || _isLoading) return;
        _page--;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    private static void Validate(NativeDashboardSnapshot data, NativeDashboardFilter request)
    {
        if (data.BusinessDate != request.BusinessDate || data.SiteId != request.SiteId ||
            data.Page != request.Page || data.TotalRows < 0 || data.TotalForce < 0 ||
            data.AutomaticallyPrepared < 0 || data.InternalTransferDay < 0 ||
            data.Rows.Count > request.PageSize ||
            data.Rows.Any(x => x.CustomCells.Count != data.CustomColumns.Count) ||
            data.StatusCounts.Any(x => x.Count < 0) ||
            data.Sites.Any(x => string.IsNullOrWhiteSpace(x.SiteId)))
            throw new InvalidDataException("SQLCipher dashboard returned an inconsistent typed snapshot.");
    }

    private static IReadOnlyList<NativeDashboardCounter> CreateCounters(NativeDashboardSnapshot data)
    {
        var list = new List<NativeDashboardCounter>
        {
            new("", "إجمالي القوة", data.TotalForce, "#0D9488", "")
        };
        NativeDashboardStatus? present = data.StatusCounts.FirstOrDefault(x =>
            x.Name.Trim() is "موجود" or "الموجود" or "حاضر" or "الحاضر");
        static string Percentage(long count, long total) => total <= 0 ? "" :
            $"{Math.Round((decimal)count * 100m / total, 0, MidpointRounding.AwayFromZero)}%";
        if (present is not null)
            list.Add(new(present.StatusId, "الموجود", present.Count,
                SafeColor(present.Color), Percentage(present.Count, data.TotalForce)));
        foreach (var item in data.StatusCounts)
            if (item != present)
                list.Add(new(item.StatusId, item.Name, item.Count,
                    SafeColor(item.Color), Percentage(item.Count, data.TotalForce)));
        list.Add(new("__AUTO_PREPARED__", "المحضّرون تلقائياً",
            data.AutomaticallyPrepared, "#0D9488", ""));
        if (data.InternalTransferDay > 0)
            list.Add(new("__TRANSFER_DAY__", "نُقلوا اليوم",
                data.InternalTransferDay, "#D8B4FE", ""));
        return list;
    }

    private static string SafeColor(string? candidate)
        => candidate is { Length: 7 } color &&
           color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit)
            ? color : "#CBD5E1";

    private void ChangedVisible()
    {
        Changed(nameof(IsLoading), nameof(IsDataConnected), nameof(StatusMessage),
            nameof(LoadError), nameof(TotalForce), nameof(TotalRows), nameof(HasNext),
            nameof(BusinessDateLabel), nameof(Counters), nameof(Rows),
            nameof(CustomColumns), nameof(PageLabel));
        if (PreviousPageCommand is NativeCommand previous) previous.NotifyAvailabilityChanged();
        if (NextPageCommand is NativeCommand next) next.NotifyAvailabilityChanged();
    }

    private void Changed(params string[] properties)
    {
        foreach (var property in properties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
