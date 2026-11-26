using System.ComponentModel;

namespace AlTamam.NativePresentation;

public sealed class NativeAccountLifecycleReview : INotifyPropertyChanged
{
    private INativeAccountLifecycleBackend? _backend;
    private NativeIdentity? _actor;
    private NativeAccountRow? _row;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeAccountRow?>? _currentRow;
    private int _generation;
    private bool _busy,_uncertain;
    private string _reason="";
    public NativeAccountLifecycleReview()
    {
        PreviewCommand=new NativeCommand(_=>Preview(),_=>CanEdit);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand PreviewCommand { get; }
    public NativeAccountAction Action { get; private set; }
    public NativeAccountLifecycleIntent? Prepared { get; private set; }
    public NativeAccountLifecycleReceipt? Receipt { get; private set; }
    public bool NeedsReload { get; private set; }
    public string Error { get; private set; }="";
    private bool ContextValid()=>_actor is { } actor && _row is { } row &&
        _liveActor?.Invoke()==actor && ReferenceEquals(_currentRow?.Invoke(),row) &&
        NativeAccountLifecycleIntent.CanUse(actor,row,Action);
    public bool IsOpen=>ContextValid();
    public NativeAccountRow? Row=>IsOpen?_row:null;
    public bool IsBusy=>IsOpen && _busy;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>IsOpen && !_busy && !_uncertain && !NeedsReload && Receipt is null;
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public bool CanConfirm=>IsOpen && !_busy && !NeedsReload && Prepared is not null;
    public bool HasPreview=>IsOpen && Prepared is not null;
    public bool NeedsPassword=>Action==NativeAccountAction.ChangePassword;
    public bool NeedsReason=>Action==NativeAccountAction.Reactivate;
    public string ActionLabel=>Action switch
    {
        NativeAccountAction.ChangePassword=>"تغيير كلمة المرور",
        NativeAccountAction.Deactivate=>"تعطيل الحساب",
        NativeAccountAction.Reactivate=>"إعادة تفعيل الحساب",
        _=>""
    };
    public string EffectLabel=>Action switch
    {
        NativeAccountAction.ChangePassword=>"تغيير كلمة المرور وإلغاء الجلسات السابقة دون تغيير الصلاحية أو الهوية.",
        NativeAccountAction.Deactivate=>"تعطيل الحساب وإلغاء جلساته مع حفظ السجل الأصلي.",
        NativeAccountAction.Reactivate=>"إعادة التفعيل مشروطة بأهلية الفرد؛ لن تستعاد الجلسات الملغاة.",
        _=>""
    };
    public string ConfirmLabel=>_uncertain?"إعادة تأكيد العملية نفسها":"تأكيد "+ActionLabel;
    public string CompletionMessage=>Receipt is null?"":"تم اعتماد "+ActionLabel+" بنجاح.";
    public string Reason
    {
        get=>_reason;
        set { if(!CanEdit || !NeedsReason || _reason==value) return; _reason=value??""; Prepared=null; Error=""; Notify(); }
    }
    public bool Open(INativeAccountLifecycleBackend backend,NativeIdentity actor,NativeAccountRow row,
        NativeAccountAction action,Func<NativeIdentity?> liveActor,Func<NativeAccountRow?> currentRow)
    {
        if(_actor is not null || liveActor()!=actor || !ReferenceEquals(currentRow(),row) ||
            !NativeAccountLifecycleIntent.CanUse(actor,row,action)) return false;
        _backend=backend; _actor=actor; _row=row; Action=action; _liveActor=liveActor; _currentRow=currentRow;
        Notify(); return true;
    }
    public void Preview()
    {
        if(!CanEdit) return;
        Prepared=null;
        var intent=new NativeAccountLifecycleIntent(_row!,Action,NeedsReason?_reason.Trim():"",
            Guid.NewGuid().ToString("N"));
        if(intent.IsValid(_actor!)) { Prepared=intent; Error=""; }
        else Error="تحقق من الحساب والصلاحية. إعادة التفعيل تتطلب سببا من 5 إلى 400 حرف.";
        Notify();
    }
    public async Task ConfirmAsync(string password,string confirmation,CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || _actor is not { } actor || Prepared is not { } intent || ct.IsCancellationRequested) return;
        if(NeedsPassword ? !NativeAccountCreateIntent.ValidPassword(password,confirmation)
            : password!="" || confirmation!="")
        {
            Error="تحقق من تطابق كلمة المرور وشروطها؛ إجراءات التفعيل والتعطيل لا تتطلب كلمة مرور."; Notify(); return;
        }
        bool retry=_uncertain;
        int generation=++_generation;
        _busy=true; _uncertain=true; Error=""; Notify();
        try
        {
            var result=await _backend.ApplyAccountActionAsync(actor,intent,password,ct);
            if(generation!=_generation || !ContextValid()) return;
            if(result is null || !result.Matches(intent))
                throw new InvalidOperationException("Unverified account lifecycle receipt.");
            Receipt=result; Prepared=null; _uncertain=false;
        }
        catch(NativeAccountLifecycleRejectedException)
        {
            if(generation==_generation && ContextValid())
            {
                if(!retry)
                {
                    NeedsReload=true; Prepared=null; _uncertain=false;
                    Error="رفضت الخدمة العملية. أغلق المراجعة لتحديث الكشف والتحقق من أهلية الحساب.";
                }
                else Error="تعذر إثبات نتيجة المحاولة السابقة. أعد تأكيد العملية نفسها، أو سجل الخروج ثم راجع الحساب وسجل العمليات.";
            }
        }
        catch
        {
            if(generation==_generation && ContextValid())
                Error="نتيجة العملية غير مؤكدة. أعد تأكيد نفس العملية"+(NeedsPassword?" بكلمة المرور الأصلية.":".");
        }
        finally
        {
            if(generation==_generation)
            {
                _busy=false;
                if(!ContextValid()) ForceClear(); else Notify();
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
        ++_generation; _backend=null; _actor=null; _row=null; _liveActor=null; _currentRow=null;
        _busy=_uncertain=NeedsReload=false; Prepared=null; Receipt=null; _reason=Error=""; Action=default;
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();
    }
}
