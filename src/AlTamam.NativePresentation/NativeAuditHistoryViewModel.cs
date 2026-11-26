using System.ComponentModel;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed class NativeAuditHistoryViewModel : INotifyPropertyChanged
{
    private INativeAuditHistoryReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private NativeAuditHistoryRequest? _request;
    private NativeAuditHistoryPage? _page;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _loading;
    private string _error="";

    public NativeAuditHistoryViewModel()
    {
        NextCommand=new NativeCommand(_=>_ = NextAsync(),_=>IsOpen && !IsLoading && _page?.MayHaveNext==true);
        PreviousCommand=new NativeCommand(_=>_ = PreviousAsync(),_=>IsOpen && !IsLoading && Page>1);
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>IsOpen && !IsLoading);
        CloseCommand=new NativeCommand(_=>Close(),_=>IsOpen);
    }
    private NativeIdentity? AuthorizedActor=>_liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role)?actor:null;
    public bool CanOpen=>_reader is not null && AuthorizedActor is not null;
    public bool IsOpen=>_actor is not null && _actor==AuthorizedActor && _request is not null;
    public bool IsLoading=>IsOpen && _loading;
    public IReadOnlyList<NativeAuditHistoryRow> Rows=>IsOpen && _page is not null?_page.Rows:[];
    public int Page=>IsOpen?_request!.Page:1;
    public string ScopeLabel=>!IsOpen?"":_request!.AuditEventId.Length==0?"جميع القيود":$"القيد: {_request.AuditEventId}";
    public string PageLabel=>$"الصفحة {Page} — عدد القيود في الصفحة: {Rows.Count}";
    public string Error=>IsOpen?_error:"";
    public NativeCommand NextCommand { get; }
    public NativeCommand PreviousCommand { get; }
    public NativeCommand RefreshCommand { get; }
    public NativeCommand CloseCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Attach(INativeAuditHistoryReader reader,Func<NativeIdentity?> liveActor)
    {
        if(_reader is not null) throw new InvalidOperationException("History reader already attached.");
        _reader=reader; _liveActor=liveActor;
        Notify();
    }
    public Task OpenAsync(string auditEventId,CancellationToken ct=default)
    {
        if(!CanOpen || AuthorizedActor is not { } actor) return Task.CompletedTask;
        var request=new NativeAuditHistoryRequest(auditEventId);
        if(!request.IsValid) return Task.CompletedTask;
        Close();
        _actor=actor;
        _request=request;
        return ReadAsync(request,ct);
    }
    public Task RefreshAsync(CancellationToken ct=default)=>IsOpen && !IsLoading
        ? ReadAsync(_request!,ct):Task.CompletedTask;
    public Task NextAsync(CancellationToken ct=default)=>IsOpen && !IsLoading && _page?.MayHaveNext==true
        ? ReadAsync(_request! with { Page=Page+1 },ct):Task.CompletedTask;
    public Task PreviousAsync(CancellationToken ct=default)=>IsOpen && !IsLoading && Page>1
        ? ReadAsync(_request! with { Page=Page-1 },ct):Task.CompletedTask;

    private async Task ReadAsync(NativeAuditHistoryRequest request,CancellationToken ct)
    {
        if(!IsOpen || _reader is null || _actor is not { } actor || !request.IsValid ||
            !request.IsAuthorized(actor.Role)) return;
        int generation=++_generation;
        _pending?.Cancel();
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _request=request; _page=null; _loading=true; _error="";
        Notify();
        try
        {
            var result=await _reader.ReadAuditHistoryAsync(actor,request,pending.Token);
            if(generation!=_generation || pending.IsCancellationRequested || AuthorizedActor!=actor) return;
            if(result is null || !result.Matches(request))
                throw new InvalidOperationException("Invalid canonical audit history page.");
            _page=result with { Rows=Array.AsReadOnly(result.Rows.ToArray()) };
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
        catch
        {
            if(generation==_generation && AuthorizedActor==actor)
                _error="تعذر قراءة تاريخ التعديلات. أعد المحاولة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _loading=false;
                if(AuthorizedActor!=actor || pending.IsCancellationRequested) Close();
                else Notify();
            }
        }
    }
    public void Close()
    {
        ++_generation; _pending?.Cancel(); _pending=null;
        _actor=null; _request=null; _page=null; _loading=false; _error="";
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        NextCommand.NotifyAvailabilityChanged(); PreviousCommand.NotifyAvailabilityChanged();
        RefreshCommand.NotifyAvailabilityChanged(); CloseCommand.NotifyAvailabilityChanged();
    }
}
