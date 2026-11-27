using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeRosterColumnMutationPhase12Tests
{
    private static readonly NativeRosterColumn Existing=new("FLD-EXAMPLE","custom_example","ملاحظة",1,5,false,true,true,7,new string('A',64));
    private static readonly NativeIdentity Manager=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeRosterColumn[] Rows()=>
    [
        new("FLD-SYS-ROWNUM","sys_row_number","الرقم",2,1,true,true,true,0,new string('A',64)),
        new("FLD-SYS-SVC-NUM","sys_service_number_a","الرقم (ع)",1,2,true,true,true,0,new string('B',64)),
        new("FLD-SYS-NAME","sys_person_name","الاسم",1,3,true,true,true,0,new string('C',64)),
        new("FLD-SYS-SITE","sys_site_name","الموقع",1,4,true,true,true,0,new string('D',64)),
        Existing
    ];
    private static NativeRosterColumnChange Edit(NativeRosterColumn? row=null)=>
        new(NativeRosterColumnOperation.Update,row??Existing,"ملاحظة جديدة",1,5,false);
    [Fact]
    public void Exact_current_metadata_and_protected_columns_are_required()
    {
        var rows=Rows();
        Assert.True(Edit().CanApply(rows));
        Assert.False(Edit(rows[2]).CanApply(rows));
        Assert.False(Edit(Existing with {Snapshot=new string('B',64)}).CanApply(rows));
        Assert.False((Edit() with {FieldType=2}).CanApply(rows));
        Assert.False((Edit() with {DisplayOrder=6}).CanApply(rows));
        Assert.False((Edit() with {Caption=Existing.Caption,Visible=true}).CanApply(rows));
        Assert.False((Edit() with {Caption="الاسم"}).CanApply(rows));
        Assert.False(Edit().CanApply([..rows.Take(4),Existing with {StoredValueCount=8}]));
    }
    [Fact]
    public void Native_caption_review_matches_canonical_arabic_normalization()
    {
        var create=new NativeRosterColumnChange(NativeRosterColumnOperation.Create,null,"ملاحظة",1,0,true);
        Assert.False(create.CanApply(Rows()));
        Assert.False((create with {Caption="مُلاحظة"}).CanApply(Rows()));
        Assert.False((create with {Caption="ــ"}).CanApply(Rows()));
        Assert.True((create with {Caption="المؤهل العلمي"}).CanApply(Rows()));
        Assert.True((Edit() with {Caption="مُلاحظة"}).CanApply(Rows()));
    }

    [Fact]
    public void Create_and_retire_have_distinct_rules_and_cannot_change_saved_values()
    {
        var create=new NativeRosterColumnChange(NativeRosterColumnOperation.Create,null,"رقم جديد",2,0,true);
        Assert.True(create.CanApply(Rows()));
        Assert.False((create with {Caption="ملاحظة"}).CanApply(Rows()));
        Assert.False((create with {Visible=false}).CanApply(Rows()));
        var retire=new NativeRosterColumnChange(NativeRosterColumnOperation.Deactivate,Existing,Existing.Caption,1,5,false);
        Assert.True(retire.CanApply(Rows()));
        Assert.False((retire with {FieldType=2}).CanApply(Rows()));
        Assert.False((retire with {Visible=true}).CanApply(Rows()));
        var retired=Existing with {Active=false,Visible=false,Snapshot=new string('B',64)};
        Assert.True(retire.MatchesReceipt(retired));
        Assert.False(retire.MatchesReceipt(retired with {StoredValueCount=0}));
        Assert.False(retire.MatchesReceipt(retired with {TechnicalKey="changed"}));
        Assert.False(retire.MatchesReceipt(retired with {Active=true}));
    }
    [Fact]
    public void Commit_requires_review_and_does_not_automatically_retry_uncertain_result()
    {
        var review=new NativeRosterColumnMutationReview();
        review.Begin(Manager,Edit(),Rows());
        Assert.False(review.CanConfirm(Manager,Rows()));
        Assert.True(review.Review(Manager,Rows()));
        Assert.False(review.CanConfirm(Manager with {SessionNonce="OTHER"},Rows()));
        var operation=review.Take(Manager,Rows());
        Assert.NotNull(operation);
        Assert.True(Guid.TryParseExact(operation.OperationId,"N",out _));
        Assert.False(review.CanConfirm(Manager,Rows()));
        Assert.Null(review.Take(Manager,Rows()));
        Assert.True(review.HasUnverifiedCommit);
        Assert.False(review.Accept(operation,Existing));
        Assert.True(review.HasUnverifiedCommit);
        Assert.True(review.Accept(operation,Existing with {Caption="ملاحظة جديدة",Visible=false,Snapshot=new string('B',64)}));
        Assert.False(review.HasUnverifiedCommit);
    }
    [Fact]
    public async Task ViewModel_requires_fresh_review_and_clears_saved_metadata_on_close()
    {
        var backend=new Backend();
        var vm=new NativeRosterLayoutViewModel();
        vm.Attach(backend,()=>Manager);
        await vm.OpenAsync();
        Assert.False(vm.EditCommand.CanExecute(vm.Rows[2]));
        Assert.False(vm.DeactivateCommand.CanExecute(vm.Rows[2]));
        vm.EditCommand.Execute(vm.Rows[4]);
        Assert.False(vm.CanEditType);
        vm.DraftCaption="ملاحظة جديدة";
        vm.ReviewCommand.Execute(null);
        Assert.True(vm.CanConfirm);
        vm.DraftVisible=false;
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0,backend.Commits);
        vm.ReviewCommand.Execute(null);
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Commits);
        Assert.False(vm.HasUnverifiedCommit);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Commits);
        bool closed=false;
        vm.CommittedAndClosed+=()=>closed=true;
        vm.Close();
        Assert.True(closed);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Lost_commit_reply_blocks_repeat_mutation_for_same_session()
    {
        var backend=new Backend{Lose=true};
        var vm=new NativeRosterLayoutViewModel();
        vm.Attach(backend,()=>Manager);
        await vm.OpenAsync();
        vm.DeactivateCommand.Execute(vm.Rows[4]);
        vm.ReviewCommand.Execute(null);
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.CancelDraftCommand.CanExecute(null));
        vm.Close();
        await vm.OpenAsync();
        Assert.False(vm.CreateCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(vm.Rows[4]));
        Assert.Equal(1,backend.Commits);
    }

    private sealed class Backend:INativeRosterLayoutReader,INativeRosterColumnMutationBackend
    {
        public int Commits;
        public bool Lose;
        public Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult<IReadOnlyList<NativeRosterColumn>>(Rows());
        public Task<NativeRosterColumn> CommitRosterColumnAsync(NativeRosterColumnCommit operation,CancellationToken ct=default)
        {
            Commits++;
            if(Lose)throw new InvalidOperationException("Lost receipt.");
            return Task.FromResult(Existing with {Caption=operation.Change.Caption,Visible=operation.Change.Visible,
                Snapshot=new string('B',64)});
        }
    }

}
