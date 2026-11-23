using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeWpfNavigationTests
{
    [Fact]
    public void Main_navigation_includes_all_requested_native_sections()
    {
        Assert.Equal(6, NativeSections.All.Count);
        Assert.Equal(
            new[] { "الرئيسية", "التمام اليومي", "التمام الشهري", "الإدارة", "التقارير", "الإعدادات" },
            NativeSections.All.Select(section => section.Title).ToArray());
        Assert.Equal(6, NativeSections.All.Select(section => section.Screen).Distinct().Count());
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
        // All six native sections, including the newly connected personnel
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

    private sealed class FixedNativeBackend : INativeAuthenticationBackend
    {
        private bool _loggedOut;
        private readonly NativeIdentity _identity = new("USR-1", "owner",
            UserRole.GeneralAdministration, "REAL_SESSION_TOKEN");

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
