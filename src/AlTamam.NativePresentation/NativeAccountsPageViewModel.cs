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
        SearchCommand=new NativeCommand(_=>_ = SearchAsync(),_=>CanRead);
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead);
        NextCommand=new NativeCommand(_=>_ = NextAsync(),_=>CanPage && _page!.HasNext && Page<100000);
        PreviousCommand=new NativeCommand(_=>_ = PreviousAsync(),_=>CanPage && Page>1);
    }

    private NativeIdentity? AuthorizedActor=>_liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)?actor:null;
    public bool CanRead=>_reader is not null && AuthorizedActor is not null;
    public override bool IsDataConnected=>_page is not null && _boundActor is not null && _boundActor==AuthorizedActor;
    public override string StatusMessage=>"قائمة الحسابات والصلاحيات — القراءة فقط في هذه المرحلة.";
    public bool IsLoading=>_loading;
    private bool CanPage=>!IsLoading && IsDataConnected;
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
            if(_term==value) return;
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
        Invalidate(); _term=""; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        SearchCommand.NotifyAvailabilityChanged(); RefreshCommand.NotifyAvailabilityChanged();
        NextCommand.NotifyAvailabilityChanged(); PreviousCommand.NotifyAvailabilityChanged();
    }
}
