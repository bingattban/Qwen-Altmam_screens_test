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
    private readonly Dictionary<string, NativeAutoRegisterRow> _selection = new(StringComparer.Ordinal);
    public NativeAttendanceReviewViewModel Review { get; } = new();
    public int SelectedCount => _selection.Count;
    public string SelectedLabel => $"المحددون: {_selection.Count} من 100";

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
        CloseCommand = new NativeCommand(_ => RequestClose(), _ => IsOpen);
        SelectVisiblePageCommand = new NativeCommand(_ => SelectVisiblePage(), _ => IsOpen && !IsLoading);
        ClearSelectionCommand = new NativeCommand(_ => ClearSelection(), _ => IsOpen && SelectedCount > 0);
        ReviewSelectedCommand = new NativeCommand(_ => _ = OpenReviewAsync(),
            _ => IsOpen && !IsLoading && SelectedCount is >= 1 and <= 100);
        Review.Committed += _ =>
        {
            // On a verified receipt, the original server has already written
            // one atomic audited change. Refresh only committed SQLCipher rows.
            _selection.Clear();
            ChangedVisible();
            _ = RefreshAsync();
        };
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
    public ICommand SelectVisiblePageCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ReviewSelectedCommand { get; }

    public void AttachReader(INativeDashboardContinuationReader reader,
        Func<NativeIdentity?> getPrincipal)
    {
        if (_reader is not null) throw new InvalidOperationException("Auto register reader already attached.");
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _getPrincipal = getPrincipal ?? throw new ArgumentNullException(nameof(getPrincipal));
        if (reader is INativeAttendanceReviewBackend reviewBackend)
            Review.AttachBackend(reviewBackend, getPrincipal);
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

    public void RequestClose()
    {
        if (Review.IsCommitting)
        {
            _error = "اعتماد التغيير قيد التنفيذ؛ انتظر نتيجة العملية.";
            ChangedVisible();
            return;
        }
        if (_selection.Count > 0)
        {
            _error = "لديك أفراد محددون دون اعتماد؛ اضغط إلغاء التحديد قبل إغلاق السجل.";
            ChangedVisible();
            return;
        }
        Close();
    }

    public void Close()
    {
        Review.ForceCancel();
        _selection.Clear();
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
            foreach (NativeAutoRegisterRow person in _rows)
                person.IsSelected = _selection.ContainsKey(person.PersonId);
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

    public void SetRowSelected(NativeAutoRegisterRow person, bool selected)
    {
        if (!_isOpen || _isLoading || Review.IsCommitting ||
            !Rows.Any(x => x.PersonId == person.PersonId)) return;
        if (selected && !_selection.ContainsKey(person.PersonId) && _selection.Count >= 100)
        {
            person.IsSelected = false;
            _error = "الحد الأقصى 100 فرد لكل عملية؛ اختر عدداً أقل.";
            ChangedVisible();
            return;
        }
        if (selected) _selection[person.PersonId] = person;
        else _selection.Remove(person.PersonId);
        person.IsSelected = selected;
        _error = "";
        if (Review.IsOpen) Review.Cancel(); // changing selection revokes preview
        ChangedVisible();
    }

    public void SelectVisiblePage()
    {
        if (!_isOpen || _isLoading || Review.IsCommitting) return;
        var missing = Rows.Where(x => !_selection.ContainsKey(x.PersonId)).ToArray();
        if (_selection.Count + missing.Length > 100)
        {
            _error = "الحد الأقصى 100 فرد لكل عملية؛ اختر عدداً أقل.";
            ChangedVisible();
            return;
        }
        foreach (var person in missing)
        {
            _selection[person.PersonId] = person;
            person.IsSelected = true;
        }
        if (Review.IsOpen) Review.Cancel();
        _error = "";
        ChangedVisible();
    }

    public void ClearSelection()
    {
        if (Review.IsCommitting) return;
        _selection.Clear();
        foreach (var person in Rows) person.IsSelected = false;
        if (Review.IsOpen) Review.Cancel();
        _error = "";
        ChangedVisible();
    }

    public Task OpenReviewAsync(CancellationToken ct = default)
    {
        if (!_isOpen || _isLoading || _selection.Count is < 1 or > 100 ||
            _getPrincipal?.Invoke() != _principal)
            return Task.CompletedTask;
        return Review.OpenAsync(_businessDate, _siteId, _selection.Values.ToArray(), ct);
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
            nameof(Rows), nameof(CustomColumns),
            nameof(SelectedCount), nameof(SelectedLabel));
        foreach (ICommand cmd in new[] {
            SearchCommand, ClearSearchCommand, PreviousPageCommand, NextPageCommand, CloseCommand,
            SelectVisiblePageCommand, ClearSelectionCommand, ReviewSelectedCommand
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
