using System.ComponentModel;
using System.Globalization;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public interface INativeAttendanceDraftReader
{
    Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default);
}

public sealed record NativeAttendanceDraft(string DraftId,DateOnly BusinessDate,string SiteId,
    string TargetStatusId,DateTimeOffset UpdatedAtUtc,IReadOnlyList<string> PersonIds)
{
    private static bool ValidId(string? value)=>value is {Length:>=1 and <=128} &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    public bool IsValid=>DraftId is {Length:36} && DraftId.StartsWith("DRF-",StringComparison.Ordinal) &&
        Guid.TryParseExact(DraftId[4..],"N",out _) && BusinessDate.Year is >=1900 and <=2199 &&
        (SiteId=="" || ValidId(SiteId)) && ValidId(TargetStatusId) &&
        UpdatedAtUtc!=default && UpdatedAtUtc.Offset==TimeSpan.Zero &&
        PersonIds is {Count:>=1 and <=100} && PersonIds.All(ValidId) &&
        PersonIds.Distinct(StringComparer.Ordinal).Count()==PersonIds.Count;
    public int PersonCount=>PersonIds.Count;
    public string DateLabel=>BusinessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public string UpdatedLabel=>UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);
    public string ScopeLabel=>SiteId==""?"جميع المواقع":SiteId;
    public static bool ValidCatalogue(IReadOnlyList<NativeAttendanceDraft>? rows)=>
        rows is {Count:<=10000} && rows.All(row=>row is not null && row.IsValid) &&
        rows.Select(row=>row.DraftId).Distinct(StringComparer.Ordinal).Count()==rows.Count;
}

/// <summary>Original shared draft screen: owner-only read, never an attendance writer.</summary>
public sealed class NativeAttendanceDraftsViewModel:INotifyPropertyChanged
{
    private INativeAttendanceDraftReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open,_busy;
    private IReadOnlyList<NativeAttendanceDraft> _rows=[];
    private string _error="";
    public NativeAttendanceDraftsViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !_busy);
        CloseCommand=new NativeCommand(_=>Clear(),_=>_open);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen=>_open;
    public bool IsBusy=>_busy;
    public IReadOnlyList<NativeAttendanceDraft> Rows=>CanRead?_rows:[];
    public string Error=>_error;
    public string CountLabel=>$"عدد المسودات: {Rows.Count}";
    private bool CanRead=>_open && _reader is not null && _actor is { } actor &&
        _liveActor?.Invoke()==actor && RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role);
    public NativeCommand RefreshCommand {get;}
    public NativeCommand CloseCommand {get;}
    public void Attach(INativeAttendanceDraftReader reader,Func<NativeIdentity?> actor)
    {
        if(_reader is not null)throw new InvalidOperationException("Draft reader already attached.");
        _reader=reader;_liveActor=actor;
    }
    public async Task OpenAsync(CancellationToken ct=default)
    {
        if(_open || _reader is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role))return;
        _actor=actor;_open=true;
        await RefreshAsync(ct);
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if(!CanRead || _busy || _reader is not { } reader || _actor is not { } actor)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_rows=[];_error="";Notify();
        try
        {
            var rows=await reader.ReadAttendanceDraftsAsync(actor,linked.Token);
            if(version!=_generation || linked.IsCancellationRequested)return;
            if(!CanRead){Clear();return;}
            if(!NativeAttendanceDraft.ValidCatalogue(rows))throw new InvalidDataException("Invalid owner draft catalogue.");
            _rows=Array.AsReadOnly(rows.Select(row=>row with {PersonIds=Array.AsReadOnly(row.PersonIds.ToArray())}).ToArray());
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch
        {
            if(version==_generation && CanRead)_error="تعذر التحقق من المسودات المحفوظة. لم يتم اعتماد أي حضور.";
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null;_busy=false;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void Clear()
    {
        ++_generation;_pending?.Cancel();_pending=null;_actor=null;
        _open=false;_busy=false;_rows=[];_error="";Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged();CloseCommand.NotifyAvailabilityChanged();
    }
}
