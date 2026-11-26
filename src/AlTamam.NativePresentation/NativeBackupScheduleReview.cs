using System.ComponentModel;
using System.Globalization;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeBackupScheduleIntent(NativeSettingsOverview Before,bool Enabled,string Time,
    int RetentionCount,string OperationId)
{
    public bool IsValid(NativeIdentity actor)=>Before is not null && Before.Matches(actor) &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        (!Enabled || Before.DirectoryAvailable) &&
        TimeOnly.TryParseExact(Time,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        (RetentionCount==0 || RetentionCount is >=2 and <=365) &&
        Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
    public string EnabledLabel=>Enabled?"تفعيل":"إيقاف";
    public string RetentionLabel=>RetentionCount==0?"الاحتفاظ بجميع النسخ التلقائية":$"الاحتفاظ بآخر {RetentionCount} نسخة تلقائية موثوقة";
}

public sealed record NativeBackupScheduleReceipt(bool Enabled,string Time,int RetentionCount)
{
    public bool Matches(NativeBackupScheduleIntent intent)=>Enabled==intent.Enabled &&
        Time==intent.Time && RetentionCount==intent.RetentionCount;
}

public sealed class NativeBackupScheduleRejectedException:Exception
{
    public NativeBackupScheduleRejectedException():base("Canonical backup schedule rejected.") { }
}

public interface INativeBackupScheduleBackend
{
    Task<NativeBackupScheduleReceipt> SaveBackupScheduleAsync(NativeIdentity actor,
        NativeBackupScheduleIntent intent,CancellationToken ct=default);
}

public sealed class NativeBackupScheduleReview:INotifyPropertyChanged
{
    private INativeBackupScheduleBackend? _backend;
    private NativeIdentity? _actor;
    private NativeSettingsOverview? _before;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeSettingsOverview?>? _current;
    private long _generation;
    private bool _busy,_uncertain,_enabled;
    private string _time="",_retention="";
    public NativeBackupScheduleReview()
    {
        PreviewCommand=new NativeCommand(_=>Preview(),_=>CanEdit);
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand PreviewCommand { get; }
    public NativeCommand ConfirmCommand { get; }
    public bool IsOpen=>_actor is not null && _actor==_liveActor?.Invoke();
    private bool ContextValid()=>IsOpen && _before is not null &&
        ReferenceEquals(_current?.Invoke(),_before) && RoleAuthorizationPolicy.CanAccessUsersManagement(_actor!.Role);
    public bool IsBusy=>IsOpen && _busy;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>ContextValid() && !_busy && !_uncertain && !NeedsReload && Receipt is null;
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public bool CanConfirm=>ContextValid() && !_busy && !NeedsReload && Prepared is not null && Receipt is null;
    public bool HasPreview=>IsOpen && Prepared is not null;
    public NativeSettingsOverview? Before=>IsOpen?_before:null;
    public NativeBackupScheduleIntent? Prepared { get; private set; }
    public NativeBackupScheduleReceipt? Receipt { get; private set; }
    public bool NeedsReload { get; private set; }
    public string Error { get; private set; }="";
    public string ConfirmLabel=>_uncertain?"إعادة تأكيد الإعداد نفسه":"حفظ إعداد الجدولة المعتمد";
    public string CompletionMessage=>Receipt is null?"":"تم حفظ إعداد الجدولة والتحقق من إيصال الحفظ.";
    public bool Enabled
    {
        get=>_enabled;
        set { if(!CanEdit || _enabled==value) return; _enabled=value; InvalidatePreview(); }
    }
    public string Time
    {
        get=>_time;
        set { if(!CanEdit || _time==value) return; _time=value??""; InvalidatePreview(); }
    }
    public string RetentionText
    {
        get=>_retention;
        set { if(!CanEdit || _retention==value) return; _retention=value??""; InvalidatePreview(); }
    }
    private void InvalidatePreview() { Prepared=null; Error=""; Notify(); }
    public bool Open(INativeBackupScheduleBackend backend,NativeIdentity actor,NativeSettingsOverview before,
        Func<NativeIdentity?> liveActor,Func<NativeSettingsOverview?> current)
    {
        if(_actor is not null || liveActor()!=actor || !ReferenceEquals(current(),before) ||
            !before.Matches(actor) || !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)) return false;
        _backend=backend; _actor=actor; _before=before; _liveActor=liveActor; _current=current;
        _enabled=before.BackupEnabled; _time=before.BackupTime;
        _retention=before.RetentionCount.ToString(CultureInfo.InvariantCulture);
        Notify(); return true;
    }
    public void Preview()
    {
        if(!CanEdit) return;
        Prepared=null;
        if(int.TryParse(_retention.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var retention))
        {
            var intent=new NativeBackupScheduleIntent(_before!,_enabled,_time.Trim(),retention,Guid.NewGuid().ToString("N"));
            if(intent.IsValid(_actor!)) Prepared=intent;
        }
        Error=Prepared is null?"تحقق من الوقت (HH:mm)، وعدد النسخ (0 أو 2 إلى 365)، وتوفر المجلد قبل التفعيل.":"";
        Notify();
    }
    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || _actor is not { } actor || Prepared is not { } intent || ct.IsCancellationRequested) return;
        bool retry=_uncertain;
        long generation=++_generation;
        _busy=true; _uncertain=true; Error=""; Notify();
        try
        {
            var receipt=await _backend.SaveBackupScheduleAsync(actor,intent,ct);
            if(generation!=_generation || !ContextValid()) return;
            if(receipt is null || !receipt.Matches(intent))
                throw new InvalidOperationException("Unverified backup schedule receipt.");
            Receipt=receipt; Prepared=null; _uncertain=false;
        }
        catch(NativeBackupScheduleRejectedException)
        {
            if(generation==_generation && ContextValid())
            {
                if(!retry)
                {
                    NeedsReload=true; Prepared=null; _uncertain=false;
                    Error="رفضت الخدمة الإعداد. أغلق المراجعة لتحديث الإعدادات والمسار.";
                }
                else Error="لا يمكن إثبات نتيجة المحاولة السابقة. أعد تأكيد الإعداد نفسه أو سجل الخروج وراجع الإعدادات وسجل العمليات.";
            }
        }
        catch
        {
            if(generation==_generation && ContextValid())
                Error="نتيجة الحفظ غير مؤكدة. أعد تأكيد الإعداد نفسه فقط.";
        }
        finally
        {
            if(generation==_generation)
            {
                _busy=false;
                if(!IsOpen) ForceClear(); else Notify();
            }
        }
    }
    public bool Close()
    {
        if(!CanClose) return false;
        ForceClear(); return true;
    }
    public void ForceClear()
    {
        ++_generation; _backend=null; _actor=null; _before=null; _liveActor=null; _current=null;
        _busy=_uncertain=_enabled=NeedsReload=false; _time=_retention=Error="";
        Prepared=null; Receipt=null; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged(); ConfirmCommand.NotifyAvailabilityChanged();
    }
}
