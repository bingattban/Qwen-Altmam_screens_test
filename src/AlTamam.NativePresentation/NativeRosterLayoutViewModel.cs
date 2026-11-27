using System.ComponentModel;
using System.Globalization;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public interface INativeRosterLayoutReader
{
    Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(NativeIdentity actor,CancellationToken ct=default);
}

public sealed record NativeRosterColumn(string ColumnId,string TechnicalKey,string Caption,int FieldType,int DisplayOrder,
    bool IsSystemProtected,bool Visible,bool Active,long StoredValueCount,string Snapshot)
{
    public string TypeLabel=>FieldType switch {1=>"نص",2=>"رقم",3=>"تاريخ",4=>"نعم/لا",_=>"غير معروف"};
    public string ProtectionLabel=>IsSystemProtected?"أساسي محمي":"إضافي";
    public string StateLabel=>!Active?"معطل":Visible?"ظاهر":"مخفي";
    public bool IsValid=>ColumnId is {Length:>=4 and <=128} && !ColumnId.Any(char.IsControl) &&
        TechnicalKey is {Length:>=1 and <=128} && !TechnicalKey.Any(char.IsControl) &&
        Caption is {Length:>=1 and <=80} && !Caption.Any(char.IsControl) &&
        FieldType is >=1 and <=4 && DisplayOrder>=1 && StoredValueCount>=0 &&
        Snapshot is {Length:64} && Snapshot.All(Uri.IsHexDigit) &&
        (IsSystemProtected?Active && Visible && DisplayOrder<=4:DisplayOrder>=5) &&
        (Active || !Visible);
    public static bool ValidCatalogue(IReadOnlyList<NativeRosterColumn> rows)=>
        rows is {Count:>=4 and <=512} && rows.All(x=>x is not null && x.IsValid) &&
        rows.Select(x=>x.ColumnId).Distinct(StringComparer.Ordinal).Count()==rows.Count &&
        rows.Select(x=>x.TechnicalKey).Distinct(StringComparer.Ordinal).Count()==rows.Count &&
        rows.Where(x=>x.IsSystemProtected).Select(x=>x.DisplayOrder).OrderBy(x=>x).SequenceEqual(new[]{1,2,3,4}) &&
        rows.Where(x=>x.IsSystemProtected).OrderBy(x=>x.DisplayOrder).Select(x=>x.ColumnId)
            .SequenceEqual(new[]{"FLD-SYS-ROWNUM","FLD-SYS-SVC-NUM","FLD-SYS-NAME","FLD-SYS-SITE"}) &&
        rows.Where(x=>x.IsSystemProtected).OrderBy(x=>x.DisplayOrder).Select(x=>x.TechnicalKey)
            .SequenceEqual(new[]{"sys_row_number","sys_service_number_a","sys_person_name","sys_site_name"}) &&
        rows.Where(x=>x.Active).Select(x=>x.DisplayOrder).OrderBy(x=>x)
            .SequenceEqual(Enumerable.Range(1,rows.Count(x=>x.Active))) &&
        rows.Count(x=>x.Active && !x.IsSystemProtected)<=64;
}

