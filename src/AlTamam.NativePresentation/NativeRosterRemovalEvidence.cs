using System.IO;

namespace AlTamam.NativePresentation;

public interface INativeRosterRemovalPreviewBackend
{
    Task<NativeRosterRemovalPreview> PreviewRosterRemovalAsync(
        NativeIdentity actor,IReadOnlyList<NativeRosterRemovalMember> members,CancellationToken ct=default);
}
public interface INativeRosterRemovalBackend:INativeRosterRemovalPreviewBackend
{
    Task<NativeRosterRemovalReceipt> CommitRosterRemovalAsync(NativeIdentity actor,
        NativeRosterRemovalPreview preview,string reason,string operationId,CancellationToken ct=default);
}
public sealed record NativeRosterRemovalReceipt(string BusinessDate,string PreviewToken,IReadOnlyList<string> ArchivedPersonIds);
public sealed record NativeRosterRemovalMember(string PersonId,string Name,string ServiceNumberA,string SiteName,long Revision);
public sealed record NativeRosterRemovalCandidate(NativeRosterRemovalMember Member,int OrganizationalStatus,long AttendanceDays,string? Blocker);
public sealed record NativeRosterRemovalPreview(string BusinessDate,string PreviewToken,long BaseRevision,
    IReadOnlyList<NativeRosterRemovalCandidate> Candidates)
{
    public string? SessionNonce {get;init;}
    public bool CanApprove=>Candidates.Count>0 && Candidates.All(x=>x.OrganizationalStatus==1 && x.Blocker is null);
}

/// <summary>Exact reviewed selection; the original SQLCipher archive service remains authoritative.</summary>
public static class NativeRosterRemovalEvidence
{
    private static bool ValidText(string? value,int max)=>!string.IsNullOrWhiteSpace(value) &&
        value.Length<=max && !value.Any(char.IsControl);
    public static bool ValidSelection(IReadOnlyList<NativeRosterRemovalMember>? members)=>
        members is {Count:>=1 and <=100} && members.All(x=>x is not null &&
            ValidText(x.PersonId,128) && ValidText(x.Name,200) && ValidText(x.ServiceNumberA,128) &&
            ValidText(x.SiteName,150) && x.Revision is >=1 and <=long.MaxValue/100) &&
        members.Select(x=>x.PersonId).Distinct(StringComparer.Ordinal).Count()==members.Count;

    public static bool ValidReason(string? reason)=>reason is {Length:>=5 and <=1000} &&
        reason==reason.Trim() && !reason.Any(char.IsControl);

    public static NativeRosterRemovalReceipt ValidateReceipt(NativeRosterRemovalPreview preview,NativeRosterRemovalReceipt receipt)
    {
        if(receipt is null || receipt.BusinessDate!=preview.BusinessDate ||
           receipt.PreviewToken!=preview.PreviewToken || receipt.ArchivedPersonIds is null ||
           !receipt.ArchivedPersonIds.SequenceEqual(preview.Candidates.Select(x=>x.Member.PersonId),StringComparer.Ordinal))
            throw new InvalidDataException("Canonical archive receipt mismatch.");
        return receipt with{ArchivedPersonIds=Array.AsReadOnly(receipt.ArchivedPersonIds.ToArray())};
    }

    public static NativeRosterRemovalPreview ValidateAndFreeze(IReadOnlyList<NativeRosterRemovalMember> members,
        NativeRosterRemovalPreview preview,string today)
    {
        if(!ValidSelection(members) || preview is null || preview.BusinessDate!=today ||
           preview.PreviewToken is not {Length:64} || !preview.PreviewToken.All(char.IsAsciiHexDigit) ||
           preview.Candidates is null || preview.Candidates.Count!=members.Count ||
           preview.BaseRevision!=members.Sum(x=>x.Revision))
            throw new InvalidDataException("Invalid roster removal preview.");
        for(int i=0;i<members.Count;i++)
        {
            var row=preview.Candidates[i];
            if(row is null || row.Member!=members[i] || row.OrganizationalStatus is <1 or >4 ||
               row.AttendanceDays<0 || (row.Blocker is not null && !ValidText(row.Blocker,500)) ||
               (row.OrganizationalStatus!=1 && row.Blocker is null))
                throw new InvalidDataException("Roster removal candidate changed.");
        }
        return preview with{Candidates=Array.AsReadOnly(preview.Candidates.ToArray())};
    }
}
