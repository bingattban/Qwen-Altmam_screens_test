using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeWpfNavigationTests
{
    [Fact]
    public void Main_navigation_includes_all_requested_native_sections()
    {
        Assert.Equal(8, NativeSections.All.Count);
        Assert.Equal(
            new[] { "الرئيسية", "التمام اليومي", "التمام الشهري", "الإدارة", "التقارير", "سجل العمليات", "حسابات المستخدمين", "الإعدادات" },
            NativeSections.All.Select(section => section.Title).ToArray());
        Assert.Equal(8, NativeSections.All.Select(section => section.Screen).Distinct().Count());
    }

    [Fact]
    public async Task Navigation_changes_selected_page_only_after_real_backend_session_verification()
    {
        var vm = new NativeShellViewModel();
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.MonthlyAttendance));
        vm.NavigateCommand.Execute(NativeScreen.MonthlyAttendance);
        Assert.Equal(NativeScreen.Dashboard, vm.CurrentPage.Screen);

        await vm.InitializeAuthenticationAsync(new FixedNativeBackend());
        Assert.True(await vm.LoginAsync("owner", "Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.MonthlyAttendance);
        Assert.Equal(NativeScreen.MonthlyAttendance, vm.CurrentPage.Screen);
        vm.NavigateCommand.Execute(NativeScreen.Reports);
        Assert.Equal(NativeScreen.Reports, vm.CurrentPage.Screen);
        await vm.LogoutAsync();
        Assert.Equal(NativeScreen.Dashboard, vm.CurrentPage.Screen);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.MonthlyAttendance));
    }

    [Fact]
    public void Unknown_screen_is_rejected_and_does_not_change_selection()
    {
        var vm = new NativeShellViewModel();
        Assert.False(vm.NavigateCommand.CanExecute((NativeScreen)999));
        vm.NavigateCommand.Execute((NativeScreen)999);
        Assert.Equal(NativeScreen.Dashboard, vm.CurrentPage.Screen);
    }

    [Fact]
    public void Unauthenticated_pages_never_claim_access_to_protected_personnel_data()
    {
        var vm = new NativeShellViewModel();
        // All eight native sections, including the newly connected personnel
        // manager, must fail closed before an authenticated SQLCipher session.
        Assert.All(vm.Pages, page => Assert.False(page.IsDataConnected));
        Assert.Empty(vm.Administration.Rows);
        Assert.False(vm.Administration.IsProfileOpen);
        Assert.Null(vm.Administration.SelectedProfile);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Administration));

        // Only the two genuinely unconverted sections may continue to
        // display the exact disconnected placeholder text.
        Assert.All(vm.Pages.Where(page =>
            page.Screen is NativeScreen.Reports or NativeScreen.Settings),
            page => Assert.Contains("لم يتم", page.StatusMessage));
        Assert.Contains("لم تتصل", vm.Administration.StatusMessage);
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Audit_navigation_requires_live_reader_and_clears_on_leave_and_logout(UserRole role)
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.AuditLog));
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend(role));
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.AuditLog));
        vm.NavigateCommand.Execute(NativeScreen.AuditLog);
        Assert.Same(vm.Audit,vm.CurrentPage);
        Assert.Single(vm.Audit.Rows);
        Assert.Equal(role==UserRole.GeneralAdministration,vm.Audit.CanIncludeTombstoned);
        vm.NavigateCommand.Execute(NativeScreen.Dashboard);
        Assert.Empty(vm.Audit.Rows);
        vm.NavigateCommand.Execute(NativeScreen.AuditLog);
        Assert.Single(vm.Audit.Rows);
        await vm.LogoutAsync();
        Assert.Empty(vm.Audit.Rows);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.AuditLog));
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration,true)]
    [InlineData(UserRole.OrdinaryAdministration,true)]
    [InlineData(UserRole.Supervisor,false)]
    public async Task Accounts_navigation_respects_roles_and_scrubs_on_leave_and_logout(UserRole role,bool allowed)
    {
        var vm=new NativeShellViewModel();
        vm.AttachDashboardReader(new AuditReader());
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Accounts));
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend(role));
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        Assert.Equal(allowed,vm.NavigateCommand.CanExecute(NativeScreen.Accounts));
        vm.NavigateCommand.Execute(NativeScreen.Accounts);
        if(allowed)
        {
            Assert.Same(vm.Accounts,vm.CurrentPage);
            Assert.Single(vm.Accounts.Rows);
            vm.NavigateCommand.Execute(NativeScreen.Dashboard);
            Assert.Empty(vm.Accounts.Rows);
            vm.NavigateCommand.Execute(NativeScreen.Accounts);
            Assert.Single(vm.Accounts.Rows);
        }
        else
        {
            Assert.Equal(NativeScreen.Dashboard,vm.CurrentPage.Screen);
            Assert.Empty(vm.Accounts.Rows);
        }
        await vm.LogoutAsync();
        Assert.Empty(vm.Accounts.Rows);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Accounts));
    }

    [Fact]
    public async Task Administration_review_blocks_all_other_navigation_and_same_page_does_not_reload()
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend());
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.Administration);
        Assert.Same(vm.Administration,vm.CurrentPage);
        Assert.Equal(1,reader.PersonnelReads);
        var person=Assert.Single(vm.Administration.Rows);
        await vm.Administration.OpenProfileAsync(person);
        Assert.True(vm.Administration.IsProfileOpen);
        foreach(var section in NativeSections.All.Where(s=>s.Screen!=NativeScreen.Administration))
        {
            Assert.False(vm.NavigateCommand.CanExecute(section.Screen));
            vm.NavigateCommand.Execute(section.Screen);
            Assert.Same(vm.Administration,vm.CurrentPage);
            Assert.True(vm.Administration.IsProfileOpen);
        }
        vm.NavigateCommand.Execute(NativeScreen.Administration);
        Assert.Equal(1,reader.PersonnelReads);
        Assert.True(vm.Administration.IsProfileOpen);
        vm.Administration.CloseProfile();
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        vm.NavigateCommand.Execute(NativeScreen.Dashboard);
        Assert.Same(vm.Dashboard,vm.CurrentPage);
    }

    [Fact]
    public async Task Repeated_click_on_current_settings_does_not_start_second_read()
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend());
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.Settings);
        Assert.Equal(1,reader.SettingsReads);
        vm.NavigateCommand.Execute(NativeScreen.Settings);
        Assert.Equal(1,reader.SettingsReads);
        vm.NavigateCommand.Execute(NativeScreen.Dashboard);
        vm.NavigateCommand.Execute(NativeScreen.Settings);
        Assert.Equal(2,reader.SettingsReads);
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration,true)]
    [InlineData(UserRole.OrdinaryAdministration,true)]
    [InlineData(UserRole.Supervisor,false)]
    public async Task Report_entries_reuse_existing_routes_and_never_elevate_roles(UserRole role,bool manager)
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        Assert.False(vm.Reports.OpenPersonnelReportsCommand.CanExecute(null));
        Assert.False(vm.Reports.OpenMonthlyReportsCommand.CanExecute(null));
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend(role));
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        Assert.False(vm.Reports.OpenPersonnelReportsCommand.CanExecute(null));
        vm.NavigateCommand.Execute(NativeScreen.Reports);
        Assert.Same(vm.Reports,vm.CurrentPage);
        Assert.Equal(manager,vm.Reports.OpenPersonnelReportsCommand.CanExecute(null));
        Assert.True(vm.Reports.OpenMonthlyReportsCommand.CanExecute(null));
        vm.Reports.OpenPersonnelReportsCommand.Execute(null);
        if(manager)
        {
            Assert.Same(vm.Administration,vm.CurrentPage);
            Assert.Equal(1,reader.PersonnelReads);
            Assert.False(vm.Administration.GroupReport.IsOpen);
            Assert.False(vm.Administration.ReportHistory.IsOpen);
            vm.NavigateCommand.Execute(NativeScreen.Reports);
        }
        else
        {
            Assert.Same(vm.Reports,vm.CurrentPage);
            Assert.Equal(0,reader.PersonnelReads);
        }
        vm.Reports.OpenMonthlyReportsCommand.Execute(null);
        Assert.Same(vm.MonthlyAttendance,vm.CurrentPage);
        Assert.False(vm.MonthlyAttendance.Excel.IsOpen);
        Assert.Equal(0,reader.ReportReads);
        Assert.Equal(0,reader.Exports);
        await vm.LogoutAsync();
        Assert.False(vm.Reports.OpenPersonnelReportsCommand.CanExecute(null));
        Assert.False(vm.Reports.OpenMonthlyReportsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Roster_export_entry_reuses_monthly_scope_without_automatic_preview_or_export(UserRole role)
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend(role));
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        vm.NavigateCommand.Execute(NativeScreen.Reports);
        Assert.True(vm.OpenRosterExportCommand.CanExecute(null));
        vm.OpenRosterExportCommand.Execute(null);
        Assert.Same(vm.MonthlyAttendance,vm.CurrentPage);
        Assert.True(vm.MonthlyAttendance.IsRosterExportEntry);
        Assert.False(vm.MonthlyAttendance.Excel.IsOpen);
        Assert.Equal(0,reader.Exports);
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        vm.NavigateCommand.Execute(NativeScreen.Dashboard);
        Assert.False(vm.MonthlyAttendance.IsRosterExportEntry);
        vm.NavigateCommand.Execute(NativeScreen.MonthlyAttendance);
        Assert.False(vm.MonthlyAttendance.IsRosterExportEntry);
        await vm.LogoutAsync();
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        Assert.False(vm.MonthlyAttendance.IsRosterExportEntry);
    }

    [Fact]
    public async Task Administration_export_entry_respects_pending_review_and_logout()
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend());
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.Administration);
        Assert.True(vm.OpenRosterExportCommand.CanExecute(null));
        await vm.Administration.OpenProfileAsync(Assert.Single(vm.Administration.Rows));
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        vm.OpenRosterExportCommand.Execute(null);
        Assert.Same(vm.Administration,vm.CurrentPage);
        vm.Administration.CloseProfile();
        Assert.True(vm.OpenRosterExportCommand.CanExecute(null));
        vm.OpenRosterExportCommand.Execute(null);
        Assert.True(vm.MonthlyAttendance.IsRosterExportEntry);
        Assert.False(vm.MonthlyAttendance.Excel.IsOpen);
        Assert.Equal(0,reader.Exports);
        await vm.LogoutAsync();
        Assert.False(vm.MonthlyAttendance.IsRosterExportEntry);
    }

    [Fact]
    public async Task Reports_without_backend_never_advertise_working_actions()
    {
        var vm=new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend());
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.Reports);
        Assert.False(vm.Reports.OpenPersonnelReportsCommand.CanExecute(null));
        Assert.False(vm.Reports.OpenMonthlyReportsCommand.CanExecute(null));
        Assert.False(vm.OpenRosterExportCommand.CanExecute(null));
        vm.OpenRosterExportCommand.Execute(null);
        vm.Reports.OpenMonthlyReportsCommand.Execute(null);
        Assert.Same(vm.Reports,vm.CurrentPage);
    }

    [Fact]
    public async Task Draft_catalogue_freezes_daily_scope_and_navigation_and_logout_scrubs_rows()
    {
        var vm=new NativeShellViewModel();
        var reader=new AuditReader();
        vm.AttachDashboardReader(reader);
        Assert.False(vm.DailyAttendance.OpenDraftsCommand.CanExecute(null));
        await vm.InitializeAuthenticationAsync(new FixedNativeBackend(UserRole.Supervisor));
        Assert.True(await vm.LoginAsync("owner","Abc123"));
        vm.NavigateCommand.Execute(NativeScreen.DailyAttendance);
        Assert.True(vm.DailyAttendance.OpenDraftsCommand.CanExecute(null));
        await vm.DailyAttendance.Drafts.OpenAsync();
        Assert.Single(vm.DailyAttendance.Drafts.Rows);
        Assert.False(vm.DailyAttendance.CanChangeRosterScope);
        Assert.False(vm.DailyAttendance.RefreshCommand.CanExecute(null));
        Assert.False(vm.DailyAttendance.SetSelectedRows([]));
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        vm.NavigateCommand.Execute(NativeScreen.Dashboard);
        Assert.Same(vm.DailyAttendance,vm.CurrentPage);
        vm.DailyAttendance.Drafts.CloseCommand.Execute(null);
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        await vm.DailyAttendance.Drafts.OpenAsync();
        Assert.Equal(2,reader.DraftReads);
        await vm.LogoutAsync();
        Assert.Empty(vm.DailyAttendance.Drafts.Rows);
        Assert.False(vm.DailyAttendance.Drafts.IsOpen);
    }

    private sealed class AuditReader : INativeDashboardReader, INativeAuditReader, INativeAccountReader,
        INativePersonnelDirectoryReader, INativeSettingsOverviewReader, INativePersonnelReportReader,
        INativePersonnelGroupReportReader, INativeMonthlyAttendanceReader, INativeMonthlyExcelExportBackend, INativeAttendanceDraftReader
    {
        public int PersonnelReads,SettingsReads,ReportReads,Exports,DraftReads;
        public Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            DraftReads++;
            return Task.FromResult<IReadOnlyList<NativeAttendanceDraft>>([new("DRF-"+new string('a',32),
                new DateOnly(2026,10,23),"SITE-A","STS-PRESENT",
                new DateTimeOffset(2026,10,23,10,0,0,TimeSpan.Zero),["P-1"])]);
        }
        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(NativeIdentity actor,NativeIndividualReportPages pages,CancellationToken ct=default)
        {
            ReportReads++;
            throw new InvalidOperationException("Landing must not query an unselected person.");
        }
        public Task<NativePersonnelGroupReportSnapshot> ReadGroupAsync(NativeIdentity actor,NativePersonnelGroupReportRequest request,CancellationToken ct=default)
        {
            ReportReads++;
            throw new InvalidOperationException("Landing must not query an unselected group.");
        }
        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult(new NativeMonthlyCatalogue([DateTime.Now.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture)],[]));
        public Task<NativeMonthlySnapshot> ReadMonthAsync(NativeIdentity actor,NativeMonthlyFilter filter,CancellationToken ct=default)
        {
            int days=DateTime.DaysInMonth(int.Parse(filter.Period[..4]),int.Parse(filter.Period[5..]));
            return Task.FromResult(new NativeMonthlySnapshot(filter.Period,filter.SiteId,filter.Page,days,0,false,[],[]));
        }
        public Task<NativeMonthlyExcelPreview> PreviewExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
        {
            Exports++;
            throw new InvalidOperationException("Landing must not preview without explicit intent.");
        }
        public Task<NativeMonthlyExcelReceipt> ExportExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
        {
            Exports++;
            throw new InvalidOperationException("Landing must never export.");
        }
        public Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            SettingsReads++;
            return Task.FromResult(new NativeSettingsOverview(actor.Username,actor.Role,
                @"E:\AlTamam",true,"16:00",true,3,"2026-11-25","","light"));
        }
        public Task<NativePersonnelCatalogue> ReadPersonnelCatalogueAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult(new NativePersonnelCatalogue([new NativeDashboardSite("SITE-A","المركز")]));
        public Task<NativePersonnelSnapshot> ReadPageAsync(NativeIdentity actor,NativePersonnelFilter filter,CancellationToken ct=default)
        {
            PersonnelReads++;
            return Task.FromResult(new NativePersonnelSnapshot(filter.SiteId,filter.SearchTerm,
                filter.OrganizationalStatus,filter.Page,1,false,
                [new NativeDashboardColumn("rank","الرتبة")],
                [new NativePersonnelRow("P-1","123","أحمد","المركز",1,true,1,["رقيب"]) {RowNumber=1}]));
        }
        public Task<NativePersonnelProfile> ReadProfileAsync(NativeIdentity actor,string personId,CancellationToken ct=default)
            =>Task.FromResult(new NativePersonnelProfile(personId,"أحمد","123",1,true,1,"2026-01-01",
                [new NativePersonnelIdentityHistory("أحمد القديم","123","2026-01-01","","تصحيح الاسم","2026-01-02T00:00:00Z")]));
        public Task<NativeDashboardSnapshot> ReadAsync(NativeIdentity principal,NativeDashboardFilter filter,CancellationToken ct=default)
            => Task.FromResult(new NativeDashboardSnapshot(filter.BusinessDate,filter.SiteId,0,0,0,[],[],[],[],0,filter.Page,false));
        public Task<NativeAccountPage> ReadAccountsAsync(NativeIdentity actor,NativeAccountRequest request,CancellationToken ct=default)
            => Task.FromResult(new NativeAccountPage(request,1,false,
                [new(1,"USR-2","supervisor",UserRole.Supervisor,true,1,"اسم المشرف","123","PER-2","owner",
                     "2026-01-01T10:00:00Z",true)]));
        public Task<NativeAuditSnapshot> ReadAuditAsync(NativeIdentity actor,NativeAuditFilter filter,CancellationToken ct=default)
            => Task.FromResult(new NativeAuditSnapshot(filter,1,false,
                [new("AUD-1",1,"SITE_CREATE","إضافة موقع","admin","المركز","الوصف","الأصل","","2026-01-01",
                    "2026-01-01T10:00:00Z","تم الاعتماد",new string('a',64),false,0)]));
    }

    private sealed class FixedNativeBackend(UserRole role=UserRole.GeneralAdministration) : INativeAuthenticationBackend
    {
        private bool _loggedOut;
        private readonly NativeIdentity _identity = new("USR-1", "owner",
            role, "REAL_SESSION_TOKEN");

        public Task<bool> IsProvisionedAsync(CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<NativeLoginResult> SignInAsync(string username, string password,
            bool firstRun, string confirmation, string fullName, CancellationToken ct = default) =>
            Task.FromResult(new NativeLoginResult(_identity, null));
        public Task<NativeIdentity?> ResolveSessionAsync(string nonce, CancellationToken ct = default) =>
            Task.FromResult<NativeIdentity?>(_loggedOut ? null : _identity);
        public Task RefreshIdleDeadlineAsync(string nonce, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task RevokeSessionAsync(string nonce, CancellationToken ct = default)
        {
            _loggedOut = true;
            return Task.CompletedTask;
        }
    }
}
