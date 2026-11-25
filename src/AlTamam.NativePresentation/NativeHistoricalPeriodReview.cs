using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Read-only direct historical lookup; writes remain in the canonical range review.</summary>
public sealed class NativeHistoricalPeriodReview : INotifyPropertyChanged
{
    private INativeHistoricalPeriodReader? _reader;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _liveActor;
    private Func<bool>? _scopeLocked;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _busy;
    private string _search="", _from="", _to="";
    private NativeHistoricalPersonCandidate? _selected;

    public NativeHistoricalPeriodReview()
    {
        SearchCommand=new NativeCommand(_=>_=SearchAsync(),_=>CanEdit && NativeHistoricalPersonCandidate.IsValidTerm(_search.Trim()));
        ReadCommand=new NativeCommand(_=>_=ReadAsync(),_=>CanEdit && _selected is not null);
        CloseCommand=new NativeCommand(_=>Close(),_=>CanClose);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SearchCommand { get; }
    public ICommand ReadCommand { get; }
    public ICommand CloseCommand { get; }
    public bool IsOpen => _actor is not null;
    public bool IsBusy => _busy;
    public bool CanEdit => IsOpen && !_busy && _scopeLocked?.Invoke()!=true && ContextValid();
    public bool CanClose => IsOpen && !_busy && _scopeLocked?.Invoke()!=true;
    public bool HasEvidence => Evidence is not null;
    public void NotifyScopeChanged() => Notify();
    public string Error { get; private set; }="";
    public IReadOnlyList<NativeHistoricalPersonCandidate> Candidates { get; private set; }=[];
    public NativeHistoricalPeriodEvidence? Evidence { get; private set; }
    public string SearchText
    {
        get=>_search;
        set
        {
            if (!CanEdit || _search==(value??"")) return;
            _search=value??"";
            ClearSearchResults();
            Notify();
        }
    }
    public NativeHistoricalPersonCandidate? SelectedPerson
    {
        get=>_selected;
        set
        {
            if (!CanEdit || ReferenceEquals(_selected,value) ||
                (value is not null && !Candidates.Any(x=>ReferenceEquals(x,value)))) return;
            _selected=value;
            ClearEvidence();
            Notify();
        }
    }
    public string FromText { get=>_from; set=>EditDate(ref _from,value); }
    public string ToText { get=>_to; set=>EditDate(ref _to,value); }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private bool ContextValid()=>_actor is { } actor && _liveActor?.Invoke()==actor &&
        RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);

    public bool Open(INativeHistoricalPeriodReader reader,Func<NativeIdentity?> liveActor,Func<bool>? scopeLocked=null)
    {
        var actor=liveActor();
        if (IsOpen || _busy || actor is null ||
            !RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role)) return false;
        _reader=reader;
        _actor=actor;
        _liveActor=liveActor;
        _scopeLocked=scopeLocked;
        _from=_to=Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        Notify();
        return true;
    }

    private void EditDate(ref string field,string? value)
    {
        if (!CanEdit || field==(value??"")) return;
        field=value??"";
        ClearEvidence();
        Notify();
    }

    private void ClearEvidence()
    {
        Evidence=null;
        Error="";
    }

    private void ClearSearchResults()
    {
        _selected=null;
        Candidates=[];
        ClearEvidence();
    }

    public async Task SearchAsync(CancellationToken ct=default)
    {
        if (!CanEdit || _reader is null || ct.IsCancellationRequested) return;
        ClearSearchResults();
        var term=_search.Trim();
        if (!NativeHistoricalPersonCandidate.IsValidTerm(term))
        {
            Error="أدخل الاسم أو الرقم العسكري (من 1 إلى 100 حرف).";
            Notify();
            return;
        }
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation=++_generation;
        _pending=linked;
        _busy=true;
        Notify();
        try
        {
            var rows=await _reader.SearchHistoricalPeopleAsync(_actor!,term,linked.Token);
            if (generation!=_generation || linked.IsCancellationRequested || !ContextValid()) return;
            if (rows is null || rows.Count>50 || rows.Any(x=>x is null || !x.IsValid) ||
                rows.Select(x=>x.PersonId).Distinct(StringComparer.Ordinal).Count()!=rows.Count)
                throw new InvalidDataException("Invalid historical person candidates.");
            Candidates=Array.AsReadOnly(rows.ToArray());
        }
        catch { if (generation==_generation && ContextValid()) Error="تعذر البحث عن الأفراد."; }
        finally { Finish(generation); }
    }

    public async Task ReadAsync(CancellationToken ct=default)
    {
        if (!CanEdit || _reader is null || _selected is not { } selected || ct.IsCancellationRequested) return;
        ClearEvidence();
        if (!Candidates.Any(x=>ReferenceEquals(x,selected)) ||
            !DateOnly.TryParseExact(_from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var from) ||
            !DateOnly.TryParseExact(_to,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var to))
        {
            Error="حدد الفرد وتاريخي البداية والنهاية بصيغة yyyy-MM-dd.";
            Notify();
            return;
        }
        var request=new NativeHistoricalPeriodRequest(selected.PersonId,from,to);
        if (!request.IsValid(Today))
        {
            Error="حدد فترة من يوم إلى 62 يوما دون تواريخ مستقبلية.";
            Notify();
            return;
        }
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation=++_generation;
        _pending=linked;
        _busy=true;
        Notify();
        try
        {
            var response=await _reader.ReadHistoricalPeriodAsync(_actor!,request,linked.Token);
            if (generation!=_generation || linked.IsCancellationRequested || !ContextValid() ||
                !ReferenceEquals(_selected,selected)) return;
            if (response is null || !response.Matches(request,Today))
                throw new InvalidDataException("Mismatched canonical historical evidence.");
            Evidence=response with { Days=Array.AsReadOnly(response.Days.ToArray()) };
        }
        catch { if (generation==_generation && ContextValid()) Error="تعذرت قراءة الفترة التاريخية."; }
        finally { Finish(generation); }
    }

    private void Finish(int generation)
    {
        if (generation!=_generation) return;
        _pending=null;
        _busy=false;
        if (!ContextValid()) { ForceClear(); return; }
        Notify();
    }

    public bool Close()
    {
        if (!CanClose) return false;
        ForceClear();
        return true;
    }

    public void ForceClear()
    {
        ++_generation;
        _pending?.Cancel();
        _pending=null;
        _reader=null;
        _actor=null;
        _liveActor=null;
        _scopeLocked=null;
        _busy=false;
        _search=_from=_to="";
        ClearSearchResults();
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        ((NativeCommand)SearchCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ReadCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