/// <summary>Canonical definition inventory and explicitly reviewed metadata changes; no value migration.</summary>
public sealed class NativeRosterLayoutViewModel:INotifyPropertyChanged
{
    private INativeRosterLayoutReader? _reader;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open,_busy;
    private IReadOnlyList<NativeRosterColumn> _rows=[];
    private string _error="";
    private NativeRosterColumnMutationReview _mutation=new();
    private NativeIdentity? _uncertainActor;
    private NativeRosterColumnOperation? _operation;
    private NativeRosterColumn? _previous;
    private string _caption="",_order="";
    private int _type=1;
    private bool _visible=true;
    private string _reviewNote="";
    public sealed record TypeOption(int Id,string Label);
    public IReadOnlyList<TypeOption> FieldTypes {get;}=[new(1,"نص"),new(2,"رقم"),new(3,"تاريخ"),new(4,"نعم/لا")];
    public NativeRosterLayoutViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !_busy && !HasDraft && !_mutation.HasUnverifiedCommit);
        CloseCommand=new NativeCommand(_=>Close(),_=>_open && !(_busy && _mutation.HasUnverifiedCommit));
        CreateCommand=new NativeCommand(_=>Begin(NativeRosterColumnOperation.Create,null),_=>CanStart);
        EditCommand=new NativeCommand(x=>Begin(NativeRosterColumnOperation.Update,x as NativeRosterColumn),
            x=>CanStart && x is NativeRosterColumn row && MayEdit(row));
        DeactivateCommand=new NativeCommand(x=>Begin(NativeRosterColumnOperation.Deactivate,x as NativeRosterColumn),
            x=>CanStart && x is NativeRosterColumn row && MayEdit(row));
        ReviewCommand=new NativeCommand(_=>ReviewDraft(),_=>CanReview);
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
        CancelDraftCommand=new NativeCommand(_=>ResetDraftAndNotify(),_=>HasDraft && !_busy && !_mutation.HasUnverifiedCommit && _mutation.Receipt is null);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public bool HasDraft=>_operation is not null;
    public bool CanEditDraft=>CanRead && !_busy && HasDraft && _operation!=NativeRosterColumnOperation.Deactivate &&
        !_mutation.HasUnverifiedCommit && _mutation.Receipt is null;
    public bool CanEditOrder=>CanEditDraft && _operation==NativeRosterColumnOperation.Update;
    public bool CanEditType=>CanEditDraft && (_previous?.StoredValueCount??0)==0;
    public bool CanReview=>CanRead && !_busy && HasDraft && _mutation.Receipt is null &&
        !_mutation.HasUnverifiedCommit && CurrentChange() is { } change && change.CanApply(_rows);
    public bool CanConfirm=>CanRead && !_busy && _mutation.CanConfirm(_liveActor?.Invoke(),_rows);
    public bool HasUnverifiedCommit=>_mutation.HasUnverifiedCommit;
    public string ReviewNote=>_reviewNote;
    public string DraftCaption {get=>_caption;set {if(CanEditDraft && _caption!=value){_caption=value??"";InvalidateReview();}}}
    public string DraftOrder {get=>_order;set {if(CanEditOrder && _order!=value){_order=value??"";InvalidateReview();}}}
    public int DraftType {get=>_type;set {if(CanEditType && _type!=value){_type=value;InvalidateReview();}}}
    public bool DraftVisible {get=>_visible;set {if(CanEditOrder && _visible!=value){_visible=value;InvalidateReview();}}}
    public string DraftHeading=>_operation switch {NativeRosterColumnOperation.Create=>"إضافة عمود",
        NativeRosterColumnOperation.Update=>"تعديل تعريف العمود",NativeRosterColumnOperation.Deactivate=>"تعطيل العمود مع إبقاء قيمه",_=>""};
    private bool CanStart=>CanRead && !_busy && !HasDraft && _reader is INativeRosterColumnMutationBackend &&
        _uncertainActor!=_actor && NativeRosterColumn.ValidCatalogue(_rows);
    private bool MayEdit(NativeRosterColumn row)=>row.Active && !row.IsSystemProtected &&
        _rows.Any(candidate=>ReferenceEquals(candidate,row));
    public NativeCommand CreateCommand {get;}
    public NativeCommand EditCommand {get;}
    public NativeCommand DeactivateCommand {get;}
    public NativeCommand ReviewCommand {get;}
    public NativeCommand ConfirmCommand {get;}
    public NativeCommand CancelDraftCommand {get;}

    private void Begin(NativeRosterColumnOperation operation,NativeRosterColumn? row)
    {
        if(!CanStart || (operation!=NativeRosterColumnOperation.Create && (row is null || !MayEdit(row))))return;
        _operation=operation;_previous=row;_caption=row?.Caption??"";_type=row?.FieldType??1;
        _order=(row?.DisplayOrder??0).ToString(CultureInfo.InvariantCulture);
        _visible=operation==NativeRosterColumnOperation.Create || (operation==NativeRosterColumnOperation.Update && row!.Visible);
        _error="";_reviewNote="";_mutation=new();Notify();
    }
    private NativeRosterColumnChange? CurrentChange()
    {
        if(_operation is not { } op || !int.TryParse(_order,NumberStyles.None,CultureInfo.InvariantCulture,out int order))return null;
        return new NativeRosterColumnChange(op,_previous,_caption,_type,order,_visible);
    }
    private void InvalidateReview()
    {
        _mutation=new();_reviewNote="";_error="";Notify();
    }
    private void ReviewDraft()
    {
        if(!CanReview || _actor is not { } actor || CurrentChange() is not { } change ||
            !_mutation.Begin(actor,change,_rows) || !_mutation.Review(actor,_rows))return;
        _reviewNote=change.Operation switch
        {
            NativeRosterColumnOperation.Create=>$"إضافة «{change.Caption}» بالنوع {FieldTypes.Single(t=>t.Id==change.FieldType).Label} في نهاية الكشف.",
            NativeRosterColumnOperation.Update=>$"تعديل «{change.Previous!.Caption}» إلى «{change.Caption}»، النوع {FieldTypes.Single(t=>t.Id==change.FieldType).Label}، الترتيب {change.DisplayOrder}، {(change.Visible?"ظاهر":"مخفي")}؛ تبقى {change.Previous.StoredValueCount} قيمة محفوظة.",
            _=>$"تعطيل «{change.Caption}» وإخفاؤه من الكشوفات؛ تبقى {change.Previous!.StoredValueCount} قيمة محفوظة دون حذف."
        };
        Notify();
    }
    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _reader is not INativeRosterColumnMutationBackend backend ||
            _mutation.Take(_liveActor?.Invoke(),_rows) is not { } operation)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_busy=true;_error="";Notify();
        try
        {
            var receipt=await backend.CommitRosterColumnAsync(operation,linked.Token);
            if(version!=_generation)return;
            if(!CanRead || linked.IsCancellationRequested) {Clear();return;}
            if(!_mutation.Accept(operation,receipt))throw new InvalidDataException("Column receipt mismatch.");
            _rows=[];_reviewNote="تم اعتماد التغيير والتحقق من التعريف المحفوظ. أغلق هذه النافذة لتحديث الكشف.";
        }
        catch
        {
            if(version==_generation)
            {
                _uncertainActor=operation.Actor;_rows=[];
                _reviewNote="";_error="لم يتم التحقق من نتيجة الحفظ. راجع تعريفات الأعمدة وسجل العمليات بعد إغلاق النافذة؛ لا تكرر العملية في هذه الجلسة.";
            }
        }
        finally
        {
            if(version==_generation){_pending=null;_busy=false;Notify();}
        }
    }
    private void ResetDraft()
    {
        _operation=null;_previous=null;_caption="";_order="";_type=1;_visible=true;
        _reviewNote="";_mutation=new();
    }
    private void ResetDraftAndNotify(){ResetDraft();_error="";Notify();}
    public void Close()
    {
        if(_busy && _mutation.HasUnverifiedCommit)return;
        bool refresh=_mutation.Receipt is not null || _mutation.HasUnverifiedCommit;
        Clear();if(refresh)CommittedAndClosed?.Invoke();
    }
    public bool IsOpen=>_open;
    public bool IsBusy=>_busy;
    public IReadOnlyList<NativeRosterColumn> Rows=>CanRead?_rows:[];
    public string Error=>_error;
    private bool CanRead=>_open && _reader is not null && _actor is { } actor &&
        _liveActor?.Invoke()==actor && RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public NativeCommand RefreshCommand {get;}
    public NativeCommand CloseCommand {get;}
    public void Attach(INativeRosterLayoutReader reader,Func<NativeIdentity?> actor)
    {
        if(_reader is not null) throw new InvalidOperationException("Roster layout already attached.");
        _reader=reader; _liveActor=actor;
    }
    public async Task OpenAsync(CancellationToken ct=default)
    {
        if(_open || _reader is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))return;
        _actor=actor; _open=true;
        await RefreshAsync(ct);
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if(!CanRead || _busy || HasDraft || _mutation.HasUnverifiedCommit || _reader is not { } reader || _actor is not { } actor)return;
        int version=++_generation;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked; _busy=true; _rows=[]; _error=""; Notify();
        try
        {
            var rows=await reader.ReadRosterLayoutAsync(actor,linked.Token);
            if(version!=_generation || linked.IsCancellationRequested)return;
            if(!CanRead){Clear();return;}
            if(!NativeRosterColumn.ValidCatalogue(rows))throw new InvalidDataException("Invalid roster layout.");
            _rows=Array.AsReadOnly(rows.ToArray());
        }
        catch(OperationCanceledException) when(linked.IsCancellationRequested){}
        catch
        {
            if(version==_generation && CanRead)_error="تعذر قراءة تعريفات الأعمدة المعتمدة.";
        }
        finally
        {
            if(version==_generation)
            {
                _pending=null; _busy=false;
                if(!CanRead)Clear();else Notify();
            }
        }
    }
    public void Clear()
    {
        if(_mutation.HasUnverifiedCommit && _actor is { } actor)_uncertainActor=actor;
        ResetDraft();
        ++_generation; _pending?.Cancel(); _pending=null; _actor=null;
        _open=false; _busy=false; _rows=[]; _error=""; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged(); CloseCommand.NotifyAvailabilityChanged();
        CreateCommand.NotifyAvailabilityChanged();EditCommand.NotifyAvailabilityChanged();DeactivateCommand.NotifyAvailabilityChanged();
        ReviewCommand.NotifyAvailabilityChanged();ConfirmCommand.NotifyAvailabilityChanged();CancelDraftCommand.NotifyAvailabilityChanged();
    }
}
