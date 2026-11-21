using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 1: real native login MUST authorize the shell; navigation cannot
/// expose read-only placeholders or privileged commands before live SQLCipher auth.
/// Tests use a fake backend ONLY to exercise the ViewModel state machine.
/// Production uses BridgeSharedServices + the existing SQLCipher database.
/// </summary>
public sealed class NativeAuthenticationPhase1Tests
{
    [Fact]
    public void Unconfigured_host_denies_all_navigation_and_never_fakes_a_login()
    {
        var vm = new NativeShellViewModel();
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Administration));
        vm.NavigateCommand.Execute(NativeScreen.Settings);
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.IsReady);
    }

    [Fact]
    public async Task First_run_requires_explicit_matching_confirmation_before_backend_is_called()
    {
        var backend = new FakeBackend { IsProvisioned = false };
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.True(vm.IsFirstRun);
        Assert.True(vm.IsReady);
        Assert.False(await vm.LoginAsync("founder", "Abc123", "Different", "Full name"));
        Assert.Equal(0, backend.Logins);
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));

        Assert.True(await vm.LoginAsync("founder", "Abc123", "Abc123", "Full name"));
        Assert.True(backend.LastWasFirstRun);
        Assert.Equal("Full name", backend.LastFullName);
        Assert.True(vm.IsAuthenticated);
        Assert.Equal(UserRole.GeneralAdministration, vm.ActiveRole);
        Assert.Equal("الإدارة العامة", vm.RoleLabel);
    }

    [Fact]
    public async Task Existing_user_login_uses_server_identity_and_never_recreates_founder()
    {
        var backend = new FakeBackend { IsProvisioned = true,
            Principal = new NativeIdentity("USR-a", "supervisor", UserRole.Supervisor, "NATIVE_NONCE") };
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.False(vm.IsFirstRun);
        Assert.True(await vm.LoginAsync("supervisor", "Password123"));
        Assert.False(backend.LastWasFirstRun);
        Assert.Equal(UserRole.Supervisor, vm.ActiveRole);
        Assert.False(vm.CanManageAccounts);
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.MonthlyAttendance));
        Assert.False(await vm.LoginAsync("other", "secondPassword"));
        Assert.Equal(1, backend.Logins);
    }

    [Fact]
    public async Task Failed_credentials_cannot_open_any_screen()
    {
        var backend = new FakeBackend { Error = "بيانات الدخول غير صحيحة." };
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.False(await vm.LoginAsync("wrong", "bad password"));
        Assert.False(vm.IsAuthenticated);
        Assert.Contains("غير صحيحة", vm.LoginError);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
    }

    [Fact]
    public async Task Expired_or_revoked_session_closes_the_native_shell_without_refresh()
    {
        var backend = new FakeBackend();
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.True(await vm.LoginAsync("founder", "Abc123"));
        Assert.True(vm.IsAuthenticated);
        backend.SessionValid = false;
        await vm.VerifyCurrentSessionAsync();
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Dashboard));
        Assert.Equal(0, backend.Refreshes);
    }

    [Fact]
    public async Task Only_explicit_native_input_may_refresh_session_idle_deadline()
    {
        var backend = new FakeBackend();
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.True(await vm.LoginAsync("founder", "Abc123"));
        await vm.VerifyCurrentSessionAsync();
        Assert.Equal(0, backend.Refreshes);
        await vm.RecordGenuineUserActivityAsync();
        Assert.Equal(1, backend.Refreshes);
    }

    [Fact]
    public async Task Logout_revokes_server_nonce_and_denies_navigation()
    {
        var backend = new FakeBackend();
        var vm = new NativeShellViewModel();
        await vm.InitializeAuthenticationAsync(backend);
        Assert.True(await vm.LoginAsync("founder", "Abc123"));
        Assert.True(vm.NavigateCommand.CanExecute(NativeScreen.Settings));
        await vm.LogoutAsync();
        Assert.Equal("NATIVE_NONCE", backend.LastRevokedNonce);
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.NavigateCommand.CanExecute(NativeScreen.Settings));
    }

    private sealed class FakeBackend : INativeAuthenticationBackend
    {
        public bool IsProvisioned { get; set; } = true;
        public bool SessionValid { get; set; } = true;
        public int Logins { get; private set; }
        public int Refreshes { get; private set; }
        public bool LastWasFirstRun { get; private set; }
        public string LastFullName { get; private set; } = "";
        public string? LastRevokedNonce { get; private set; }
        public string? Error { get; set; }
        public NativeIdentity Principal { get; set; } = new("USR-1", "founder",
            UserRole.GeneralAdministration, "NATIVE_NONCE");

        public Task<bool> IsProvisionedAsync(CancellationToken ct = default) =>
            Task.FromResult(IsProvisioned);

        public Task<NativeLoginResult> SignInAsync(string username, string password,
            bool firstRun, string confirmation, string fullName, CancellationToken ct = default)
        {
            Logins++;
            LastWasFirstRun = firstRun;
            LastFullName = fullName;
            return Task.FromResult(Error is null
                ? new NativeLoginResult(Principal, null)
                : new NativeLoginResult(null, Error));
        }

        public Task<NativeIdentity?> ResolveSessionAsync(string nonce,
            CancellationToken ct = default) =>
            Task.FromResult<NativeIdentity?>(SessionValid ? Principal : null);

        public Task RefreshIdleDeadlineAsync(string nonce, CancellationToken ct = default)
        {
            Refreshes++;
            return Task.CompletedTask;
        }

        public Task RevokeSessionAsync(string nonce, CancellationToken ct = default)
        {
            LastRevokedNonce = nonce;
            SessionValid = false;
            return Task.CompletedTask;
        }
    }
}
