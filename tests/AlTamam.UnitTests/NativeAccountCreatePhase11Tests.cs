using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAccountCreatePhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly NativeAccountPerson Person=new("PER-1","اسم الفرد","123","المركز",1,1);
    private static NativeAccountCreateIntent Intent()=>new(Person,"newuser",UserRole.Supervisor,Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_intent_validates_role_active_person_password_and_receipt()
    {
        var intent=Intent();
        Assert.True(intent.IsValid(Admin.Role));
        Assert.True(intent.IsValid(UserRole.OrdinaryAdministration));
        Assert.False(intent.IsValid(UserRole.Supervisor));
        Assert.False((intent with { Role=UserRole.GeneralAdministration }).IsValid(Admin.Role));
        Assert.False((intent with { Role=UserRole.OrdinaryAdministration }).IsValid(UserRole.OrdinaryAdministration));
        Assert.False((intent with { Person=Person with { OrganizationalStatus=2 } }).IsValid(Admin.Role));
        Assert.False((intent with { Username="bad name" }).IsValid(Admin.Role));
        Assert.False(NativeAccountCreateIntent.ValidPassword("abc123","abc124"));
        Assert.False(NativeAccountCreateIntent.ValidPassword("123456","123456"));
        Assert.True(NativeAccountCreateIntent.ValidPassword("abc123","abc123"));
        var receipt=new NativeAccountCreateReceipt("USR-2",intent.Username,intent.Role,Person.PersonId);
        Assert.True(receipt.Matches(intent));
        Assert.False((receipt with { PersonId="PER-2" }).Matches(intent));
        Assert.False((receipt with { Role=UserRole.GeneralAdministration }).Matches(intent));
    }

    [Fact]
    public async Task Create_requires_current_search_selection_explicit_review_and_password_confirmation()
    {
        var backend=new Backend();
        var vm=new NativeAccountCreateReview();
        Assert.True(vm.Open(backend,Admin,()=>Admin));
        vm.Term="اسم";
        await vm.SearchAsync();
        vm.SelectedPerson=Person with { };
        Assert.Null(vm.SelectedPerson);
        vm.SelectedPerson=Assert.Single(vm.People);
        vm.Username="newuser";
        vm.Role=UserRole.Supervisor;
        await vm.ConfirmAsync("abc123","abc123");
        Assert.Empty(backend.Intents);
        vm.Preview();
        Assert.NotNull(vm.Prepared);
        await vm.ConfirmAsync("abc123","abc124");
        Assert.Empty(backend.Intents);
        vm.Username="newuser2";
        Assert.Null(vm.Prepared);
        vm.Preview();
        await vm.ConfirmAsync("abc123","abc123");
        Assert.Single(backend.Intents);
        Assert.NotNull(vm.Receipt);
        Assert.True(vm.Close());
    }

    [Fact]
    public async Task Create_ambiguous_commit_freezes_nonsecret_intent_and_reuses_operation_id()
    {
        var backend=new Backend { Fail=true };
        var vm=new NativeAccountCreateReview();
        Assert.True(vm.Open(backend,Admin,()=>Admin));
        vm.Term="اسم"; await vm.SearchAsync(); vm.SelectedPerson=Assert.Single(vm.People);
        vm.Username="newuser"; vm.Role=UserRole.Supervisor; vm.Preview();
        await vm.ConfirmAsync("abc123","abc123");
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.Close());
        var first=Assert.Single(backend.Intents);
        vm.Username="other"; vm.Term="آخر"; vm.Preview();
        backend.Fail=false;
        await vm.ConfirmAsync("abc123","abc123");
        Assert.Equal(first,backend.Intents[1]);
        Assert.NotNull(vm.Receipt);
        Assert.False(vm.HasUnverifiedCommit);
    }

    [Fact]
    public async Task Create_search_late_result_cannot_repaint_after_scope_change()
    {
        var backend=new Backend();
        var vm=new NativeAccountCreateReview();
        Assert.True(vm.Open(backend,Admin,()=>Admin));
        vm.Term="اسم";
        var pending=new TaskCompletionSource<NativeAccountPersonSearch>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=vm.SearchAsync();
        vm.Term="مختلف";
        pending.SetResult(new("اسم",[Person]));
        await task;
        Assert.Empty(vm.People);
        Assert.Null(vm.SelectedPerson);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task Create_rejects_supervisor_and_clears_late_receipt_after_session_teardown()
    {
        var backend=new Backend();
        var vm=new NativeAccountCreateReview();
        var supervisor=Admin with { Role=UserRole.Supervisor };
        Assert.False(vm.Open(backend,supervisor,()=>supervisor));
        NativeIdentity? actor=Admin;
        Assert.True(vm.Open(backend,Admin,()=>actor));
        vm.Term="اسم"; await vm.SearchAsync(); vm.SelectedPerson=Assert.Single(vm.People);
        vm.Username="newuser"; vm.Role=UserRole.Supervisor; vm.Preview();
        var pending=new TaskCompletionSource<NativeAccountCreateReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.CommitPending=pending.Task;
        var task=vm.ConfirmAsync("abc123","abc123");
        actor=null; vm.ForceClear();
        pending.SetResult(backend.Receipt(Assert.Single(backend.Intents)));
        await task;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Receipt);
        Assert.Null(vm.Prepared);
        Assert.Empty(vm.People);
    }

    private sealed class Backend : INativeAccountCreateBackend
    {
        public List<NativeAccountCreateIntent> Intents=[];
        public bool Fail;
        public Task<NativeAccountPersonSearch>? Pending;
        public Task<NativeAccountCreateReceipt>? CommitPending;
        public Task<NativeAccountPersonSearch> SearchAccountPeopleAsync(NativeIdentity actor,string term,CancellationToken ct=default)
            =>Pending??Task.FromResult(new NativeAccountPersonSearch(term,[Person]));
        public NativeAccountCreateReceipt Receipt(NativeAccountCreateIntent intent)=>
            new("USR-2",intent.Username,intent.Role,intent.Person.PersonId);
        public Task<NativeAccountCreateReceipt> CreateAccountAsync(NativeIdentity actor,NativeAccountCreateIntent intent,
            string password,string confirmation,CancellationToken ct=default)
        {
            Intents.Add(intent);
            if(Fail) throw new IOException("response lost");
            return CommitPending??Task.FromResult(Receipt(intent));
        }
    }
}
