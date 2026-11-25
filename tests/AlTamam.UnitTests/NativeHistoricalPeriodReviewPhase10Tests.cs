using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeHistoricalPeriodReviewPhase10Tests
{
    private static readonly NativeIdentity Owner = new("USR-1","admin",UserRole.GeneralAdministration,"PERIOD-NONCE");
    private static readonly NativeHistoricalPersonCandidate Person = new("P-1","أحمد","123","المركز",3);
    private static string Today => DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Direct_search_requires_admin_and_exact_candidate_then_reads_complete_period()
    {
        var reader=new Fake();
        var vm=new NativeHistoricalPeriodReview();
        Assert.False(vm.Open(reader,()=>Owner with { Role=UserRole.Supervisor }));
        Assert.True(vm.Open(reader,()=>Owner));
        vm.SearchText="123";
        await vm.SearchAsync();
        Assert.Single(vm.Candidates);
        vm.SelectedPerson=Person with { };
        Assert.Null(vm.SelectedPerson);
        vm.SelectedPerson=vm.Candidates[0];
        vm.FromText=vm.ToText=Today;
        await vm.ReadAsync();
        Assert.NotNull(vm.Evidence);
        Assert.Equal("P-1",vm.Evidence!.Request.PersonId);
        Assert.False(vm.Evidence.Days[0].HasSavedRecord);
        vm.FromText="invalid";
        Assert.Null(vm.Evidence);
        await vm.ReadAsync();
        Assert.Equal(1,reader.Reads);
        Assert.True(vm.Close());
        Assert.Empty(vm.Candidates);
    }

    [Fact]
    public async Task Direct_search_discards_late_results_after_clear_and_session_replacement()
    {
        var reader=new Fake { Pending=new(TaskCreationOptions.RunContinuationsAsynchronously) };
        NativeIdentity? actor=Owner;
        var vm=new NativeHistoricalPeriodReview();
        Assert.True(vm.Open(reader,()=>actor));
        vm.SearchText="123";
        var pending=vm.SearchAsync();
        vm.ForceClear();
        reader.Pending.SetResult([Person]);
        await pending;
        Assert.Empty(vm.Candidates);
        Assert.False(vm.IsOpen);
        reader.Pending=null;
        Assert.True(vm.Open(reader,()=>actor));
        vm.SearchText="123";
        await vm.SearchAsync();
        actor=Owner with { Role=UserRole.Supervisor };
        vm.SelectedPerson=vm.Candidates[0];
        Assert.Null(vm.SelectedPerson);
        await vm.ReadAsync();
        Assert.Equal(0,reader.Reads);
    }

    [Fact]
    public async Task Direct_period_rejects_mismatched_evidence_and_invalidates_on_search_edit()
    {
        var reader=new Fake { Forge=true };
        var vm=new NativeHistoricalPeriodReview();
        Assert.True(vm.Open(reader,()=>Owner));
        vm.SearchText="123";
        await vm.SearchAsync();
        vm.SelectedPerson=vm.Candidates[0];
        vm.FromText=vm.ToText=Today;
        await vm.ReadAsync();
        Assert.Null(vm.Evidence);
        Assert.NotEmpty(vm.Error);
        reader.Forge=false;
        await vm.ReadAsync();
        Assert.NotNull(vm.Evidence);
        vm.SearchText="456";
        Assert.Empty(vm.Candidates);
        Assert.Null(vm.SelectedPerson);
        Assert.Null(vm.Evidence);
    }

    private sealed class Fake : INativeHistoricalPeriodReader
    {
        public TaskCompletionSource<IReadOnlyList<NativeHistoricalPersonCandidate>>? Pending;
        public bool Forge;
        public int Reads;
        public Task<IReadOnlyList<NativeHistoricalPersonCandidate>> SearchHistoricalPeopleAsync(NativeIdentity actor,string term,CancellationToken ct=default)
            => Pending?.Task ?? Task.FromResult<IReadOnlyList<NativeHistoricalPersonCandidate>>([Person]);
        public Task<NativeHistoricalPeriodEvidence> ReadHistoricalPeriodAsync(NativeIdentity actor,NativeHistoricalPeriodRequest request,CancellationToken ct=default)
        {
            Reads++;
            return Task.FromResult(new NativeHistoricalPeriodEvidence(Forge ? request with { PersonId="OTHER" } : request,
                Person.Name,Person.ServiceNumberA,[new(request.From,false,"لا يوجد سجل تحضير","",0)]));
        }
    }
}
