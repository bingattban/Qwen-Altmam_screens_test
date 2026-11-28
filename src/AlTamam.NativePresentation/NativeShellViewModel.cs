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
    private readonly NativeCommand _openAdministrativeHistoricalCommand;
    private readonly NativeCommand _openRosterExportCommand;
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
        Settings = new SettingsPageViewModel();
        Audit = new NativeAuditPageViewModel();
        Accounts = new NativeAccountsPageViewModel();
        Reports = new ReportsPageViewModel(CanOpenReportDestination,OpenReportDestination);
        Pages = new NativePageViewModel[]
        {
            Dashboard,
            DailyAttendance,
            MonthlyAttendance,
            Administration,
            Reports,
            Audit,
            Accounts,
            Settings
        };
        _pages = Pages.ToDictionary(page => page.Screen);
        _currentPage = _pages[NativeScreen.Dashboard];
        _navigateCommand = new NativeCommand(
            parameter =>
            {
                if (parameter is NativeScreen screen && CanNavigate(screen))
                {
                    // Clicking the selected section is not a refresh or cancellation.
                    if (CurrentPage.Screen == screen) return;
                    if (screen != NativeScreen.Dashboard)
                        Dashboard.AutoRegister.Close();
                    if (screen != NativeScreen.DailyAttendance)
                    {
                        DailyAttendance.Review.Cancel();
                        DailyAttendance.Drafts.Clear();
                    }
                    if (screen != NativeScreen.MonthlyAttendance)
                    {
                        MonthlyAttendance.SetRosterExportEntry(false);
                        MonthlyAttendance.CloseCellDetails();
                        MonthlyAttendance.Excel.Close();
                    }
                    if (screen != NativeScreen.Administration)
                    {
                        Administration.CloseProfile();
                        Administration.Mutation.ForceCancel();
                        Administration.Movement.ForceCancel();
                        Administration.Removal.ForceCancel();
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
                    if (screen != NativeScreen.AuditLog)
                        Audit.Clear();
                    if (screen != NativeScreen.Accounts)
                        Accounts.Clear();
                    if (screen != NativeScreen.Settings)
                    {
                        Settings.Diagnostics.Clear();
                        Settings.Overview.Clear();
                    }
                    CurrentPage = _pages[screen];
                    if (screen == NativeScreen.Dashboard)
                        _ = Dashboard.RefreshAsync();
                    if (screen == NativeScreen.DailyAttendance)
                        _ = DailyAttendance.RefreshAsync();
                    if (screen == NativeScreen.MonthlyAttendance)
                        _ = MonthlyAttendance.RefreshAsync();
                    if (screen == NativeScreen.Administration)
                        _ = Administration.RefreshAsync();
                    if (screen == NativeScreen.AuditLog)
                        _ = Audit.RefreshAsync();
                    if (screen == NativeScreen.Accounts)
                        _ = Accounts.RefreshAsync();
                    if (screen == NativeScreen.Settings)
                        _ = Settings.Overview.RefreshAsync();
                }
            },
            parameter => parameter is NativeScreen screen && CanNavigate(screen));
        _openAdministrativeHistoricalCommand = new NativeCommand(
            _ => OpenAdministrativeHistorical(), _ => CanOpenAdministrativeHistorical());
        _openRosterExportCommand = new NativeCommand(_ => OpenRosterExport(), _ => CanOpenRosterExport());
        _navigateCommand.CanExecuteChanged += (_, _) =>
        {
            _openAdministrativeHistoricalCommand.NotifyAvailabilityChanged();
            _openRosterExportCommand.NotifyAvailabilityChanged();
            Reports.NotifyAvailability();
        };
        Administration.PropertyChanged += (_, _) =>
        {
            _openAdministrativeHistoricalCommand.NotifyAvailabilityChanged();
            _navigateCommand.NotifyAvailabilityChanged();
        };
        DailyAttendance.Drafts.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        DailyAttendance.Review.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        MonthlyAttendance.Correction.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        MonthlyAttendance.RangeCorrection.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        MonthlyAttendance.Excel.PropertyChanged += (_, e) =>
        {
            if(e.PropertyName==nameof(MonthlyAttendance.Excel.IsExporting))_navigateCommand.NotifyAvailabilityChanged();
        };
        Settings.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        Audit.Edit.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        Accounts.Create.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        Accounts.Lifecycle.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();
        _logoutCommand = new NativeCommand(
            _ => _ = LogoutAsync(),
            _ => IsAuthenticated && !IsBusy);
    }

    public DashboardPageViewModel Dashboard { get; }
    public DailyAttendancePageViewModel DailyAttendance { get; }
    public MonthlyAttendancePageViewModel MonthlyAttendance { get; }
    public AdministrationPageViewModel Administration { get; }
    public SettingsPageViewModel Settings { get; }
    public NativeAuditPageViewModel Audit { get; }
    public NativeAccountsPageViewModel Accounts { get; }
    public ReportsPageViewModel Reports { get; }
    public IReadOnlyList<NativePageViewModel> Pages { get; }

    // Called once by the real desktop host after SQLCipher initialization.
    // The delegate only yields the current server-verified native principal.
    public void AttachDashboardReader(INativeDashboardReader reader)
    {
        Dashboard.AttachReader(reader, () => _active);
        Reports.AttachReader(reader);
        DailyAttendance.AttachReader(reader, () => _active);
        if (reader is INativeAccountReader accountReader)
            Accounts.AttachReader(accountReader, () => _active);
        if (reader is INativeAuditReader auditReader)
            Audit.AttachReader(auditReader, () => _active);
        if (reader is INativeMonthlyAttendanceReader archiveReader)
            MonthlyAttendance.AttachReader(archiveReader, () => _active);
        if (reader is INativePersonnelDirectoryReader personnelReader)
            Administration.AttachReader(personnelReader, () => _active);
        if (reader is INativeSettingsOverviewReader overviewReader)
            Settings.Overview.Attach(overviewReader, () => _active, () => !Settings.Backup.IsOpen && !Settings.Schedule.IsOpen && !Settings.Inspection.IsOpen && !Settings.MonthlyExportSchedule.IsOpen);
        if (reader is INativeThemeBackend themeBackend)
            Settings.Overview.AttachTheme(themeBackend, () => !Settings.IsReviewOpen &&
                ReferenceEquals(CurrentPage,Settings));
        if (reader is INativeProgramDiagnosticsBackend diagnosticsBackend)
            Settings.Diagnostics.Attach(diagnosticsBackend, () => _active,
                () => !Settings.IsReviewOpen && !Settings.Overview.IsLoading &&
                    ReferenceEquals(CurrentPage,Settings));
        if (reader is INativeBackupInspectionBackend inspectionBackend)
            Settings.AttachBackupInspection(inspectionBackend, () => _active);
        if (reader is INativeBackupScheduleBackend scheduleBackend)
            Settings.AttachBackupSchedule(scheduleBackend, () => _active);
        if (reader is INativeMonthlyExportScheduleBackend monthlyExportBackend)
            Settings.AttachMonthlyExportSchedule(monthlyExportBackend, () => _active);
        if (reader is INativeManualBackupBackend backupBackend)
            Settings.AttachManualBackup(backupBackend, () => _active);
        if (reader is INativeAdministrativeCatchUpBackend catchUpBackend)
            Settings.AttachCatchUp(catchUpBackend, () => _active);
        if (reader is INativeClosedDayPolicyBackend policyBackend)
            Settings.AttachClosedDayPolicy(policyBackend, () => _active);
        if (reader is INativeHistoricalPeriodReader historicalReader && reader is INativeHistoricalRangeBackend historicalBackend &&
            reader is INativeClosedDayCorrectionBackend historicalCatalogue)
            Settings.AttachHistorical(historicalReader,historicalBackend,historicalCatalogue,() => _active);
        _openRosterExportCommand.NotifyAvailabilityChanged();
    }
    public ICommand NavigateCommand => _navigateCommand;
    public ICommand OpenAdministrativeHistoricalCommand => _openAdministrativeHistoricalCommand;
    public ICommand OpenRosterExportCommand => _openRosterExportCommand;
    public ICommand LogoutCommand => _logoutCommand;
    public NativePageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (ReferenceEquals(_currentPage, value)) return;
            _currentPage = value;
            Changed(nameof(CurrentPage));
            Reports.NotifyAvailability();
            _openAdministrativeHistoricalCommand.NotifyAvailabilityChanged();
            _openRosterExportCommand.NotifyAvailabilityChanged();
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

    private bool CanOpenReportDestination(NativeScreen destination) =>
        !IsBusy && ReferenceEquals(CurrentPage,Reports) && CanNavigate(destination);
    private void OpenReportDestination(NativeScreen destination)
    {
        if(CanOpenReportDestination(destination))
            _navigateCommand.Execute(destination);
    }

    private bool CanOpenRosterExport() =>
        !IsBusy && (ReferenceEquals(CurrentPage,Administration) || ReferenceEquals(CurrentPage,Reports)) &&
        MonthlyAttendance.CanEnterRosterExport && CanNavigate(NativeScreen.MonthlyAttendance);

    private void OpenRosterExport()
    {
        if(!CanOpenRosterExport())return;
        _navigateCommand.Execute(NativeScreen.MonthlyAttendance);
        if(ReferenceEquals(CurrentPage,MonthlyAttendance))
            MonthlyAttendance.SetRosterExportEntry(true);
    }

    private bool CanOpenAdministrativeHistorical() =>
        !IsBusy && ReferenceEquals(CurrentPage,Administration) &&
        _active is { } actor && RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role) &&
        Administration.CanLeaveForHistoricalReview && Settings.CanOpenHistorical &&
        CanNavigate(NativeScreen.Settings);

    private void OpenAdministrativeHistorical()
    {
        if (!CanOpenAdministrativeHistorical()) return;
        _navigateCommand.Execute(NativeScreen.Settings);
        if (ReferenceEquals(CurrentPage,Settings))
            Settings.OpenHistoricalCommand.Execute(null);
    }

    private bool CanNavigate(NativeScreen screen)
        => IsAuthenticated && _pages.ContainsKey(screen) &&
           _active is { } identity && RoleAuthorizationPolicy.CanPerformStandardOperations(identity.Role) &&
           (screen != NativeScreen.Accounts || Accounts.CanRead &&
            (!ReferenceEquals(CurrentPage,Administration) || Administration.CanLeaveForHistoricalReview)) &&
           (screen != NativeScreen.AuditLog || Audit.CanRead &&
            (!ReferenceEquals(CurrentPage,Administration) || Administration.CanLeaveForHistoricalReview)) &&
           (screen != NativeScreen.Administration ||
            RoleAuthorizationPolicy.CanAccessUsersManagement(identity.Role)) &&
           (screen == NativeScreen.Administration ||
            !ReferenceEquals(CurrentPage,Administration) || Administration.CanLeaveForHistoricalReview) &&
           // Never hide a live or ambiguous audited commit by leaving its page.
           (screen == NativeScreen.DailyAttendance ||
            (!DailyAttendance.Review.IsCommitting && !DailyAttendance.Review.HasUnverifiedCommit)) &&
           (screen == NativeScreen.DailyAttendance || !DailyAttendance.Drafts.IsOpen) &&
           (screen == NativeScreen.DailyAttendance || !DailyAttendance.Review.IsSavingDraft) &&
           (screen == NativeScreen.MonthlyAttendance || !MonthlyAttendance.Correction.IsOpen && !MonthlyAttendance.RangeCorrection.IsOpen && !MonthlyAttendance.Excel.IsExporting) &&
           (screen == NativeScreen.Settings || !Settings.IsReviewOpen) &&
           !Audit.Edit.IsOpen && !Accounts.Create.IsOpen && !Accounts.Lifecycle.IsOpen;

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
            Accounts.Clear();
            Audit.Clear();
            Settings.NotifySessionChanged();
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
        Accounts.Clear();
        Audit.Clear();
        Settings.Clear();
        Settings.NotifySessionChanged();
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
        Reports.NotifyAvailability();
        _logoutCommand.NotifyAvailabilityChanged();
        _openAdministrativeHistoricalCommand.NotifyAvailabilityChanged();
        _openRosterExportCommand.NotifyAvailabilityChanged();
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
