using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeRosterRemovalCommitPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static string Today=>DateTime.Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    private static NativePersonnelDisplayRow[] Rows()=>[
        new(1,"P-1","12345","","أحمد","المركز","على القوة",true,2,[]),
        new(2,"P-2","12346","","سالم","المركز","على القوة",true,3,[])];
    private static NativeRosterRemovalViewModel Open(Probe b,Func<NativeIdentity?> live,NativePersonnelDisplayRow[] rows)
    {
        var vm=new NativeRosterRemovalViewModel();
        vm.AttachBackend(b,live,row=>rows.Any(x=>ReferenceEquals(x,row)));
        vm.Open(rows);return vm;
    }

    [Fact]
    public async Task Nothing_selected_automatically_and_two_explicit_approvals_required()
    {
        var b=new Probe();var vm=Open(b,()=>Actor,Rows());
        Assert.True(vm.IsOpen);Assert.Equal(0,vm.SelectedCount);
        Assert.False(vm.CanPreview);Assert.False(vm.CanConfirm);
        vm.Options[1].IsSelected=true;vm.Reason="قرار إداري موثق";
        await vm.PreviewAsync();Assert.Equal(0,b.Commits);
        Assert.False(vm.CanConfirm);vm.IdentityReviewed=true;
        Assert.False(vm.CanConfirm);vm.AccountSuspensionAcknowledged=true;
        Assert.True(vm.CanConfirm);await vm.ConfirmAsync();
        Assert.Equal(1,b.Commits);Assert.Equal(new[]{"P-2"},vm.Receipt!.ArchivedPersonIds);
        await vm.ConfirmAsync();Assert.Equal(1,b.Commits);
    }

    [Fact]
    public async Task Changed_selection_or_reason_invalidates_review_and_blocker_denies_entire_batch()
    {
        var b=new Probe();var vm=Open(b,()=>Actor,Rows());
        vm.Options[0].IsSelected=true;vm.Reason="قرار إداري موثق";
        await vm.PreviewAsync();vm.IdentityReviewed=true;vm.AccountSuspensionAcknowledged=true;
        Assert.True(vm.CanConfirm);vm.Reason="قرار إداري آخر";
        Assert.Null(vm.Preview);Assert.False(vm.CanConfirm);
        b.Block=true;vm.Options[1].IsSelected=true;await vm.PreviewAsync();
        vm.IdentityReviewed=true;vm.AccountSuspensionAcknowledged=true;
        Assert.False(vm.CanConfirm);await vm.ConfirmAsync();Assert.Equal(0,b.Commits);
    }

    [Fact]
    public async Task Ambiguous_receipt_blocks_reopen_and_replay_in_same_session()
    {
        var rows=Rows();var b=new Probe{Forged=true};var vm=Open(b,()=>Actor,rows);
        vm.Options[0].IsSelected=true;vm.Reason="قرار إداري موثق";
        await vm.PreviewAsync();vm.IdentityReviewed=true;vm.AccountSuspensionAcknowledged=true;
        await vm.ConfirmAsync();Assert.Null(vm.Receipt);Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.CanConfirm);vm.Close();vm.Open(rows);
        Assert.False(vm.IsOpen);Assert.Equal(1,b.Commits);
    }

    [Fact]
    public async Task Supervisor_and_replaced_session_cannot_commit()
    {
        var rows=Rows();var b=new Probe();NativeIdentity? live=Actor with{Role=UserRole.Supervisor};
        var vm=Open(b,()=>live,rows);Assert.False(vm.IsOpen);
        live=Actor;vm.Open(rows);vm.Options[0].IsSelected=true;vm.Reason="قرار إداري موثق";
        await vm.PreviewAsync();vm.IdentityReviewed=true;vm.AccountSuspensionAcknowledged=true;
        live=Actor with{SessionNonce="NEW"};Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();Assert.Equal(0,b.Commits);
        vm.ForceCancel();Assert.Empty(vm.Options);Assert.Null(vm.Preview);
    }

    private sealed class Probe:INativeRosterRemovalBackend
    {
        public int Commits;public bool Block,Forged;
        public Task<NativeRosterRemovalPreview> PreviewRosterRemovalAsync(NativeIdentity actor,IReadOnlyList<NativeRosterRemovalMember> members,CancellationToken ct=default)
            =>Task.FromResult(new NativeRosterRemovalPreview(Today,new string('a',64),members.Sum(x=>x.Revision),
                members.Select(x=>new NativeRosterRemovalCandidate(x,1,5,Block?"غير مؤهل":null)).ToArray()){SessionNonce=actor.SessionNonce});
        public Task<NativeRosterRemovalReceipt> CommitRosterRemovalAsync(NativeIdentity actor,NativeRosterRemovalPreview preview,string reason,string operationId,CancellationToken ct=default)
        {
            Commits++;
            return Task.FromResult(new NativeRosterRemovalReceipt(preview.BusinessDate,preview.PreviewToken,
                Forged?new[]{"P-OTHER"}:preview.Candidates.Select(x=>x.Member.PersonId).ToArray()));
        }
    }
}
