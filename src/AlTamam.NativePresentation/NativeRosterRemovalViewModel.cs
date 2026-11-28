using System.ComponentModel;
using System.Globalization;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed class NativeRosterRemovalViewModel:INotifyPropertyChanged
{
    private INativeRosterRemovalBackend? _backend;
    private Func<NativeIdentity?>? _live;
    private Func<NativePersonnelDisplayRow,bool>? _current;
    private NativeIdentity? _actor,_uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation,_openGeneration;
    private bool _open,_busy,_attempted,_uncertain,_identityReviewed,_accountAcknowledged;
    private string _reason="",_error="";
    private IReadOnlyList<NativeBulkTransferOption> _options=[];
    private NativeRosterRemovalPreview? _preview;
    private NativeRosterRemovalReceipt? _receipt;
    private static string Today=>DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public NativeRosterRemovalViewModel()
    {
        PreviewCommand=new NativeCommand(_=>_ = PreviewAsync(),_=>CanPreview);
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
        CloseCommand=new NativeCommand(_=>Close(),_=>IsOpen && !IsBusy);
    }
    public NativeCommand PreviewCommand {get;}
    public NativeCommand ConfirmCommand {get;}
    public NativeCommand CloseCommand {get;}
    public bool IsOpen=>_open;
    public bool IsBusy=>_busy;
    public bool HasUnverifiedCommit=>_uncertain;
    public bool CanOpen=>_backend is not null && !_open && _live?.Invoke() is {} actor &&
        actor!=_uncertainActor && RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(actor.Role);
    private bool Live=>_open && _actor is {} actor && _live?.Invoke()==actor &&
        RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(actor.Role);
    private bool ContextValid=>Live && _options.Count is >=1 and <=100 &&
        _options.All(x=>_current?.Invoke(x.Row)==true && x.Row.Active && x.Row.StatusName=="على القوة");
    public bool CanEdit=>ContextValid && !_busy && !_attempted;
    public IReadOnlyList<NativeBulkTransferOption> Options=>Live?_options:[];
    public int SelectedCount=>Options.Count(x=>x.IsSelected);
    public string Error=>Live?_error:"";
    public NativeRosterRemovalPreview? Preview=>Live?_preview:null;
    public NativeRosterRemovalReceipt? Receipt=>Live?_receipt:null;
    public bool HasPreview=>Preview is not null;
    public IReadOnlyList<NativeRosterRemovalCandidate> Candidates=>Preview?.Candidates??[];
    public string CompletionMessage=>Receipt is {} receipt?$"تمت أرشفة {receipt.ArchivedPersonIds.Count} فرد من القوة بتاريخ {receipt.BusinessDate} مع حفظ التاريخ.":"";
    public string Reason {get=>_reason;set{if(!CanEdit || value==_reason)return;_reason=value??"";Invalidate();}}
    public bool IdentityReviewed {get=>_identityReviewed;set{if(!CanEdit || !HasPreview)return;_identityReviewed=value;Notify();}}
    public bool AccountSuspensionAcknowledged {get=>_accountAcknowledged;set{if(!CanEdit || !HasPreview)return;_accountAcknowledged=value;Notify();}}
    private bool ValidReason=>NativeRosterRemovalEvidence.ValidReason(_reason.Trim()) && !_reason.Any(char.IsControl);
    private NativeRosterRemovalMember[] Selection()=>_options.Where(x=>x.IsSelected).Select(x=>Member(x.Row)).ToArray();
    private static NativeRosterRemovalMember Member(NativePersonnelDisplayRow r)=>new(r.PersonId,r.Name,r.ServiceNumberA,r.SiteName,r.Revision);
    public bool CanPreview=>CanEdit && ValidReason && NativeRosterRemovalEvidence.ValidSelection(Selection());
    public bool CanConfirm=>CanPreview && _identityReviewed && _accountAcknowledged &&
        _preview is {CanApprove:true} p && p.BusinessDate==Today && p.SessionNonce==_actor?.SessionNonce &&
        p.Candidates.Select(x=>x.Member).SequenceEqual(Selection());
    public void AttachBackend(INativeRosterRemovalBackend backend,Func<NativeIdentity?> live,
        Func<NativePersonnelDisplayRow,bool> current)
    {
        if(_backend is not null)throw new InvalidOperationException("Removal backend already attached.");
        _backend=backend;_live=live;_current=current;
    }
    public void Open(IReadOnlyList<NativePersonnelDisplayRow> rows)
    {
        if(!CanOpen || _live?.Invoke() is not {} actor || rows is not {Count:>=1 and <=100} ||
           rows.Any(r=>r is null || !r.Active || r.StatusName!="على القوة" || _current?.Invoke(r)!=true) ||
           !NativeRosterRemovalEvidence.ValidSelection(rows.Select(Member).ToArray()))return;
        _actor=actor;_open=true;
        int openGeneration=++_openGeneration;
        _options=Array.AsReadOnly(rows.Select(r=>new NativeBulkTransferOption(r,()=>openGeneration==_openGeneration && CanEdit,Invalidate)).ToArray());
        Notify();
    }
    public async Task PreviewAsync(CancellationToken ct=default)
    {
        if(!CanPreview || _backend is null || _actor is not {} actor)return;
        var members=Selection();
        int generation=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_preview=null;_identityReviewed=_accountAcknowledged=false;_error="";Notify();
        try
        {
            var result=await _backend.PreviewRosterRemovalAsync(actor,members,linked.Token);
            if(generation!=_generation || linked.IsCancellationRequested)return;
            if(!ContextValid){ForceCancel();return;}
            if(result.SessionNonce!=actor.SessionNonce)throw new InvalidDataException("Removal session mismatch.");
            _preview=NativeRosterRemovalEvidence.ValidateAndFreeze(members,result,Today);
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch{if(generation==_generation)_error="تعذرت معاينة الأرشفة؛ حدّث الكشف وراجع الأفراد.";}
        finally{if(generation==_generation){_pending=null;_busy=false;if(!Live)ForceCancel();else Notify();}}
    }
    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || _actor is not {} actor || _preview is not {} preview)return;
        int generation=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_attempted=true;Notify();
        try
        {
            var receipt=await _backend.CommitRosterRemovalAsync(actor,preview,_reason.Trim(),Guid.NewGuid().ToString("N"),linked.Token);
            if(generation!=_generation)return;
            if(linked.IsCancellationRequested || !ContextValid){ForceCancel();return;}
            _receipt=NativeRosterRemovalEvidence.ValidateReceipt(preview,receipt);
            _preview=null;
        }
        catch
        {
            if(generation==_generation)
            {
                _uncertainActor=actor;_uncertain=true;_preview=null;
                _error="لم يتم إثبات نتيجة الأرشفة؛ راجع سجل العمليات والكشف المؤرشف قبل أي إجراء جديد. لن تتكرر العملية في هذه الجلسة.";
            }
        }
        finally{if(generation==_generation){_pending=null;_busy=false;if(!Live)ForceCancel();else Notify();}}
    }
    private void Invalidate()
    {
        ++_generation;_pending?.Cancel();_pending=null;_preview=null;
        _identityReviewed=_accountAcknowledged=false;_error="";Notify();
    }
    public void Close()
    {
        if(!_open || _busy)return;
        bool refresh=_attempted;ForceCancel();if(refresh)CommittedAndClosed?.Invoke();
    }
    public void ForceCancel()
    {
        if(_attempted && _receipt is null && _actor is {} actor)_uncertainActor=actor;
        ++_generation;++_openGeneration;_pending?.Cancel();_pending=null;_actor=null;_options=[];
        _open=_busy=_attempted=_uncertain=_identityReviewed=_accountAcknowledged=false;
        _reason=_error="";_preview=null;_receipt=null;Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        PreviewCommand.NotifyAvailabilityChanged();ConfirmCommand.NotifyAvailabilityChanged();CloseCommand.NotifyAvailabilityChanged();
    }
}
