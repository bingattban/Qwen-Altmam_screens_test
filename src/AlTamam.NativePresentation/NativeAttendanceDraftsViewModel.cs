using System.ComponentModel;
using System.Globalization;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public interface INativeAttendanceDraftReader
{
    Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default);
}

public interface INativeAttendanceDraftDeleter
{
    Task<IReadOnlyList<NativeAttendanceDraft>> DeleteAttendanceDraftAsync(NativeIdentity actor,
        NativeAttendanceDraft expected,CancellationToken ct=default);
}

public sealed record NativeAttendanceDraft(string DraftId,DateOnly BusinessDate,string SiteId,
    string TargetStatusId,DateTimeOffset UpdatedAtUtc,IReadOnlyList<string> PersonIds)
{
    private static bool ValidId(string? value)=>value is {Length:>=1 and <=128} &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
    public bool IsValid=>DraftId is {Length:36} && DraftId.StartsWith("DRF-",StringComparison.Ordinal) &&
        Guid.TryParseExact(DraftId[4..],"N",out _) && BusinessDate.Year is >=1900 and <=2199 &&
        (SiteId=="" || ValidId(SiteId)) && ValidId(TargetStatusId) &&
        UpdatedAtUtc!=default && UpdatedAtUtc.Offset==TimeSpan.Zero &&
        PersonIds is {Count:>=1 and <=100} && PersonIds.All(ValidId) &&
        PersonIds.Distinct(StringComparer.Ordinal).Count()==PersonIds.Count;
    public int PersonCount=>PersonIds.Count;
    public string DateLabel=>BusinessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public string UpdatedLabel=>UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture);
    public string ScopeLabel=>SiteId==""?"جميع المواقع":SiteId;
    public static bool ValidCatalogue(IReadOnlyList<NativeAttendanceDraft>? rows)=>
        rows is {Count:<=10000} && rows.All(row=>row is not null && row.IsValid) &&
        rows.Select(row=>row.DraftId).Distinct(StringComparer.Ordinal).Count()==rows.Count;
}

