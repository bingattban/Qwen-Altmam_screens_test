using System.ComponentModel;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeDefaultStatusIntent(NativeAttendanceSettingsState Before,
    NativeDefaultStatusChoice Choice,string OperationId)
{
    public bool IsValid(NativeIdentity actor)=>Before.IsValid && Choice.IsValid &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        Choice.Id!=Before.DefaultStatusId &&
        Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
    public bool Matches(NativeAttendanceSettingsState state)=>state.IsValid && state.Configured &&
        state.DefaultStatusId==Choice.Id && state.DefaultStatusName==Choice.Name;
}

public sealed class NativeDefaultStatusRejectedException:Exception { }

public interface INativeDefaultStatusBackend
{
    Task<NativeAttendanceSettingsState> SaveDefaultStatusAsync(NativeIdentity actor,
        NativeDefaultStatusIntent intent,CancellationToken ct=default);
}

public sealed class NativeDefaultStatusReview:INotifyPropertyChanged
{
    private INativeDefaultStatusBackend? _backend;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _live;
    private Func<NativeSettingsOverview?>? _current;
    private NativeSettingsOverview? _snapshot;
    private NativeDefaultStatusIntent? _prepared;
    private NativeAttendanceSettingsState? _receipt;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _busy,_attempted,_uncertain,_reload;
    private NativeDefaultStatusChoice? _selected;
    private string _error="";
    public NativeDefaultStatusReview()
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
    public NativeDefaultStatusIntent? Prepared=>IsOpen?_prepared:null;
    public NativeAttendanceSettingsState? Receipt=>IsOpen?_receipt:null;
    public bool HasPreview=>Prepared is not null;
    public bool IsBusy=>IsOpen && _busy;
    public bool NeedsReload=>IsOpen && _reload;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>ContextValid && !_busy && !_attempted;
    public bool CanConfirm=>CanEdit && _prepared is {} intent && _actor is {} actor && intent.IsValid(actor);
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public string Error=>IsOpen?_error:"";
    public string CompletionMessage=>Receipt is null?"":"تم حفظ الحالة الافتراضية والتحقق من القيمة المحفوظة.";
    public IReadOnlyList<NativeDefaultStatusChoice> Choices=>IsOpen?_snapshot?.DefaultStatusChoices??Array.Empty<NativeDefaultStatusChoice>():Array.Empty<NativeDefaultStatusChoice>();
    public NativeDefaultStatusChoice? Selected
    {
        get=>IsOpen?_selected:null;
        set
        {
            if(!CanEdit)return;
            var canonical=Choices.FirstOrDefault(choice=>ReferenceEquals(choice,value));
            if(!ReferenceEquals(_selected,canonical)){_selected=canonical;Invalidate();}
        }
    }
    private void Invalidate(){_prepared=null;_error="";Notify();}
    public bool Open(INativeDefaultStatusBackend backend,NativeIdentity actor,NativeSettingsOverview snapshot,
        Func<NativeIdentity?> live,Func<NativeSettingsOverview?> current)
    {
        if(_actor is not null || live()!=actor || !ReferenceEquals(current(),snapshot) ||
            !snapshot.Matches(actor) || snapshot.AttendanceSettings is not {} before ||
            !snapshot.HasDefaultStatusChoices ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))return false;
        _backend=backend;_actor=actor;_snapshot=snapshot;_live=live;_current=current;
        _selected=Choices.FirstOrDefault(choice=>choice.Id==before.DefaultStatusId);Notify();return true;
    }
    public void Preview()
    {
        if(!CanEdit || Before is not {} before || _actor is not {} actor)return;
        var intent=_selected is {} choice && Choices.Any(item=>ReferenceEquals(item,choice))?
            new NativeDefaultStatusIntent(before,choice,Guid.NewGuid().ToString("N")):null;
        _prepared=intent is not null && intent.IsValid(actor)?intent:null;
        _error=_prepared is null?"اختر حالة نشطة مختلفة من القائمة.":"";Notify();
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
            var result=await backend.SaveDefaultStatusAsync(actor,intent,pending.Token);
            if(generation!=_generation || !ContextValid)return;
            if(result is null || !intent.Matches(result) || pending.IsCancellationRequested)
                throw new InvalidOperationException("Unverified default status receipt.");
            _receipt=result;_prepared=null;_uncertain=false;
        }
        catch(NativeDefaultStatusRejectedException)
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
        _prepared=null;_receipt=null;_busy=_attempted=_uncertain=_reload=false;_selected=null;_error="";Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();ConfirmCommand.NotifyAvailabilityChanged();
    }
}
