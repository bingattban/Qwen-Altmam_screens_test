using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public enum NativeRosterColumnOperation { Create, Update, Deactivate }

public sealed record NativeRosterColumnChange(NativeRosterColumnOperation Operation,
    NativeRosterColumn? Previous,string Caption,int FieldType,int DisplayOrder,bool Visible)
{
    private bool ValidFields=>Caption is {Length:>=2 and <=80} && Caption==Caption.Trim() &&
        !string.IsNullOrWhiteSpace(Caption) && !Caption.Any(char.IsControl) &&
        ArabicTextNormalizer.NormalizeForSearch(Caption).Length>0 && FieldType is >=1 and <=4;

    public bool CanApply(IReadOnlyList<NativeRosterColumn> rows)
    {
        if(!ValidFields || !NativeRosterColumn.ValidCatalogue(rows))return false;
        string normalized=ArabicTextNormalizer.NormalizeForSearch(Caption);
        if(rows.Any(r=>r.Active && r.ColumnId!=Previous?.ColumnId &&
            ArabicTextNormalizer.NormalizeForSearch(r.Caption)==normalized))return false;
        if(Operation==NativeRosterColumnOperation.Create)
            return Previous is null && DisplayOrder==0 && Visible &&
                rows.Count<512 && rows.Count(r=>r.Active && !r.IsSystemProtected)<64;
        if(Previous is not {Active:true,IsSystemProtected:false} old ||
            !rows.Any(r=>r==old))return false;
        if(Operation==NativeRosterColumnOperation.Deactivate)
            return Caption==old.Caption && FieldType==old.FieldType &&
                DisplayOrder==old.DisplayOrder && !Visible;
        return Operation==NativeRosterColumnOperation.Update &&
            DisplayOrder>=5 && DisplayOrder<=rows.Count(r=>r.Active) &&
            (FieldType==old.FieldType || old.StoredValueCount==0) &&
            (Caption!=old.Caption || FieldType!=old.FieldType ||
             DisplayOrder!=old.DisplayOrder || Visible!=old.Visible);
    }

    public bool MatchesReceipt(NativeRosterColumn? result)
    {
        if(result is null || !result.IsValid || result.IsSystemProtected ||
            result.Caption!=Caption || result.FieldType!=FieldType)return false;
        if(Operation==NativeRosterColumnOperation.Create)
            return Previous is null && result.Active && result.Visible && result.StoredValueCount==0 &&
                result.ColumnId.StartsWith("FLD-",StringComparison.Ordinal) &&
                result.TechnicalKey.StartsWith("custom_",StringComparison.Ordinal);
        if(Previous is not { } old || result.ColumnId!=old.ColumnId ||
            result.TechnicalKey!=old.TechnicalKey || result.Snapshot==old.Snapshot ||
            result.StoredValueCount!=old.StoredValueCount || result.DisplayOrder!=DisplayOrder)return false;
        return Operation==NativeRosterColumnOperation.Update
            ? result.Active && result.Visible==Visible
            : Operation==NativeRosterColumnOperation.Deactivate && !result.Active && !result.Visible;
    }
}

public sealed record NativeRosterColumnCommit(NativeIdentity Actor,NativeRosterColumnChange Change,string OperationId);

public interface INativeRosterColumnMutationBackend
{
    Task<NativeRosterColumn> CommitRosterColumnAsync(NativeRosterColumnCommit operation,CancellationToken ct=default);
}

/// <summary>One human review, one operation identifier, no implicit replay after ambiguous writes.</summary>
public sealed class NativeRosterColumnMutationReview
{
    private NativeIdentity? _actor;
    private NativeRosterColumnChange? _draft,_reviewed;
    private NativeRosterColumnCommit? _issued;
    public NativeRosterColumn? Receipt {get;private set;}
    public bool HasUnverifiedCommit=>_issued is not null && Receipt is null;
    public bool Begin(NativeIdentity actor,NativeRosterColumnChange change,IReadOnlyList<NativeRosterColumn> rows)
    {
        if(_issued is not null || !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            !change.CanApply(rows))return false;
        _actor=actor;_draft=change;_reviewed=null;
        return true;
    }
    public bool Review(NativeIdentity? live,IReadOnlyList<NativeRosterColumn> rows)
    {
        if(_issued is not null || live is null || live!=_actor ||
            _draft is not { } draft || !draft.CanApply(rows))return false;
        _reviewed=draft;return true;
    }
    public bool CanConfirm(NativeIdentity? live,IReadOnlyList<NativeRosterColumn> rows)=>
        _issued is null && live is not null && live==_actor &&
        _reviewed is { } reviewed && ReferenceEquals(reviewed,_draft) && reviewed.CanApply(rows);
    public NativeRosterColumnCommit? Take(NativeIdentity? live,IReadOnlyList<NativeRosterColumn> rows)
    {
        if(!CanConfirm(live,rows))return null;
        _issued=new NativeRosterColumnCommit(live!,_reviewed!,Guid.NewGuid().ToString("N"));
        return _issued;
    }
    public bool Accept(NativeRosterColumnCommit operation,NativeRosterColumn result)
    {
        if(!ReferenceEquals(_issued,operation) || Receipt is not null ||
            !operation.Change.MatchesReceipt(result))return false;
        Receipt=result;return true;
    }
}
