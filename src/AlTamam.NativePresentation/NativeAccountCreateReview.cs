using System.ComponentModel;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed class NativeAccountCreateReview : INotifyPropertyChanged
{
    private INativeAccountCreateBackend? _backend;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _liveActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _searching,_busy,_uncertain;
    private string _term="",_username="",_error="";
    private UserRole? _role;
    private NativeAccountPerson? _selected;
    private IReadOnlyList<NativeAccountPerson> _people=[];

    public NativeAccountCreateReview()
    {
        SearchCommand=new NativeCommand(_=>_ = SearchAsync(),_=>CanEdit && !_searching);
        PreviewCommand=new NativeCommand(_=>Preview(),_=>CanEdit && !_searching);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand SearchCommand { get; }
    public NativeCommand PreviewCommand { get; }
    private bool ContextValid()=>_actor is { } actor && _liveActor?.Invoke()==actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public bool IsOpen=>ContextValid();
    public bool IsSearching=>IsOpen && _searching;
    public bool IsBusy=>IsOpen && _busy;
    public bool HasUnverifiedCommit=>IsOpen && _uncertain;
    public bool CanEdit=>IsOpen && !_busy && !_uncertain && Receipt is null;
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public bool CanConfirm=>IsOpen && !_busy && !_searching && Prepared is not null && Receipt is null;
    public NativeAccountCreateIntent? Prepared { get; private set; }
    public NativeAccountCreateReceipt? Receipt { get; private set; }
    public bool HasPreview=>IsOpen && Prepared is not null;
    public IReadOnlyList<NativeAccountPerson> People=>IsOpen?_people:[];
    public IReadOnlyList<NativeAccountRoleChoice> RoleChoices=>IsOpen
        ? NativeAccountRequest.AllowedCreationRoles(_actor!.Role).Select(role=>new NativeAccountRoleChoice(role,role.ToArabicLabel())).ToArray():[];
    public string Error=>IsOpen?_error:"";
    public string ConfirmLabel=>_uncertain?"إعادة تأكيد الإنشاء بنفس كلمة المرور":"تأكيد إنشاء الحساب";
    public string CompletionMessage=>Receipt is { } receipt?$"تم إنشاء الحساب: {receipt.Username}": "";
    public string Term
    {
        get=>_term;
        set
        {
            if(!CanEdit || _term==value) return;
            _term=value??""; CancelSearch(); _people=[]; _selected=null; RevokePreview();
        }
    }
    public string Username
    {
        get=>_username;
        set { if(!CanEdit || _username==value) return; _username=value??""; RevokePreview(); }
    }
    public UserRole? Role
    {
        get=>_role;
        set
        {
            if(!CanEdit || _role==value) return;
            _role=value is { } role && RoleChoices.Any(choice=>choice.Value==role)?role:null;
            RevokePreview();
        }
    }
    public NativeAccountPerson? SelectedPerson
    {
        get=>IsOpen?_selected:null;
        set
        {
            if(!CanEdit || ReferenceEquals(_selected,value)) return;
            _selected=value is not null && _people.Any(person=>ReferenceEquals(person,value))?value:null;
            RevokePreview();
        }
    }
    public bool Open(INativeAccountCreateBackend backend,NativeIdentity actor,Func<NativeIdentity?> liveActor)
    {
        if(_actor is not null || liveActor()!=actor || !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)) return false;
        _backend=backend; _actor=actor; _liveActor=liveActor; Notify(); return true;
    }
    private void CancelSearch()
    {
        ++_generation; _pending?.Cancel(); _pending=null; _searching=false;
    }
    private void RevokePreview() { Prepared=null; _error=""; Notify(); }

    public async Task SearchAsync(CancellationToken ct=default)
    {
        if(!CanEdit || _backend is null || _actor is not { } actor) return;
        CancelSearch(); _people=[]; _selected=null; Prepared=null; _error="";
        string term=_term.Trim();
        if(!NativeAccountPersonSearch.ValidTerm(term))
        {
            _error="أدخل اسما أو رقما صالحا للبحث (حتى 100 حرف)."; Notify(); return;
        }
        int generation=_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _searching=true; Notify();
        try
        {
            var result=await _backend.SearchAccountPeopleAsync(actor,term,pending.Token);
            if(generation!=_generation || pending.IsCancellationRequested || !ContextValid()) return;
            if(result is null || !result.Matches(term))
                throw new InvalidOperationException("Invalid active person search.");
            _people=Array.AsReadOnly(result.People.ToArray());
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
        catch
        {
            if(generation==_generation && ContextValid()) _error="تعذر البحث عن أفراد كشف القوة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _searching=false;
                if(!ContextValid()) ForceClear(); else Notify();
            }
        }
    }

    public void Preview()
    {
        if(!CanEdit || _searching) return;
        Prepared=null;
        if(_selected is { } person && _people.Any(x=>ReferenceEquals(x,person)) && _role is { } role)
        {
            var intent=new NativeAccountCreateIntent(person,_username.Trim(),role,Guid.NewGuid().ToString("N"));
            if(intent.IsValid(_actor!.Role)) Prepared=intent;
        }
        _error=Prepared is null?"اختر فردا نشطا وصلاحية متاحة واسم مستخدم صالحا من 3 إلى 64 حرفا بلا مسافات.":"";
        Notify();
    }

    // Secrets exist only as arguments of this call, never as ViewModel fields.
    // On an ambiguous response the operator re-enters the same secret; the
    // canonical operation ID/payload receipt enforces exact replay.
    public async Task ConfirmAsync(string password,string confirmation,CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || _actor is not { } actor || Prepared is not { } intent || ct.IsCancellationRequested) return;
        if(!NativeAccountCreateIntent.ValidPassword(password,confirmation))
        {
            _error="كلمتا المرور يجب أن تتطابقا وتحتويا على ثلاثة أحرف إنجليزية وثلاثة أرقام على الأقل دون مسافات."; Notify(); return;
        }
        int generation=++_generation;
        _busy=true; _uncertain=true; _error=""; Notify();
        try
        {
            var receipt=await _backend.CreateAccountAsync(actor,intent,password,confirmation,ct);
            if(generation!=_generation || !ContextValid()) return;
            if(receipt is null || !receipt.Matches(intent) || receipt.UserId==actor.UserId)
                throw new InvalidOperationException("Unverified account creation receipt.");
            Receipt=receipt; Prepared=null; _uncertain=false;
        }
        catch(NativeAccountCreateRejectedException)
        {
            if(generation==_generation && ContextValid())
            {
                Prepared=null; _uncertain=false; _error="رفضت الخدمة إنشاء الحساب. تحقق من الاسم والفرد والصلاحية ثم أعد المعاينة.";
            }
        }
        catch
        {
            if(generation==_generation && ContextValid())
                _error="نتيجة الإنشاء غير مؤكدة. أعد تأكيد العملية نفسها بكلمة المرور الأصلية.";
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
        CancelSearch(); _backend=null; _actor=null; _liveActor=null;
        _people=[]; _selected=null; _role=null; _term=_username=_error="";
        _busy=_uncertain=false; Prepared=null; Receipt=null; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        SearchCommand.NotifyAvailabilityChanged(); PreviewCommand.NotifyAvailabilityChanged();
    }
}
