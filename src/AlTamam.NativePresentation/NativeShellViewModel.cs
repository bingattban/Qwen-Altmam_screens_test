using System.ComponentModel;
using System.Windows.Input;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Real native WPF shell authentication state. Credentials are supplied once
/// by a PasswordBox and are NEVER stored in this ViewModel. All authority
/// comes from the existing SQLCipher session resolver, not from navigation
/// flags, UI-supplied roles, or a local "logged-in" preference.
/// </summary>
public sealed class NativeShellViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyDictionary<NativeScreen, NativePageViewModel> _pages;
    private readonly NativeCommand _navigateCommand;
    private readonly NativeCommand _logoutCommand;
    private NativePageViewModel _currentPage;
    private INativeAuthenticationBackend? _backend;
    private NativeIdentity? _active;
    private bool _isReady;
    private bool _isBusy;
    private bool _isFirstRun;
    private string _loginError = "";
    private DateTimeOffset _lastNativeInputRefresh = DateTimeOffset.MinValue;

    public NativeShellViewModel()
    {
        Dashboard = new DashboardPageViewModel();
        DailyAttendance = new DailyAttendancePageViewModel();
        MonthlyAttendance = new MonthlyAttendancePageViewModel();
        Administration = new AdministrationPageViewModel();
        Pages = new NativePageViewModel[]
        {
            Dashboard,
            DailyAttendance,
            MonthlyAttendance,
            Administration,
            new ReportsPageViewModel(),
            new SettingsPageViewModel()
        };
        _pages = Pages.ToDictionary(page => page.Screen);
        _currentPage = _pages[NativeScreen.Dashboard];
        _navigateCommand = new NativeCommand(
            parameter =>
            {
                if (parameter is NativeScreen screen && CanNavigate(screen))
                {
                    if (screen != NativeScreen.Dashboard)
                        Dashboard.AutoRegister.Close();
                    if (screen != NativeScreen.DailyAttendance)
                        DailyAttendance.Review.Cancel();
                    if (screen != NativeScreen.MonthlyAttendance)
                    {
                        MonthlyAttendance.CloseCellDetails();
                        MonthlyAttendance.Excel.Close();
                    }
                    if (screen != NativeScreen.Administration)
                    {
                        Administration.CloseProfile();
                        Administration.Mutation.ForceCancel();
                        Administration.Movement.ForceCancel();
                        Administration.BulkMovement.ForceCancel();
                        Administration.BulkExternalMovement.ForceCancel();
                        Administration.ExcelImport.ForceCancel();
                        Administration.ImportHistory.Close();
                        Administration.ReportHistory.Close();
                        Administration.GroupReport.Close();
                        Administration.Separation.ForceCancel();
                        Administration.SeparationArchive.Close();
                        Administration.Return.ForceCancel();
                        Administration.SiteCatalog.ForceCancel();
                        Administration.StatusCatalog.ForceCancel();
                         Administration.Archive.Close();
                    }
                    CurrentPage = _pages[screen];
                    if (screen == NativeScreen.DailyAttendance)
                        _ = DailyAttendance.RefreshAsync();
                    if (screen == NativeScreen.MonthlyAttendance)
                        _ = MonthlyAttendance.RefreshAsync();
                    if (screen == NativeScreen.Administration)
                        _ = Administration.RefreshAsync();
                }
            },
            parameter => parameter is NativeScreen screen && CanNavigate(screen));
        MonthlyAttendance.Correction.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        MonthlyAttendance.RangeCorrection.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        _logoutCommand = new NativeCommand(
            _ => _ = LogoutAsync(),
            _ => IsAuthenticated && !IsBusy);
    }

    public DashboardPageViewModel Dashboard { get; }
    public DailyAttendancePageViewModel DailyAttendance { get; }
    public MonthlyAttendancePageViewModel MonthlyAttendance { get; }
    public AdministrationPageViewModel Administration { get; }
    public IReadOnlyList<NativePageViewModel> Pages { get; }

    // Called once by the real desktop host after SQLCipher initialization.
    // The delegate only yields the current server-verified native principal.
    public void AttachDashboardReader(INativeDashboardReader reader)
    {
        Dashboard.AttachReader(reader, () => _active);
        DailyAttendance.AttachReader(reader, () => _active);
        if (reader is INativeMonthlyAttendanceReader archiveReader)
            MonthlyAttendance.AttachReader(archiveReader, () => _active);
        if (reader is INativePersonnelDirectoryReader personnelReader)
            Administration.AttachReader(personnelReader, () => _active);
    }
    public ICommand NavigateCommand => _navigateCommand;
    public ICommand LogoutCommand => _logoutCommand;
    public NativePageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (ReferenceEquals(_currentPage, value)) return;
            _currentPage = value;
            Changed(nameof(CurrentPage));
        }
    }

    public bool IsAuthenticated => _active is not null;
    public bool IsReady => _isReady;
    public bool IsBusy => _isBusy;
    public bool IsFirstRun => _isFirstRun;
    public string LoginError => _loginError;
    public string Username => _active?.Username ?? "";
    public UserRole? ActiveRole => _active?.Role;
    public string RoleLabel => _active?.Role.ToArabicLabel() ?? "لم يتم تسجيل الدخول";
    public bool CanManageAccounts => _active is { } person &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(person.Role);

    private bool CanNavigate(NativeScreen screen)
        => IsAuthenticated && _pages.ContainsKey(screen) &&
           _active is { } identity && RoleAuthorizationPolicy.CanPerformStandardOperations(identity.Role) &&
           (screen != NativeScreen.Administration ||
            RoleAuthorizationPolicy.CanAccessUsersManagement(identity.Role)) &&
           // Never hide a live or ambiguous audited commit by leaving its page.
           (screen == NativeScreen.DailyAttendance ||
            (!DailyAttendance.Review.IsCommitting && !DailyAttendance.Review.HasUnverifiedCommit)) &&
           (screen == NativeScreen.MonthlyAttendance || !MonthlyAttendance.Correction.IsOpen && !MonthlyAttendance.RangeCorrection.IsOpen);

    public async Task InitializeAuthenticationAsync(
        INativeAuthenticationBackend backend, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (_backend is not null) throw new InvalidOperationException("Native auth is already initialized.");
        _backend = backend;
        IsBusySet(true);
        try
        {
            _isFirstRun = !await backend.IsProvisionedAsync(ct);
            _isReady = true;
            _loginError = "";
            Changed(nameof(IsFirstRun), nameof(IsReady), nameof(LoginError));
        }
        catch
        {
            _backend = null;
            _loginError = "تعذر فتح قاعدة البيانات المشفرة. لم يتم تسجيل الدخول أو تعديل بيانات القوة.";
            Changed(nameof(LoginError));
            throw;
        }
        finally { IsBusySet(false); }
    }

    public async Task<bool> LoginAsync(string username, string password,
        string confirmation = "", string fullName = "", CancellationToken ct = default)
    {
        if (_backend is null || !_isReady || IsBusy || IsAuthenticated) return false;
        _loginError = "";
        Changed(nameof(LoginError));
        username = username?.Trim() ?? "";
        if (username.Length == 0 || string.IsNullOrEmpty(password))
        {
            Fail("اسم المستخدم وكلمة المرور مطلوبان.");
            return false;
        }
        if (_isFirstRun && !string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Fail("تأكيد كلمة المرور غير مطابق لكلمة المرور.");
            return false;
        }
        IsBusySet(true);
        try
        {
            // The backend enforces one-time founder intent, Argon2id, lockout,
            // and server-side role/roster checks using existing audited services.
            NativeLoginResult answer = await _backend.SignInAsync(
                username, password, _isFirstRun,
                _isFirstRun ? confirmation : "",
                _isFirstRun ? fullName.Trim() : "", ct);
            if (answer.Identity is null)
            {
                Fail(answer.ErrorMessage ?? "تعذر تسجيل الدخول.");
                return false;
            }

            // NEVER treat a mere login acknowledgment as sufficient. Verify
            // the nonce against durable server authority before opening WPF.
            NativeIdentity? confirmed = await _backend.ResolveSessionAsync(answer.Identity.SessionNonce, ct);
            if (confirmed is null || !Matches(answer.Identity, confirmed))
            {
                await _backend.RevokeSessionAsync(answer.Identity.SessionNonce, ct);
                Fail("تعذر التحقق من صلاحية الجلسة؛ أعد تسجيل الدخول.");
                return false;
            }

            _active = confirmed;
            _isFirstRun = false;
            CurrentPage = _pages[NativeScreen.Dashboard];
            Changed(nameof(IsAuthenticated), nameof(Username), nameof(ActiveRole),
                nameof(RoleLabel), nameof(CanManageAccounts), nameof(IsFirstRun));
            _navigateCommand.NotifyAvailabilityChanged();
            _logoutCommand.NotifyAvailabilityChanged();
            // A dashboard failure displays a safe read-only error, but never
            // changes the server's successful authentication outcome.
            await Dashboard.RefreshAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            Fail("تعذر الاتصال بخدمات المصادقة المحلية. تحقق من حالة قاعدة البيانات.");
            return false;
        }
        finally { IsBusySet(false); }
    }

    /// <summary>
    /// Read-only check. A timer or background query MUST NOT renew the
    /// five-minute idle deadline or create a new session.
    /// </summary>
    public async Task VerifyCurrentSessionAsync(CancellationToken ct = default)
    {
        if (_active is not { } prior || _backend is null) return;
        try
        {
            var current = await _backend.ResolveSessionAsync(prior.SessionNonce, ct);
            if (current is null || !Matches(prior, current))
                ForgetSession("انتهت صلاحية الجلسة أو ألغيت؛ يرجى تسجيل الدخول من جديد.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            ForgetSession("تعذر التحقق من جلسة المستخدم؛ يرجى تسجيل الدخول من جديد.");
        }
    }

    /// <summary>
    /// Called ONLY by the real WPF mouse/keyboard input handler, not by
    /// navigation timers or background updates. Expired tokens cannot revive.
    /// </summary>
    public async Task RecordGenuineUserActivityAsync(CancellationToken ct = default)
    {
        if (_active is not { } session || _backend is null) return;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastNativeInputRefresh < TimeSpan.FromSeconds(10)) return;
        _lastNativeInputRefresh = now;
        try
        {
            await _backend.RefreshIdleDeadlineAsync(session.SessionNonce, ct);
            await VerifyCurrentSessionAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { ForgetSession("تعذر تأكيد جلسة المستخدم بعد النشاط؛ يرجى تسجيل الدخول."); }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (_active is not { } session || _backend is null || IsBusy) return;
        IsBusySet(true);
        try
        {
            // Never unlock the login screen or permit a different user while
            // the durable nonce has not been revoked.
            await _backend.RevokeSessionAsync(session.SessionNonce, ct);
            ForgetSession("");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { Fail("تعذر إلغاء الجلسة بأمان. حاول تسجيل الخروج مرة أخرى."); }
        finally { IsBusySet(false); }
    }

    private static bool Matches(NativeIdentity left, NativeIdentity right) =>
        string.Equals(left.UserId, right.UserId, StringComparison.Ordinal) &&
        string.Equals(left.Username, right.Username, StringComparison.Ordinal) &&
        left.Role == right.Role &&
        string.Equals(left.SessionNonce, right.SessionNonce, StringComparison.Ordinal);

    private void ForgetSession(string message)
    {
        // Scrub all native personnel/counters synchronously when the backend
        // revokes or expires the session, even if a SQL read is still running.
        Dashboard.Clear();
        DailyAttendance.Clear();
        MonthlyAttendance.Clear();
        Administration.Clear();
        _active = null;
        _lastNativeInputRefresh = DateTimeOffset.MinValue;
        _loginError = message;
        CurrentPage = _pages[NativeScreen.Dashboard];
        Changed(nameof(IsAuthenticated), nameof(Username), nameof(ActiveRole),
            nameof(RoleLabel), nameof(CanManageAccounts), nameof(LoginError));
        _navigateCommand.NotifyAvailabilityChanged();
        _logoutCommand.NotifyAvailabilityChanged();
    }

    private void Fail(string message)
    {
        _loginError = message;
        Changed(nameof(LoginError));
    }

    private void IsBusySet(bool value)
    {
        _isBusy = value;
        Changed(nameof(IsBusy));
        _logoutCommand.NotifyAvailabilityChanged();
    }

    private void Changed(params string[] properties)
    {
        foreach (string property in properties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class NativeCommand(Action<object?> execute, Predicate<object?> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute(parameter);
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }
    public event EventHandler? CanExecuteChanged;
    public void NotifyAvailabilityChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
