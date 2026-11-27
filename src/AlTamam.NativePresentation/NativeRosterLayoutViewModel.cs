using System.ComponentModel;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public interface INativeRosterLayoutReader
{
    Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(NativeIdentity actor,CancellationToken ct=default);
}

public sealed record NativeRosterColumn(string ColumnId,string TechnicalKey,string Caption,int FieldType,int DisplayOrder,
    bool IsSystemProtected,bool Visible,bool Active,long StoredValueCount,string Snapshot)
{
    public string TypeLabel=>FieldType switch {1=>"نص",2=>"رقم",3=>"تاريخ",4=>"نعم/لا",_=>"غير معروف"};
    public string ProtectionLabel=>IsSystemProtected?"أساسي محمي":"إضافي";
    public string StateLabel=>!Active?"معطل":Visible?"ظاهر":"مخفي";
    public bool IsValid=>ColumnId is {Length:>=4 and <=128} && !ColumnId.Any(char.IsControl) &&
        TechnicalKey is {Length:>=1 and <=128} && !TechnicalKey.Any(char.IsControl) &&
        Caption is {Length:>=1 and <=80} && !Caption.Any(char.IsControl) &&
        FieldType is >=1 and <=4 && DisplayOrder>=1 && StoredValueCount>=0 &&
        Snapshot is {Length:64} && Snapshot.All(Uri.IsHexDigit) &&
        (IsSystemProtected?Active && Visible && DisplayOrder<=4:DisplayOrder>=5) &&
        (Active || !Visible);
    public static bool ValidCatalogue(IReadOnlyList<NativeRosterColumn> rows)=>
        rows is {Count:>=4 and <=512} && rows.All(x=>x is not null && x.IsValid) &&
        rows.Select(x=>x.ColumnId).Distinct(StringComparer.Ordinal).Count()==rows.Count &&
        rows.Select(x=>x.TechnicalKey).Distinct(StringComparer.Ordinal).Count()==rows.Count &&
        rows.Where(x=>x.IsSystemProtected).Select(x=>x.DisplayOrder).OrderBy(x=>x).SequenceEqual(new[]{1,2,3,4}) &&
        rows.Count(x=>x.Active && !x.IsSystemProtected)<=64;
}

/// <summary>Read-only definition inventory; no metadata changes or value migrations.</summary>
public sealed class NativeRosterLayoutViewModel:INotifyPropertyChanged
{
    private INativeRosterLayoutReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open,_busy;
    private IReadOnlyList<NativeRosterColumn> _rows=[];
    private string _error="";
    public NativeRosterLayoutViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !_busy);
        CloseCommand=new NativeCommand(_=>Clear(),_=>_open);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen=>_open;
    public bool IsBusy=>_busy;
    public IReadOnlyList<NativeRosterColumn> Rows=>CanRead?_rows:[];
    public string Error=>_error;
    private bool CanRead=>_open && _reader is not null && _actor is { } actor &&
        _liveActor?.Invoke()==actor && RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public NativeCommand RefreshCommand {get;}
    public NativeCommand CloseCommand {get;}
    public void Attach(INativeRosterLayoutReader reader,Func<NativeIdentity?> actor)
    {
        if(_reader is not null) throw new InvalidOperationException("Roster layout already attached.");
        _reader=reader; _liveActor=actor;
    }
    public async Task OpenAsync(CancellationToken ct=default)
    {
        if(_open || _reader is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))return;
        _actor=actor; _open=true;
        await RefreshAsync(ct);
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if(!CanRead || _busy || _reader is not { } reader || _actor is not { } actor)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked; _busy=true; _rows=[]; _error=""; Notify();
        try
        {
            var rows=await reader.ReadRosterLayoutAsync(actor,linked.Token);
            if(version!=_generation || linked.IsCancellationRequested)return;
            if(!CanRead){Clear();return;}
            if(!NativeRosterColumn.ValidCatalogue(rows))throw new InvalidDataException("Invalid roster layout.");
            _rows=Array.AsReadOnly(rows.ToArray());
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch
        {
            if(version==_generation && CanRead)_error="تعذر قراءة تعريفات الأعمدة المعتمدة.";
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null; _busy=false;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void Clear()
    {
        ++_generation; _pending?.Cancel(); _pending=null; _actor=null;
        _open=false; _busy=false; _rows=[]; _error=""; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged(); CloseCommand.NotifyAvailabilityChanged();
    }
}
