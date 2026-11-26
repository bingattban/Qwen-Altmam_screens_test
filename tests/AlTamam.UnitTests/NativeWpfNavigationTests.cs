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

    private sealed class AuditReader : INativeDashboardReader, INativeAuditReader, INativeAccountReader
    {
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
