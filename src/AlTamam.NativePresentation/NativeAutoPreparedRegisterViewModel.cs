using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// Modal read-only list of automatically committed attendance. Uses precisely
/// the canonical SQLCipher QueryDailyRoster "__AUTO_PREPARED__" projection.
/// Closing, logout, date rollover and nonce replacement revoke all row data.
/// No status edits or writes are exposed until their own verified WPF parity.
/// </summary>
public sealed class NativeAutoPreparedRegisterViewModel : INotifyPropertyChanged
{
    private INativeDashboardContinuationReader? _reader;
    private Func<NativeIdentity?>? _getPrincipal;
    private NativeIdentity? _principal;
    private CancellationTokenSource? _pending;
    private int _generation;
    private DateOnly _businessDate;
    private string _siteId = "";
    private string _searchDraft = "";
    private string _term = "";
    private int _page = 1;
    private long _total;
    private bool _hasNext, _isLoading, _isOpen;
    private string _error = "";
    private IReadOnlyList<NativeAutoRegisterRow> _rows = [];
    private IReadOnlyList<NativeDashboardColumn> _customColumns = [];

    public NativeAutoPreparedRegisterViewModel()
    {
        SearchCommand = new NativeCommand(_ => _ = SearchAsync(SearchDraft),
            _ => IsOpen && !IsLoading);
        ClearSearchCommand = new NativeCommand(_ => _ = SearchAsync(""),
            _ => IsOpen && !IsLoading);
        PreviousPageCommand = new NativeCommand(_ => _ = PreviousPageAsync(),
            _ => IsOpen && !IsLoading && Page > 1);
        NextPageCommand = new NativeCommand(_ => _ = NextPageAsync(),
            _ => IsOpen && !IsLoading && HasNext);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen);
    }

    public bool IsOpen => _isOpen;
    public bool IsLoading => _isLoading;
    public string LoadError => _error;
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

    public string Title => "المحضّرون تلقائياً — " + _businessDate.ToString("yyyy-MM-dd");
    public string PageLabel => $"المحضّرون تلقائياً: {_total} — صفحة {_page}";
    public long Total => _total;
    public int Page => _page;
    public bool HasNext => _hasNext;
    public IReadOnlyList<NativeAutoRegisterRow> Rows => _rows;
    public IReadOnlyList<NativeDashboardColumn> CustomColumns => _customColumns;

    public ICommand SearchCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachReader(INativeDashboardContinuationReader reader,
        Func<NativeIdentity?> getPrincipal)
    {
        if (_reader is not null) throw new InvalidOperationException("Auto register reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _getPrincipal = getPrincipal ?? throw new ArgumentNullException(nameof(getPrincipal));
    }

    public async Task OpenAsync(DateOnly businessDate, string siteId, CancellationToken ct = default)
    {
        // Close first to revoke the old generation, even when the user opens
        // another register while the prior SQLCipher read has not returned.
        Close();
        var principal = _getPrincipal?.Invoke();
        if (principal is null || _reader is null) return;
        _principal = principal;
        _businessDate = businessDate;
        _siteId = siteId ?? "";
        _isOpen = true;
        ChangedVisible();
        await RefreshAsync(ct);
    }

    public void Close()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _principal = null;
        _isOpen = false;
        _isLoading = false;
        _rows = [];
        _customColumns = [];
        _total = 0;
        _hasNext = false;
        _page = 1;
        _error = "";
        _term = "";
        _searchDraft = "";
        _siteId = "";
        ChangedVisible();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!_isOpen || _reader is null || _principal is not { } expected) return;
        if (_getPrincipal?.Invoke() != expected ||
            DateOnly.FromDateTime(DateTime.Now) != _businessDate)
        {
            Close();
            return;
        }

        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _rows = [];
        _customColumns = [];
        _total = 0;
        _hasNext = false;
        _error = "";
        _isLoading = true;
        ChangedVisible();
        var query = new NativeAutoRegisterFilter(_businessDate, _siteId, _term, _page, 100);
        try
        {
            NativeAutoRegisterPage data = await _reader.ReadAutoRegisterAsync(
                expected, query, linked.Token);
            if (version != _generation || !_isOpen || linked.IsCancellationRequested ||
                _getPrincipal?.Invoke() != expected)
                return;
            if (data.BusinessDate != query.BusinessDate || data.SiteId != query.SiteId ||
                data.Page != query.Page || data.Total < 0 || data.Rows.Count > query.PageSize ||
                data.Rows.Any(r => string.IsNullOrWhiteSpace(r.PersonId) ||
                    r.CustomCells.Count != data.CustomColumns.Count))
                throw new InvalidDataException("Inconsistent SQLCipher auto-prepared register.");

            _rows = data.Rows;
            _customColumns = data.CustomColumns;
            _total = data.Total;
            _hasNext = data.HasNext;
            _error = "";
            _isLoading = false;
            ChangedVisible();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || linked.IsCancellationRequested)
        {
            if (version == _generation)
            {
                _isLoading = false;
                ChangedVisible();
            }
        }
        catch
        {
            if (version == _generation)
            {
                _rows = [];
                _customColumns = [];
                _total = 0;
                _hasNext = false;
                _isLoading = false;
                _error = "تعذر قراءة سجل المحضّرين تلقائياً من قاعدة البيانات؛ لم يتم عرض بيانات قديمة.";
                ChangedVisible();
            }
        }
        finally
        {
            if (version == _generation) _pending = null;
        }
    }

    public async Task SearchAsync(string term, CancellationToken ct = default)
    {
        if (!_isOpen) return;
        string cleaned = (term ?? "").Trim();
        if (cleaned.Length > 100 || cleaned.Any(char.IsControl))
        {
            ++_generation;
            _pending?.Cancel();
            _rows = [];
            _customColumns = [];
            _total = 0;
            _hasNext = false;
            _isLoading = false;
            _error = "بحث السجل لا يتجاوز 100 حرف ولا يقبل أحرف التحكم.";
            ChangedVisible();
            return;
        }
        _searchDraft = cleaned;
        _term = cleaned;
        _page = 1;
        ChangedVisible();
        await RefreshAsync(ct);
    }

    public async Task NextPageAsync(CancellationToken ct = default)
    {
        if (!_isOpen || !_hasNext || _isLoading) return;
        _page++;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    public async Task PreviousPageAsync(CancellationToken ct = default)
    {
        if (!_isOpen || _page <= 1 || _isLoading) return;
        _page--;
        Changed(nameof(Page));
        await RefreshAsync(ct);
    }

    private void ChangedVisible()
    {
        Changed(nameof(IsOpen), nameof(IsLoading), nameof(LoadError), nameof(SearchDraft),
            nameof(Title), nameof(PageLabel), nameof(Total), nameof(Page), nameof(HasNext),
            nameof(Rows), nameof(CustomColumns));
        foreach (ICommand cmd in new[] {
            SearchCommand, ClearSearchCommand, PreviousPageCommand, NextPageCommand, CloseCommand
        })
            if (cmd is NativeCommand native) native.NotifyAvailabilityChanged();
    }

    private void Changed(params string[] names)
    {
        foreach (string name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
