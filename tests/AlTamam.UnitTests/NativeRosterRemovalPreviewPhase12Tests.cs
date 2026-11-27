using System.IO;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeRosterRemovalPreviewPhase12Tests
{
    private static NativeRosterRemovalMember[] Members()=>[
        new("P-1","أحمد","123","المركز",2),
        new("P-2","سالم","456","المركز",3)];
    private static NativeRosterRemovalPreview Preview()=>new("2026-11-28",new string('a',64),5,
        [new(Members()[0],1,7,null),new(Members()[1],1,9,null)]);
    [Fact]
    public void Exact_order_identity_version_and_aggregate_are_required()
    {
        var p=Preview();
        var verified=NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p,"2026-11-28");
        Assert.True(verified.CanApprove);
        Assert.Equal(2,verified.Candidates.Count);
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{BaseRevision=6},"2026-11-28"));
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{Candidates=p.Candidates.Reverse().ToArray()},"2026-11-28"));
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{Candidates=[p.Candidates[0],p.Candidates[1] with{Member=Members()[1] with{Revision=4}}]},"2026-11-28"));
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p,"2026-11-29"));
    }
    [Fact]
    public void Any_blocker_disables_entire_batch_and_freezes_evidence()
    {
        var p=Preview();
        var candidates=p.Candidates.ToArray();
        candidates[1]=candidates[1] with{Blocker="لا يوجد انتساب مؤهل"};
        var result=NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{Candidates=candidates},"2026-11-28");
        Assert.False(result.CanApprove);
        candidates[1]=p.Candidates[1];
        Assert.False(result.CanApprove);
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{Candidates=[p.Candidates[0],p.Candidates[1] with{OrganizationalStatus=2}]},"2026-11-28"));
    }
    [Fact]
    public void Reject_duplicate_or_unbounded_selection_and_invalid_receipt_fields()
    {
        var p=Preview();
        Assert.False(NativeRosterRemovalEvidence.ValidSelection([Members()[0],Members()[0]]));
        Assert.False(NativeRosterRemovalEvidence.ValidSelection(Enumerable.Range(1,101).Select(i=>Members()[0] with{PersonId="P-"+i}).ToArray()));
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{PreviewToken="bad"},"2026-11-28"));
        Assert.Throws<InvalidDataException>(()=>NativeRosterRemovalEvidence.ValidateAndFreeze(Members(),p with{Candidates=[p.Candidates[0],p.Candidates[1] with{AttendanceDays=-1}]},"2026-11-28"));
    }
}
