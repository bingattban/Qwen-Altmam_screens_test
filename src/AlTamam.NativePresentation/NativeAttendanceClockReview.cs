using System.ComponentModel;
using System.Globalization;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public enum NativeAttendanceClockKind { Due, Lock }
public sealed record NativeAttendanceClockIntent(NativeAttendanceSettingsState Before,
    NativeAttendanceClockKind Kind,string Time,string OperationId)
{
    public string Previous=>Kind==NativeAttendanceClockKind.Due?Before.DueTime:Before.LockTime;
    public string SettingKey=>Kind==NativeAttendanceClockKind.Due?"AutoAttendanceDueTimeLocal":"DailyAttendanceLockTimeLocal";
    public string PayloadKey=>Kind==NativeAttendanceClockKind.Due?"dueTimeLocal":"dailyLockTimeLocal";
    public bool IsValid(NativeIdentity actor)=>Before.IsValid &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        Kind is NativeAttendanceClockKind.Due or NativeAttendanceClockKind.Lock &&
        Time is {Length:5} && TimeOnly.TryParseExact(Time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        Time!=Previous && Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
    public bool Matches(NativeAttendanceSettingsState state)=>state.IsValid &&
        (Kind==NativeAttendanceClockKind.Due?state.DueTime:state.LockTime)==Time;
}

public sealed class NativeAttendanceClockRejectedException:Exception { }

public interface INativeAttendanceClockBackend
{
    Task<NativeAttendanceSettingsState> SaveAttendanceClockAsync(NativeIdentity actor,
        NativeAttendanceClockIntent intent,CancellationToken ct=default);
}

public sealed class NativeAttendanceClockReview:INotifyPropertyChanged
{
    private INativeAttendanceClockBackend? _backend;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _live;
    private Func<NativeSettingsOverview?>? _current;
    private NativeSettingsOverview? _snapshot;
    private NativeAttendanceClockIntent? _prepared;
    private NativeAttendanceSettingsState? _receipt;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _busy,_attempted,_uncertain,_reload;
    private NativeAttendanceClockKind _kind;
    private string _time="",_error="";
    public NativeAttendanceClockReview()
    {
        PreviewCommand=new NativeCommand(_=>Preview(),_=>CanEdit);
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand PreviewCommand {get;}
    public NativeCommand ConfirmCommand {get;}
    public bool IsOpen=>_actor is {} actor && _live?.Invoke()==actor;
    private bool ContextValid=>IsOpen && _snapshot is not null &&
        ReferenceEquals(_current?.Invoke(),_snapshot) && _actor is {} actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public NativeAttendanceSettingsState? Before=>IsOpen?_snapshot?.AttendanceSettings:null;
    public NativeAttendanceClockIntent? Prepared=>IsOpen?_prepared:null;
    public NativeAttendanceSettingsState? Receipt=>IsOpen?_receipt:null;
    public bool HasPreview=>Prepared is not null;
    public bool IsBusy=>IsOpen && _busy;
    public bool NeedsReload=>IsOpen && _reload;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>ContextValid && !_busy && !_attempted;
    public bool CanConfirm=>CanEdit && _prepared is {} intent && _actor is {} actor && intent.IsValid(actor);
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public string Error=>IsOpen?_error:"";
    public string CompletionMessage=>Receipt is null?"":"تم حفظ الوقت المحدد والتحقق من القيمة المحفوظة.";
    public string ClockLabel=>_kind==NativeAttendanceClockKind.Due?"وقت التحضير التلقائي":"وقت قفل التمام اليومي";
    public string PreviousTime=>Before is {} before?(_kind==NativeAttendanceClockKind.Due?before.DueTime:before.LockTime):"";
    public string Time {get=>IsOpen?_time:"";set{if(CanEdit && _time!=value){_time=value??"";Invalidate();}}}
    private void Invalidate(){_prepared=null;_error="";Notify();}
    public bool Open(INativeAttendanceClockBackend backend,NativeIdentity actor,NativeSettingsOverview snapshot,
        Func<NativeIdentity?> live,Func<NativeSettingsOverview?> current,NativeAttendanceClockKind kind)
    {
        if(_actor is not null || live()!=actor || !ReferenceEquals(current(),snapshot) ||
            !snapshot.Matches(actor) || snapshot.AttendanceSettings is not {} before ||
            kind is not (NativeAttendanceClockKind.Due or NativeAttendanceClockKind.Lock) ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))return false;
        _backend=backend;_actor=actor;_snapshot=snapshot;_live=live;_current=current;
        _kind=kind;_time=kind==NativeAttendanceClockKind.Due?before.DueTime:before.LockTime;Notify();return true;
    }
    public void Preview()
    {
        if(!CanEdit || Before is not {} before || _actor is not {} actor)return;
        var intent=new NativeAttendanceClockIntent(before,_kind,_time.Trim(),Guid.NewGuid().ToString("N"));
        _prepared=intent.IsValid(actor)?intent:null;
        _error=_prepared is null?"حدد وقتا مختلفا وصالحا بصيغة HH:mm.":"";Notify();
    }
    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is not {} backend || _actor is not {} actor ||
            _prepared is not {} intent || ct.IsCancellationRequested)return;
        long generation=++_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending;_busy=_attempted=_uncertain=true;_error="";Notify();
        try
        {
            var result=await backend.SaveAttendanceClockAsync(actor,intent,pending.Token);
            if(generation!=_generation || !ContextValid)return;
            if(result is null || !intent.Matches(result) || pending.IsCancellationRequested)
                throw new InvalidOperationException("Unverified attendance clock receipt.");
            _receipt=result;_prepared=null;_uncertain=false;
        }
        catch(NativeAttendanceClockRejectedException)
        {
            if(generation==_generation && ContextValid)
            {
                _reload=true;_prepared=null;_uncertain=false;
                _error="رفضت الخدمة التغيير. أغلق المراجعة وحدّث الإعدادات.";
            }
        }
        catch
        {
            if(generation==_generation && ContextValid)
                _error="نتيجة الحفظ غير مؤكدة. لن تتكرر العملية تلقائيا؛ سجل الخروج وراجع الإعدادات وسجل العمليات.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null;_busy=false;if(!IsOpen)ForceClear();else Notify();
            }
        }
    }
    public bool Close(){if(!CanClose)return false;ForceClear();return true;}
    public void ForceClear()
    {
        ++_generation;_pending?.Cancel();_pending=null;_actor=null;_backend=null;_live=null;_current=null;_snapshot=null;
        _prepared=null;_receipt=null;_busy=_attempted=_uncertain=_reload=false;_time=_error="";Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();ConfirmCommand.NotifyAvailabilityChanged();
    }
}
