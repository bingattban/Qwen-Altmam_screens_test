using System.ComponentModel;
using System.Globalization;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeMonthlyExportScheduleIntent(NativeAutomaticMonthlyExcelState Before,
    bool Enabled,string Time,string Directory,string OperationId)
{
    public bool IsValid(NativeIdentity actor)=>Before.IsValid &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        TimeOnly.TryParseExact(Time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        (Directory.Length==0 ? !Enabled : NativeManualBackupReceipt.ValidDirectory(Directory)) &&
        (Enabled!=Before.Enabled || Time!=Before.ExportTime || Directory!=Before.Directory) &&
        Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
    public bool Matches(NativeAutomaticMonthlyExcelState state)=>state.IsValid &&
        Enabled==state.Enabled && Time==state.ExportTime && Directory==state.Directory;
    public string EnabledLabel=>Enabled?"تفعيل":"إيقاف";
}

public sealed class NativeMonthlyExportScheduleRejectedException:Exception { }

public interface INativeMonthlyExportScheduleBackend
{
    Task<NativeAutomaticMonthlyExcelState> SaveMonthlyExportScheduleAsync(NativeIdentity actor,
        NativeMonthlyExportScheduleIntent intent,CancellationToken ct=default);
}

public sealed class NativeMonthlyExportScheduleReview:INotifyPropertyChanged
{
    private INativeMonthlyExportScheduleBackend? _backend;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _live;
    private Func<NativeSettingsOverview?>? _current;
    private NativeSettingsOverview? _snapshot;
    private NativeMonthlyExportScheduleIntent? _prepared;
    private NativeAutomaticMonthlyExcelState? _receipt;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _busy,_attempted,_uncertain,_reload,_enabled;
    private string _time="",_directory="",_error="";
    public NativeMonthlyExportScheduleReview()
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
    public NativeAutomaticMonthlyExcelState? Before=>IsOpen?_snapshot?.MonthlyExport:null;
    public NativeMonthlyExportScheduleIntent? Prepared=>IsOpen?_prepared:null;
    public NativeAutomaticMonthlyExcelState? Receipt=>IsOpen?_receipt:null;
    public bool HasPreview=>Prepared is not null;
    public bool IsBusy=>IsOpen && _busy;
    public bool NeedsReload=>IsOpen && _reload;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>ContextValid && !_busy && !_attempted;
    public bool CanConfirm=>CanEdit && _prepared is {} intent && _actor is {} actor && intent.IsValid(actor);
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public string Error=>IsOpen?_error:"";
    public string CompletionMessage=>Receipt is null?"":"تم حفظ إعداد التصدير والتحقق من الإعداد المحفوظ، ولم ينفذ تصدير بهذه العملية.";
    public bool Enabled {get=>IsOpen && _enabled;set{if(CanEdit && _enabled!=value){_enabled=value;Invalidate();}}}
    public string Time {get=>IsOpen?_time:"";set{if(CanEdit && _time!=value){_time=value??"";Invalidate();}}}
    public string Directory {get=>IsOpen?_directory:"";set{if(CanEdit && _directory!=value){_directory=value??"";Invalidate();}}}
    private void Invalidate(){_prepared=null;_error="";Notify();}
    public bool Open(INativeMonthlyExportScheduleBackend backend,NativeIdentity actor,NativeSettingsOverview snapshot,
        Func<NativeIdentity?> live,Func<NativeSettingsOverview?> current)
    {
        if(_actor is not null || live()!=actor || !ReferenceEquals(current(),snapshot) ||
            !snapshot.Matches(actor) || snapshot.MonthlyExport is not {} before ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))return false;
        _backend=backend;_actor=actor;_snapshot=snapshot;_live=live;_current=current;
        _enabled=before.Enabled;_time=before.ExportTime;_directory=before.Directory;Notify();return true;
    }
    public void Preview()
    {
        if(!CanEdit || Before is not {} before || _actor is not {} actor)return;
        var intent=new NativeMonthlyExportScheduleIntent(before,_enabled,_time.Trim(),_directory.Trim(),Guid.NewGuid().ToString("N"));
        _prepared=intent.IsValid(actor)?intent:null;
        _error=_prepared is null?"حدد تغييرا صالحا ووقتا بصيغة HH:mm ومجلدا محليا عند التفعيل.":"";Notify();
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
            var result=await backend.SaveMonthlyExportScheduleAsync(actor,intent,pending.Token);
            if(generation!=_generation || !ContextValid)return;
            if(result is null || !intent.Matches(result) || pending.IsCancellationRequested)
                throw new InvalidOperationException("Unverified monthly export setting receipt.");
            _receipt=result;_prepared=null;_uncertain=false;
        }
        catch(NativeMonthlyExportScheduleRejectedException)
        {
            if(generation==_generation && ContextValid)
            {
                _reload=true;_prepared=null;_uncertain=false;
                _error="رفضت الخدمة الإعداد. أغلق المراجعة لتحديث البيانات والتحقق من المجلد.";
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
        _prepared=null;_receipt=null;_busy=_attempted=_uncertain=_reload=_enabled=false;_time=_directory=_error="";Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();ConfirmCommand.NotifyAvailabilityChanged();
    }
}
