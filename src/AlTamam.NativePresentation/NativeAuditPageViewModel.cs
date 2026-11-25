using System.ComponentModel;
using System.Runtime.CompilerServices;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Read-only audit list. Draft edits invalidate the displayed page; only explicit
/// requests read the canonical store. Session and request generations fence late responses.</summary>
public sealed class NativeAuditPageViewModel : NativePageViewModel, INotifyPropertyChanged
{
    private INativeAuditReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _boundActor;
    private NativeAuditFilter _draft=new();
    private NativeAuditSnapshot? _snapshot;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _loading;
    private string _error="";
    private NativeAuditRow? _detailRow;
    private NativeAuditDetail? _detail;
    private CancellationTokenSource? _detailPending;
    private long _detailGeneration;
    private bool _detailLoading;
    private string _detailError="";

    public NativeAuditPageViewModel() : base(NativeScreen.AuditLog,"سجل العمليات")
    {
        SearchCommand=new NativeCommand(_=>_ = SearchAsync(),_=>CanRead);
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead);
        NextCommand=new NativeCommand(_=>_ = NextAsync(),_=>CanPage && _snapshot!.HasNext && _snapshot.Filter.Page<100000);
        PreviousCommand=new NativeCommand(_=>_ = PreviousAsync(),_=>CanPage && _snapshot!.Filter.Page>1);
        ClearFiltersCommand=new NativeCommand(_=>_ = ClearFiltersAsync(),_=>CanRead);
        OpenDetailCommand=new NativeCommand(row=>{if(row is NativeAuditRow item) _=OpenDetailAsync(item);},
            row=>row is NativeAuditRow item && CanOpenDetail(item));
        CloseDetailCommand=new NativeCommand(_=>CloseDetail(),_=>IsDetailOpen);
    }

    private NativeIdentity? AuthorizedActor => _liveActor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role) ? actor : null;
    public bool CanRead => _reader is not null && AuthorizedActor is not null;
    public bool CanIncludeTombstoned => AuthorizedActor is { } actor &&
        RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role);
    private bool CanPage => !_loading && IsDataConnected;
    public override bool IsDataConnected => _snapshot is not null && _boundActor is not null &&
        _boundActor==AuthorizedActor && _snapshot.Filter.IsAuthorized(_boundActor.Role);
    public override string StatusMessage => "سجل العمليات للقراءة فقط — لا يمكن تعديل أو حذف الدليل الأصلي.";
    public IReadOnlyList<NativeAuditRow> Rows => IsDataConnected ? _snapshot!.Rows : [];
    public long Total => IsDataConnected ? _snapshot!.Total : 0;
    public int Page => IsDataConnected ? _snapshot!.Filter.Page : 1;
    public string PageLabel => IsDataConnected ? $"الصفحة {Page} — إجمالي القيود: {Total}" : "لم يتم تحميل السجل";
    public bool IsLoading => _loading;
    public string Error => _error;
    private bool IsCurrentRow(NativeAuditRow row)=>IsDataConnected &&
        Rows.Any(current=>ReferenceEquals(current,row));
    public bool IsDetailOpen=>_detailRow is { } row && IsCurrentRow(row);
    public bool IsDetailLoading=>IsDetailOpen && _detailLoading;
    public NativeAuditDetail? Detail=>IsDetailOpen ? _detail : null;
    public string DetailError=>IsDetailOpen ? _detailError : "";
    public NativeAuditRow? DetailRow=>IsDetailOpen ? _detailRow : null;
    public NativeCommand OpenDetailCommand { get; }
    public NativeCommand CloseDetailCommand { get; }
    private bool CanOpenDetail(NativeAuditRow row)=>_reader is INativeAuditDetailReader &&
        !_loading && IsCurrentRow(row) && !IsDetailLoading;
    public string Term { get=>_draft.Term; set=>SetDraft(_draft with { Term=value??"" }); }
    public string Actor { get=>_draft.Actor; set=>SetDraft(_draft with { Actor=value??"" }); }
    public string Target { get=>_draft.Target; set=>SetDraft(_draft with { Target=value??"" }); }
    public string Operation { get=>_draft.Operation; set=>SetDraft(_draft with { Operation=value??"" }); }
    public string FromDate { get=>_draft.FromDate; set=>SetDraft(_draft with { FromDate=value??"" }); }
    public string ToDate { get=>_draft.ToDate; set=>SetDraft(_draft with { ToDate=value??"" }); }
    public bool IncludeTombstoned
    {
        get=>_draft.IncludeTombstoned && CanIncludeTombstoned;
        set=>SetDraft(_draft with { IncludeTombstoned=value && CanIncludeTombstoned });
    }
    public NativeCommand SearchCommand { get; }
    public NativeCommand RefreshCommand { get; }
    public NativeCommand NextCommand { get; }
    public NativeCommand PreviousCommand { get; }
    public NativeCommand ClearFiltersCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachReader(INativeAuditReader reader,Func<NativeIdentity?> liveActor)
    {
        if (_reader is not null) throw new InvalidOperationException("Native audit reader already attached.");
        _reader=reader??throw new ArgumentNullException(nameof(reader));
        _liveActor=liveActor??throw new ArgumentNullException(nameof(liveActor));
        Notify();
    }

    private void SetDraft(NativeAuditFilter next,[CallerMemberName] string? property=null)
    {
        if (_draft==next) return;
        _draft=next with { Page=1 };
        Invalidate();
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
        Notify();
    }

    private NativeAuditFilter Requested(int page) => new(Term.Trim(),Actor.Trim(),Target.Trim(),
        Operation.Trim(),FromDate.Trim(),ToDate.Trim(),IncludeTombstoned,page);

    public Task SearchAsync(CancellationToken ct=default)=>ReadAsync(Requested(1),ct);
    public Task RefreshAsync(CancellationToken ct=default)=>ReadAsync(Requested(Page),ct);
    public Task NextAsync(CancellationToken ct=default)=>CanPage && _snapshot!.HasNext && Page<100000
        ? ReadAsync(Requested(Page+1),ct):Task.CompletedTask;
    public Task PreviousAsync(CancellationToken ct=default)=>CanPage && Page>1
        ? ReadAsync(Requested(Page-1),ct):Task.CompletedTask;
    public Task ClearFiltersAsync(CancellationToken ct=default)
    {
        Clear();
        return SearchAsync(ct);
    }

    private async Task ReadAsync(NativeAuditFilter filter,CancellationToken ct)
    {
        Invalidate();
        if (_reader is null || AuthorizedActor is not { } actor ||
            !filter.IsAuthorized(actor.Role) || !filter.IsValid)
        {
            _error="تحقق من صلاحية الجلسة ومعايير البحث والتواريخ.";
            Notify();
            return;
        }
        long generation=_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending;
        _loading=true;
        Notify();
        try
        {
            var result=await _reader.ReadAuditAsync(actor,filter,pending.Token);
            if (generation!=_generation || pending.IsCancellationRequested || AuthorizedActor!=actor)
                return;
            if (result is null || !result.Matches(filter))
                throw new InvalidOperationException("Invalid canonical audit page.");
            _snapshot=result with { Rows=Array.AsReadOnly(result.Rows.ToArray()) };
            _boundActor=actor;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch
        {
            if (generation==_generation && AuthorizedActor==actor)
                _error="تعذر قراءة سجل العمليات. أعد المحاولة.";
        }
        finally
        {
            if (generation==_generation)
            {
                _pending=null;
                _loading=false;
                Notify();
            }
        }
    }

    public async Task OpenDetailAsync(NativeAuditRow row,CancellationToken ct=default)
    {
        if (!CanOpenDetail(row) || _reader is not INativeAuditDetailReader reader ||
            AuthorizedActor is not { } actor) return;
        ResetDetail();
        long generation=_detailGeneration;
        _detailRow=row;
        _detailLoading=true;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _detailPending=pending;
        Notify();
        try
        {
            var result=await reader.ReadAuditDetailAsync(actor,row,pending.Token);
            if (generation!=_detailGeneration || pending.IsCancellationRequested ||
                AuthorizedActor!=actor || !IsCurrentRow(row)) return;
            if (result is null || !result.Matches(row,actor.Role))
                throw new InvalidOperationException("Canonical audit detail changed.");
            _detail=result;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch
        {
            if (generation==_detailGeneration && AuthorizedActor==actor && IsCurrentRow(row))
                _detailError="تعذر التحقق من تفاصيل القيد. حدث الكشف ثم أعد المحاولة.";
        }
        finally
        {
            if (generation==_detailGeneration)
            {
                _detailPending=null;
                _detailLoading=false;
                if (AuthorizedActor!=actor || !IsCurrentRow(row) || pending.IsCancellationRequested)
                    ResetDetail();
                Notify();
            }
        }
    }

    private void ResetDetail()
    {
        ++_detailGeneration;
        _detailPending?.Cancel();
        _detailPending=null;
        _detailRow=null;
        _detail=null;
        _detailLoading=false;
        _detailError="";
    }
    public void CloseDetail()
    {
        ResetDetail();
        Notify();
    }

    private void Invalidate()
    {
        ResetDetail();
        ++_generation;
        _pending?.Cancel();
        _pending=null;
        _snapshot=null;
        _boundActor=null;
        _loading=false;
        _error="";
    }
    public void Clear()
    {
        Invalidate();
        _draft=new();
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        SearchCommand.NotifyAvailabilityChanged();
        RefreshCommand.NotifyAvailabilityChanged();
        NextCommand.NotifyAvailabilityChanged();
        PreviousCommand.NotifyAvailabilityChanged();
        ClearFiltersCommand.NotifyAvailabilityChanged();
        OpenDetailCommand.NotifyAvailabilityChanged();
        CloseDetailCommand.NotifyAvailabilityChanged();
    }
}