/// <summary>Original shared draft screen: owner-only read, never an attendance writer.</summary>
public sealed class NativeAttendanceDraftsViewModel:INotifyPropertyChanged
{
    private INativeAttendanceDraftReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open,_busy,_restoring,_deleting;
    private NativeAttendanceDraft? _deleteSelection;
    private string _deleteNotice="";
    private Func<NativeIdentity,NativeAttendanceDraft,bool,CancellationToken,Task<bool>>? _restore;
    private IReadOnlyList<NativeAttendanceDraft> _rows=[];
    private string _error="";
    public NativeAttendanceDraftsViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !_busy && !IsDeleteReviewOpen);
        CloseCommand=new NativeCommand(_=>{if(!_restoring && !_deleting && !IsDeleteReviewOpen)Clear();},_=>_open && !_restoring && !_deleting && !IsDeleteReviewOpen);
        RestoreInitialCommand=new NativeCommand(x=>_ = RestoreAsync(x as NativeAttendanceDraft,true),x=>CanRestore(x as NativeAttendanceDraft));
        RestoreRevisionCommand=new NativeCommand(x=>_ = RestoreAsync(x as NativeAttendanceDraft,false),x=>CanRestore(x as NativeAttendanceDraft));
        BeginDeleteCommand=new NativeCommand(x=>BeginDelete(x as NativeAttendanceDraft),x=>CanBeginDelete(x as NativeAttendanceDraft));
        ConfirmDeleteCommand=new NativeCommand(_=>_ = ConfirmDeleteAsync(),_=>CanConfirmDelete);
        CancelDeleteCommand=new NativeCommand(_=>CancelDelete(),_=>IsDeleteReviewOpen && !_busy);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsOpen=>_open;
    public bool IsBusy=>_busy;
    public bool IsRestoring=>_restoring;
    public bool IsDeleting=>_deleting;
    public bool IsDeleteReviewOpen=>_deleteSelection is not null;
    public string DeleteNotice=>_deleteNotice;
    public string DeleteReviewLabel=>_deleteSelection is { } row
        ? $"حذف المسودة فقط؟ {row.DraftId} | {row.DateLabel} | {row.PersonCount} فرد | {row.TargetStatusId} | {row.ScopeLabel} | {row.UpdatedLabel}. لن يتغير الحضور المعتمد."
        : "";
    public IReadOnlyList<NativeAttendanceDraft> Rows=>CanRead?_rows:[];
    public string Error=>_error;
    public string CountLabel=>$"عدد المسودات: {Rows.Count}";
    private bool CanRead=>_open && _reader is not null && _actor is { } actor &&
        _liveActor?.Invoke()==actor && RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role);
    public NativeCommand RefreshCommand {get;}
    public NativeCommand CloseCommand {get;}
    public NativeCommand RestoreInitialCommand {get;}
    public NativeCommand RestoreRevisionCommand {get;}
    public NativeCommand BeginDeleteCommand {get;}
    public NativeCommand ConfirmDeleteCommand {get;}
    public NativeCommand CancelDeleteCommand {get;}
    private bool CanBeginDelete(NativeAttendanceDraft? row)=>CanRead && !_busy && !IsDeleteReviewOpen &&
        _reader is INativeAttendanceDraftDeleter && row is not null && row.IsValid && _rows.Any(r=>ReferenceEquals(r,row));
    private bool CanConfirmDelete=>CanRead && !_busy && _reader is INativeAttendanceDraftDeleter &&
        _deleteSelection is { } row && _rows.Any(r=>ReferenceEquals(r,row));
    public void BeginDelete(NativeAttendanceDraft? row)
    {
        if(!CanBeginDelete(row))return;
        _deleteSelection=row;_deleteNotice="";_error="";Notify();
    }
    public void CancelDelete()
    {
        if(_busy || !IsDeleteReviewOpen)return;
        _deleteSelection=null;Notify();
    }
    public async Task ConfirmDeleteAsync(CancellationToken ct=default)
    {
        if(!CanConfirmDelete || _deleteSelection is not { } row || _actor is not { } actor ||
            _reader is not INativeAttendanceDraftDeleter deleter)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_deleting=true;_deleteNotice="";_error="";Notify();
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var remaining=await deleter.DeleteAttendanceDraftAsync(actor,row,linked.Token);
            if(version!=_generation)return;
            if(!CanRead){Clear();return;}
            linked.Token.ThrowIfCancellationRequested();
            if(!NativeAttendanceDraft.ValidCatalogue(remaining) || remaining.Any(r=>r.DraftId==row.DraftId))
                throw new InvalidDataException("Invalid canonical deletion read-back.");
            _rows=Array.AsReadOnly(remaining.Select(r=>r with {PersonIds=Array.AsReadOnly(r.PersonIds.ToArray())}).ToArray());
            _deleteNotice="تم التحقق من حذف المسودة فقط.";
        }
        catch
        {
            if(version==_generation && CanRead)
            {
                _rows=[];_deleteNotice="";_error="تعذر التحقق من نتيجة حذف المسودة. حدث القائمة قبل أي محاولة جديدة.";
            }
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null;_busy=false;_deleting=false;_deleteSelection=null;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void AttachRestore(Func<NativeIdentity,NativeAttendanceDraft,bool,CancellationToken,Task<bool>> restore)
    {
        if(_restore is not null)throw new InvalidOperationException("Draft restore already attached.");
        _restore=restore??throw new ArgumentNullException(nameof(restore));
    }
    private bool CanRestore(NativeAttendanceDraft? row)=>CanRead && !_busy && !IsDeleteReviewOpen && _restore is not null &&
        row is not null && row.IsValid && row.BusinessDate==DateOnly.FromDateTime(DateTime.Now) &&
        !NativeAttendanceDateRange.RequiresRange(row.TargetStatusId) && _rows.Any(r=>ReferenceEquals(r,row));
    private static bool SameEvidence(NativeAttendanceDraft a,NativeAttendanceDraft b)=>
        a.DraftId==b.DraftId && a.BusinessDate==b.BusinessDate && a.SiteId==b.SiteId &&
        a.TargetStatusId==b.TargetStatusId && a.UpdatedAtUtc==b.UpdatedAtUtc &&
        a.PersonIds.Order(StringComparer.Ordinal).SequenceEqual(b.PersonIds.Order(StringComparer.Ordinal),StringComparer.Ordinal);
    public async Task RestoreAsync(NativeAttendanceDraft? row,bool initial,CancellationToken ct=default)
    {
        if(!CanRestore(row) || row is null || _reader is not { } reader || _actor is not { } actor || _restore is not { } restore)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_restoring=true;_error="";Notify();
        try
        {
            var fresh=await reader.ReadAttendanceDraftsAsync(actor,linked.Token);
            if(version!=_generation || linked.IsCancellationRequested)return;
            if(!CanRead){Clear();return;}
            if(!NativeAttendanceDraft.ValidCatalogue(fresh) || fresh.SingleOrDefault(r=>r.DraftId==row.DraftId) is not { } current ||
                !SameEvidence(row,current) || current.BusinessDate!=DateOnly.FromDateTime(DateTime.Now))
            {
                _rows=[];_error="تغيرت المسودة أو لم تعد متاحة. حدث القائمة قبل المحاولة.";return;
            }
            bool restored=await restore(actor,row,initial,linked.Token);
            if(version!=_generation)return;
            if(!CanRead){Clear();return;}
            // Once a live, verified preview was handed off, close the catalogue
            // even if cancellation arrived just after the successful callback.
            if(restored){Clear();return;}
            if(linked.IsCancellationRequested)return;
            _error="تعذرت المعاينة الجديدة. تحقق من نوع التحضير والحالة والموقع والأفراد. لم يعتمد أي حضور.";
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch
        {
            if(version==_generation && CanRead){_rows=[];_error="تعذر التحقق من المسودة الحالية. حدث القائمة.";}
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null;_busy=false;_restoring=false;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void Attach(INativeAttendanceDraftReader reader,Func<NativeIdentity?> actor)
    {
        if(_reader is not null)throw new InvalidOperationException("Draft reader already attached.");
        _reader=reader;_liveActor=actor;
    }
    public async Task OpenAsync(CancellationToken ct=default)
    {
        if(_open || _reader is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role))return;
        _actor=actor;_open=true;
        await RefreshAsync(ct);
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if(!CanRead || _busy || IsDeleteReviewOpen || _reader is not { } reader || _actor is not { } actor)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_rows=[];_error="";_deleteNotice="";Notify();
        try
        {
            var rows=await reader.ReadAttendanceDraftsAsync(actor,linked.Token);
            if(version!=_generation || linked.IsCancellationRequested)return;
            if(!CanRead){Clear();return;}
            if(!NativeAttendanceDraft.ValidCatalogue(rows))throw new InvalidDataException("Invalid owner draft catalogue.");
            _rows=Array.AsReadOnly(rows.Select(row=>row with {PersonIds=Array.AsReadOnly(row.PersonIds.ToArray())}).ToArray());
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch
        {
            if(version==_generation && CanRead)_error="تعذر التحقق من المسودات المحفوظة. لم يتم اعتماد أي حضور.";
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null;_busy=false;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void Clear()
    {
        ++_generation;_pending?.Cancel();_pending=null;_actor=null;
        _open=false;_busy=false;_restoring=false;_deleting=false;_deleteSelection=null;_deleteNotice="";_rows=[];_error="";Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged();CloseCommand.NotifyAvailabilityChanged();
        RestoreInitialCommand.NotifyAvailabilityChanged();RestoreRevisionCommand.NotifyAvailabilityChanged();
        BeginDeleteCommand.NotifyAvailabilityChanged();ConfirmDeleteCommand.NotifyAvailabilityChanged();CancelDeleteCommand.NotifyAvailabilityChanged();
    }
}
