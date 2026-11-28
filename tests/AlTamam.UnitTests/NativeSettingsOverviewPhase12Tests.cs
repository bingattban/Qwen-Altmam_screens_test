using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeSettingsOverviewPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Data(NativeIdentity actor)=>new(actor.Username,actor.Role,
        @"E:\AlTamam",true,"16:00",true,3,"2026-11-25","","light")
        {MonthlyExport=new(false,"21:00","","",""),AttendanceSettings=actor.Role==UserRole.Supervisor?null:new("08:00","20:00",null,null,false)};
    [Fact]
    public void Overview_validates_identity_schedule_and_retention_without_claiming_scheduler_running()
    {
        var data=Data(Actor);
        Assert.True(data.Matches(Actor));
        Assert.False((data with { Username="other" }).Matches(Actor));
        Assert.False((data with { Role=UserRole.Supervisor }).Matches(Actor));
        Assert.False((data with { BackupTime="25:00" }).Matches(Actor));
        Assert.False((data with { RetentionCount=1 }).Matches(Actor));
        Assert.False((data with { LastBackupDate="2026-02-30" }).Matches(Actor));
        Assert.False((data with { Theme="unknown" }).Matches(Actor));
        Assert.True((data with { DirectoryAvailable=false,LastBackupDate=null }).Matches(Actor));
    }

    [Fact]
    public void Monthly_export_saved_state_validates_time_directory_and_last_success_without_claiming_execution()
    {
        var disabled=new NativeAutomaticMonthlyExcelState(false,"21:00","","","");
        Assert.True(disabled.IsValid);
        Assert.False(disabled.Enabled);
        Assert.NotEmpty(disabled.LastSuccessLabel);
        var configured=disabled with{Enabled=true,Directory=@"Z:\Disconnected",LastSuccessDate="2026-11-25",LastFailure="تعذر الوصول إلى المجلد"};
        Assert.True(configured.IsValid);
        Assert.Equal("2026-11-25",configured.LastSuccessLabel);
        Assert.True((Data(Actor) with{MonthlyExport=configured}).Matches(Actor));
        Assert.False((Data(Actor) with{MonthlyExport=configured with{Directory=""}}).Matches(Actor));
        Assert.False((configured with{ExportTime="24:00"}).IsValid);
        Assert.False((configured with{Directory="bad\npath"}).IsValid);
        Assert.False((configured with{LastSuccessDate="2026-02-30"}).IsValid);
    }

    [Fact]
    public async Task Monthly_export_overview_rejects_invalid_evidence_and_never_shows_replaced_session_state()
    {
        NativeIdentity? actor=Actor;
        var reader=new Reader{InvalidMonthly=true};
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Null(vm.Snapshot);Assert.NotEmpty(vm.Error);
        reader.InvalidMonthly=false;
        await vm.RefreshAsync();
        Assert.True(vm.Snapshot?.HasMonthlyExport==true);
        Assert.Equal("21:00",vm.Snapshot?.MonthlyExport?.ExportTime);
        actor=Actor with{SessionNonce="NONCE-B"};
        Assert.Null(vm.Snapshot);
    }

    [Fact]
    public void Attendance_policy_evidence_validates_clocks_and_configured_status_without_inventing_a_default()
    {
        var empty=new NativeAttendanceSettingsState("08:00","20:00",null,null,false);
        Assert.True(empty.IsValid);
        Assert.Equal("لم تحدد حالة افتراضية",empty.DefaultLabel);
        var active=empty with{DefaultStatusId="STS-PRESENT",DefaultStatusName="موجود",Configured=true};
        Assert.True(active.IsValid);
        Assert.Equal("موجود",active.DefaultLabel);
        Assert.True((Data(Actor) with{AttendanceSettings=active}).Matches(Actor));
        var retired=active with{Configured=false};
        Assert.True(retired.IsValid);
        Assert.NotEqual(active.DefaultLabel,retired.DefaultLabel);
        Assert.False((active with{DueTime="24:00"}).IsValid);
        Assert.False((active with{LockTime="8:00"}).IsValid);
        Assert.False((active with{DefaultStatusId="STS-LEAVE"}).IsValid);
        Assert.False((active with{DefaultStatusId="STS-PERMISSION"}).IsValid);
        Assert.False((active with{DefaultStatusName=null}).IsValid);
        Assert.False((empty with{Configured=true}).IsValid);
        var supervisor=Actor with{Role=UserRole.Supervisor};
        Assert.False((Data(supervisor) with{AttendanceSettings=active}).Matches(supervisor));
    }

    [Fact]
    public async Task Attendance_policy_read_is_session_bound_and_rejects_malformed_evidence()
    {
        NativeIdentity? actor=Actor;
        var reader=new Reader{InvalidAttendance=true};
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Null(vm.Snapshot);Assert.NotEmpty(vm.Error);
        reader.InvalidAttendance=false;
        await vm.RefreshAsync();
        Assert.True(vm.Snapshot?.HasAttendanceSettings==true);
        Assert.Equal("20:00",vm.Snapshot?.AttendanceSettings?.LockTime);
        actor=Actor with{SessionNonce="NONCE-B"};
        Assert.Null(vm.Snapshot);
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Overview_reads_all_authorized_roles_and_clears_on_exit(UserRole role)
    {
        var actor=Actor with { Role=role };
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Equal(1,reader.Reads);
        Assert.Equal(Data(actor),vm.Snapshot);
        vm.Clear();
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Overview_denies_missing_session_and_rejects_wrong_identity()
    {
        NativeIdentity? actor=null;
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Equal(0,reader.Reads);
        actor=Actor; reader.Forged=true;
        await vm.RefreshAsync();
        Assert.Null(vm.Snapshot);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_discards_late_result_after_navigation_or_session_change(bool changeSession)
    {
        NativeIdentity? actor=Actor;
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        var pending=new TaskCompletionSource<NativeSettingsOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.RefreshAsync();
        Assert.True(vm.IsLoading);
        if(changeSession) actor=Actor with { SessionNonce="NONCE-B" }; else vm.Clear();
        pending.SetResult(Data(Actor));
        await task;
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsLoading);
    }

    private sealed class Reader:INativeSettingsOverviewReader
    {
        public int Reads;
        public bool Forged,InvalidMonthly,InvalidAttendance;
        public Task<NativeSettingsOverview>? Pending;
        public Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            Reads++;
            var data=Data(actor);
            if(Forged)data=data with{Username="other"};
            if(InvalidMonthly)data=data with{MonthlyExport=new(true,"25:00","","","")};
            if(InvalidAttendance)data=data with{AttendanceSettings=new("08:00","25:00",null,null,false)};
            return Pending??Task.FromResult(data);
        }
    }
}
