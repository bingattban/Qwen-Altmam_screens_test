using System.ComponentModel;

namespace AlTamam.NativePresentation;

public sealed class NativeAuditEditReview : INotifyPropertyChanged
{
    private INativeAuditEditBackend? _backend;
    private NativeIdentity? _actor;
    private NativeAuditRow? _row;
    private NativeAuditDetail? _detail;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeAuditRow?>? _currentRow;
    private Func<NativeAuditDetail?>? _currentDetail;
    private string _description="",_reason="";
    private int _generation;
    private bool _busy,_uncertain;

    public NativeAuditEditReview()
    {
        PreviewCommand=new NativeCommand(_=>Preview(),_=>CanEdit);
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand PreviewCommand { get; }
    public NativeCommand ConfirmCommand { get; }
    public NativeAuditEditRequest? Prepared { get; private set; }
    public NativeAuditEditReceipt? Receipt { get; private set; }
    public bool NeedsReload { get; private set; }
    public string Error { get; private set; }="";
    public bool IsOpen=>_row is not null;
    public bool IsBusy=>_busy;
    public bool HasUnverifiedCommit=>_uncertain;
    public bool CanEdit=>IsOpen && !_busy && !_uncertain && !NeedsReload && Receipt is null && ContextValid();
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public bool CanConfirm=>!_busy && !NeedsReload && Prepared is not null && ContextValid();
    public bool HasPreview=>Prepared is not null;
    public string BeforeDescription=>_row?.Description??"";
    public string ConfirmLabel=>_uncertain?"إعادة تأكيد العملية نفسها":"تأكيد تعديل الوصف";
    public string CompletionMessage=>Receipt is null?"":$"تم اعتماد التعديل. رقم قيد العملية: {Receipt.OperationAuditEventId}";
    public string Description
    {
        get=>_description;
        set { if(!CanEdit || _description==value) return; _description=value??""; RevokePreview(); }
    }
    public string Reason
    {
        get=>_reason;
        set { if(!CanEdit || _reason==value) return; _reason=value??""; RevokePreview(); }
    }
    private bool ContextValid()=>_actor is { } actor && _row is { } row && _detail is { } detail &&
        _liveActor?.Invoke()==actor && ReferenceEquals(_currentRow?.Invoke(),row) &&
        ReferenceEquals(_currentDetail?.Invoke(),detail) && NativeAuditEditRequest.CanUse(row,detail,actor.Role);

    public bool Open(INativeAuditEditBackend backend,NativeIdentity actor,NativeAuditRow row,
        NativeAuditDetail detail,Func<NativeIdentity?> liveActor,Func<NativeAuditRow?> currentRow,
        Func<NativeAuditDetail?> currentDetail)
    {
        if(IsOpen || liveActor()!=actor || !ReferenceEquals(currentRow(),row) ||
            !ReferenceEquals(currentDetail(),detail) || !NativeAuditEditRequest.CanUse(row,detail,actor.Role))
            return false;
        _backend=backend; _actor=actor; _row=row; _detail=detail;
        _liveActor=liveActor; _currentRow=currentRow; _currentDetail=currentDetail;
        _description=row.Description;
        Notify();
        return true;
    }

    private void RevokePreview()
    {
        Prepared=null; Error=""; Notify();
    }

    // The canonical service has no preview endpoint: this freezes the reviewed
    // exact read evidence and intended change; the service checks the version at commit.
    public void Preview()
    {
        if(!CanEdit) return;
        Prepared=null;
        var request=new NativeAuditEditRequest(_row!,_detail!,_description.Trim(),_reason.Trim(),Guid.NewGuid().ToString("N"));
        if(!request.IsValid(_actor!.Role))
            Error="أدخل وصفا مختلفا وسببا واضحا (الوصف حتى 2000 حرف والسبب حتى 400 حرف).";
        else { Prepared=request; Error=""; }
        Notify();
    }

    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || Prepared is not { } request || ct.IsCancellationRequested) return;
        int generation=++_generation;
        _busy=true; _uncertain=true; Error=""; Notify();
        try
        {
            var result=await _backend.EditAuditDescriptionAsync(_actor!,request,ct);
            if(generation!=_generation || !ContextValid()) return;
            if(result is null || !result.Matches(request))
                throw new InvalidOperationException("Unverified audit edit receipt.");
            Receipt=result; Prepared=null; _uncertain=false;
        }
        catch(NativeAuditEditStaleException)
        {
            if(generation==_generation && ContextValid())
            {
                NeedsReload=true; Prepared=null; _uncertain=false;
                Error="تغير إصدار القيد؛ أغلق المراجعة لتحديث الكشف قبل أي تعديل.";
            }
        }
        catch
        {
            if(generation==_generation)
                Error="نتيجة التعديل غير مؤكدة؛ أعد تأكيد العملية نفسها فقط.";
        }
        finally
        {
            if(generation==_generation) { _busy=false; Notify(); }
        }
    }

    public bool Close()
    {
        if(!CanClose) return false;
        ForceClear();
        return true;
    }

    public void ForceClear()
    {
        ++_generation;
        _backend=null; _actor=null; _row=null; _detail=null;
        _liveActor=null; _currentRow=null; _currentDetail=null;
        _description=_reason=Error=""; _busy=_uncertain=NeedsReload=false;
        Prepared=null; Receipt=null; Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();
        ConfirmCommand.NotifyAvailabilityChanged();
    }
}
