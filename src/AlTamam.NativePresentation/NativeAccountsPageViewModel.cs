using System.ComponentModel;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed class NativeAccountsPageViewModel : NativePageViewModel,INotifyPropertyChanged
{
    private INativeAccountReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _boundActor;
    private NativeAccountPage? _page;
    private CancellationTokenSource? _pending;
    private long _generation;
    private string _term="",_error="";
    private bool _loading;

    public NativeAccountsPageViewModel():base(NativeScreen.Accounts,"حسابات المستخدمين")
    {
        SearchCommand=new NativeCommand(_=>_ = SearchAsync(),_=>CanQuery);
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanQuery);
        NextCommand=new NativeCommand(_=>_ = NextAsync(),_=>CanPage && _page!.HasNext && Page<100000);
        PreviousCommand=new NativeCommand(_=>_ = PreviousAsync(),_=>CanPage && Page>1);
        OpenCreateCommand=new NativeCommand(_=>OpenCreate(),_=>CanOpenCreate);
        CloseCreateCommand=new NativeCommand(_=>_ = CloseCreateAsync(),_=>Create.CanClose);
        ChangePasswordCommand=new NativeCommand(row=>OpenAction(row,NativeAccountAction.ChangePassword),row=>CanOpenAction(row,NativeAccountAction.ChangePassword));
        DeactivateCommand=new NativeCommand(row=>OpenAction(row,NativeAccountAction.Deactivate),row=>CanOpenAction(row,NativeAccountAction.Deactivate));
        ReactivateCommand=new NativeCommand(row=>OpenAction(row,NativeAccountAction.Reactivate),row=>CanOpenAction(row,NativeAccountAction.Reactivate));
        CloseLifecycleCommand=new NativeCommand(_=>_ = CloseLifecycleAsync(),_=>Lifecycle.CanClose);
        Lifecycle.PropertyChanged+=(_,_)=>Notify();
        Create.PropertyChanged+=(_,_)=>Notify();
    }

    private NativeIdentity? AuthorizedActor=>_liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)?actor:null;
    public bool CanRead=>_reader is not null && AuthorizedActor is not null;
    public bool CanQuery=>CanRead && !Create.IsOpen && !Lifecycle.IsOpen;
    public NativeAccountCreateReview Create { get; }=new();
    public NativeAccountLifecycleReview Lifecycle { get; }=new();
    public NativeCommand ChangePasswordCommand { get; }
    public NativeCommand DeactivateCommand { get; }
    public NativeCommand ReactivateCommand { get; }
    public NativeCommand CloseLifecycleCommand { get; }
    private NativeAccountRow? CurrentRow(NativeAccountRow row)=>Rows.FirstOrDefault(item=>ReferenceEquals(item,row));
    public bool CanOpenAction(object? parameter,NativeAccountAction action)=>CanQuery && !IsLoading &&
        _reader is INativeAccountLifecycleBackend && AuthorizedActor is { } actor &&
        parameter is NativeAccountRow row && CurrentRow(row) is not null &&
        NativeAccountLifecycleIntent.CanUse(actor,row,action);
    public void OpenAction(object? parameter,NativeAccountAction action)
    {
        if(CanOpenAction(parameter,action) && parameter is NativeAccountRow row &&
            _reader is INativeAccountLifecycleBackend backend && AuthorizedActor is { } actor)
            Lifecycle.Open(backend,actor,row,action,()=>AuthorizedActor,()=>CurrentRow(row));
    }
    public async Task CloseLifecycleAsync(CancellationToken ct=default)
    {
        bool refresh=Lifecycle.Receipt is not null || Lifecycle.NeedsReload;
        if(!Lifecycle.Close()) return;
        if(refresh) await RefreshAsync(ct);
    }
    public NativeCommand OpenCreateCommand { get; }
    public NativeCommand CloseCreateCommand { get; }
    public bool CanOpenCreate=>CanQuery && !_loading && _reader is INativeAccountCreateBackend;
    public void OpenCreate()
    {
        if(CanOpenCreate && _reader is INativeAccountCreateBackend backend && AuthorizedActor is { } actor)
            Create.Open(backend,actor,()=>AuthorizedActor);
    }
    public async Task CloseCreateAsync(CancellationToken ct=default)
    {
        bool refresh=Create.Receipt is not null;
        if(!Create.Close()) return;
        if(refresh) await SearchAsync(ct);
    }
    public override bool IsDataConnected=>_page is not null && _boundActor is not null && _boundActor==AuthorizedActor;
    public override string StatusMessage=>"قائمة الحسابات والصلاحيات — إنشاء الحساب وإدارة كلمة المرور وحالة التفعيل وفق الصلاحيات المعتمدة.";
    public bool IsLoading=>_loading;
    private bool CanPage=>!IsLoading && IsDataConnected && !Create.IsOpen && !Lifecycle.IsOpen;
    public IReadOnlyList<NativeAccountRow> Rows=>IsDataConnected?_page!.Rows:[];
    public long Total=>IsDataConnected?_page!.Total:0;
    public int Page=>IsDataConnected?_page!.Request.Page:1;
    public string PageLabel=>IsDataConnected?$"الصفحة {Page} — إجمالي الحسابات: {Total}":"لم يتم تحميل الحسابات";
    public string Error=>_error;
    public IReadOnlyList<UserRole> AllowedCreationRoles=>AuthorizedActor is { } actor
        ? NativeAccountRequest.AllowedCreationRoles(actor.Role):[];
    public string CreationRoleLabel=>string.Join("، ",AllowedCreationRoles.Select(role=>role.ToArabicLabel()));
    public string Term
    {
        get=>_term;
        set
        {
            if(Create.IsOpen || Lifecycle.IsOpen || _term==value) return;
            _term=value??"";
            Invalidate();
            Notify();
        }
    }
    public NativeCommand SearchCommand { get; }
    public NativeCommand RefreshCommand { get; }
    public NativeCommand NextCommand { get; }
    public NativeCommand PreviousCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachReader(INativeAccountReader reader,Func<NativeIdentity?> liveActor)
    {
        if(_reader is not null) throw new InvalidOperationException("Account reader already attached.");
        _reader=reader; _liveActor=liveActor; Notify();
    }
    public Task SearchAsync(CancellationToken ct=default)=>ReadAsync(new(_term.Trim(),1),ct);
    public Task RefreshAsync(CancellationToken ct=default)=>ReadAsync(new(_term.Trim(),Page),ct);
    public Task NextAsync(CancellationToken ct=default)=>CanPage && _page!.HasNext && Page<100000
        ?ReadAsync(new(_term.Trim(),Page+1),ct):Task.CompletedTask;
    public Task PreviousAsync(CancellationToken ct=default)=>CanPage && Page>1
        ?ReadAsync(new(_term.Trim(),Page-1),ct):Task.CompletedTask;

    private async Task ReadAsync(NativeAccountRequest request,CancellationToken ct)
    {
        if(Create.IsOpen || Lifecycle.IsOpen) return;
        Invalidate();
        if(_reader is null || AuthorizedActor is not { } actor || !request.IsValid)
        {
            _error="تحقق من صلاحية الجلسة ومعايير البحث."; Notify(); return;
        }
        long generation=_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _loading=true; Notify();
        try
        {
            var result=await _reader.ReadAccountsAsync(actor,request,pending.Token);
            if(generation!=_generation || pending.IsCancellationRequested || AuthorizedActor!=actor) return;
            if(result is null || !result.Matches(request,actor))
                throw new InvalidOperationException("Invalid canonical account page.");
            _page=result with { Rows=Array.AsReadOnly(result.Rows.ToArray()) };
            _boundActor=actor;
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
        catch
        {
            if(generation==_generation && AuthorizedActor==actor)
                _error="تعذر قراءة قائمة الحسابات. أعد المحاولة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _loading=false; Notify();
            }
        }
    }
    private void Invalidate()
    {
        ++_generation; _pending?.Cancel(); _pending=null; _page=null;
        _boundActor=null; _loading=false; _error="";
    }
    public void Clear()
    {
        Lifecycle.ForceClear(); Create.ForceClear(); Invalidate(); _term=""; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        SearchCommand.NotifyAvailabilityChanged(); RefreshCommand.NotifyAvailabilityChanged();
        NextCommand.NotifyAvailabilityChanged(); PreviousCommand.NotifyAvailabilityChanged();
        OpenCreateCommand.NotifyAvailabilityChanged(); CloseCreateCommand.NotifyAvailabilityChanged();
        ChangePasswordCommand.NotifyAvailabilityChanged(); DeactivateCommand.NotifyAvailabilityChanged();
        ReactivateCommand.NotifyAvailabilityChanged(); CloseLifecycleCommand.NotifyAvailabilityChanged();
    }
}
