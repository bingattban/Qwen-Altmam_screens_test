using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeRosterLayoutPhase12Tests
{
    private static readonly NativeIdentity Manager=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeRosterColumn Column(string id="FLD-1",bool system=false,int order=5)
        =>new(id,"key-"+id,"عنوان العمود",1,order,system,true,true,0,new string('A',64));
    private static NativeRosterColumn[] Rows()=>[
        new("FLD-SYS-ROWNUM","sys_row_number","الرقم",2,1,true,true,true,0,new string('A',64)),
        new("FLD-SYS-SVC-NUM","sys_service_number_a","الرقم (ع)",1,2,true,true,true,0,new string('B',64)),
        new("FLD-SYS-NAME","sys_person_name","الاسم",1,3,true,true,true,0,new string('C',64)),
        new("FLD-SYS-SITE","sys_site_name","الموقع",1,4,true,true,true,0,new string('D',64)),
        Column()];
    [Fact]
    public void Metadata_denies_duplicate_identity_bad_snapshot_and_unprotected_system_positions()
    {
        Assert.True(NativeRosterColumn.ValidCatalogue(Rows()));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows(),Column()]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(4),Column() with {Snapshot="bad"}]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(4),Column(order:4)]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(3),Column()]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(2),Rows()[2] with { Visible=false },..Rows().Skip(3)]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(4),Column() with {TechnicalKey="sys_person_name"}]));
        Assert.True(NativeRosterColumn.ValidCatalogue([..Rows(),Column("FLD-RETIRED") with {Active=false,Visible=false,StoredValueCount=8}]));
        Assert.False(NativeRosterColumn.ValidCatalogue([..Rows().Take(4),Column() with {StoredValueCount=-1}]));
    }
    [Fact]
    public async Task Catalogue_is_read_only_and_denies_supervisor_or_missing_session()
    {
        NativeIdentity? actor=null;
        var reader=new Reader();
        var vm=new NativeRosterLayoutViewModel();
        vm.Attach(reader,()=>actor);
        await vm.OpenAsync();
        actor=Manager with {Role=UserRole.Supervisor};
        await vm.OpenAsync();
        Assert.Equal(0,reader.Reads);
        actor=Manager;
        await vm.OpenAsync();
        Assert.Equal(1,reader.Reads);
        Assert.Equal(5,vm.Rows.Count);
        Assert.True(vm.IsOpen);
        vm.Clear();
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsOpen);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Late_catalogue_is_discarded_after_close_or_session_change(bool swap)
    {
        NativeIdentity? actor=Manager;
        var reader=new Reader {Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new NativeRosterLayoutViewModel();
        vm.Attach(reader,()=>actor);
        var task=vm.OpenAsync();
        Assert.True(vm.IsBusy);
        if(swap) actor=Manager with {SessionNonce="NONCE-B"}; else vm.Clear();
        reader.Pending.SetResult(Rows());
        await task;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsOpen);
        Assert.False(vm.IsBusy);
    }
    private sealed class Reader:INativeRosterLayoutReader
    {
        public int Reads;
        public TaskCompletionSource<IReadOnlyList<NativeRosterColumn>>? Pending;
        public Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            Reads++;
            return Pending?.Task??Task.FromResult<IReadOnlyList<NativeRosterColumn>>(Rows());
        }
    }
}
