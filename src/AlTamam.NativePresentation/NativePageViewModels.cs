namespace AlTamam.NativePresentation;

/// <summary>
/// Shared native page base. Reports and unconverted settings remain placeholders;
/// connected pages and administrative catch-up use canonical SQLCipher services.
/// No placeholder may mutate domain data.
/// Domain operations remain in the existing audited Application/Domain/Infrastructure.
/// </summary>
public abstract class NativePageViewModel(NativeScreen screen, string title)
{
    public NativeScreen Screen { get; } = screen;
    public string Title { get; } = title;
    public virtual bool IsDataConnected => false;
    public virtual string StatusMessage =>
        "تم تفعيل تسجيل الدخول الأصلي والتحقق من الجلسة. لم يتم بعد ربط هذه الشاشة بالخدمات التشغيلية؛ هذه معاينة XAML للمرحلة التالية.";
}

/// <summary>Navigation only: reuse the existing authorized report/Excel workflows.</summary>
public sealed class ReportsPageViewModel : NativePageViewModel, System.ComponentModel.INotifyPropertyChanged
{
    private bool _personnelAvailable,_monthlyAvailable;
    private bool _attached;
    private readonly Func<NativeScreen,bool> _canNavigate;
    private readonly Action<NativeScreen> _navigate;
    public ReportsPageViewModel(Func<NativeScreen,bool> canNavigate,Action<NativeScreen> navigate)
        : base(NativeScreen.Reports,"التقارير")
    {
        _canNavigate=canNavigate; _navigate=navigate;
        OpenPersonnelReportsCommand=new NativeCommand(_=>_navigate(NativeScreen.Administration),
            _=>_personnelAvailable && _canNavigate(NativeScreen.Administration));
        OpenMonthlyReportsCommand=new NativeCommand(_=>_navigate(NativeScreen.MonthlyAttendance),
            _=>_monthlyAvailable && _canNavigate(NativeScreen.MonthlyAttendance));
    }
    public NativeCommand OpenPersonnelReportsCommand { get; }
    public NativeCommand OpenMonthlyReportsCommand { get; }
    public override string StatusMessage => _attached
        ? "اختر مسار التقرير المتاح لصلاحيتك. يتم اختيار الأفراد والفترة ومعاينة التصدير في الشاشات الأصلية؛ لا يتم إنشاء أي ملف تلقائيا."
        : base.StatusMessage;
    public void AttachReader(INativeDashboardReader reader)
    {
        if(_attached) throw new InvalidOperationException("Reports entry already attached.");
        _attached=true;
        _personnelAvailable=reader is INativePersonnelDirectoryReader &&
            reader is INativePersonnelReportReader && reader is INativePersonnelGroupReportReader;
        _monthlyAvailable=reader is INativeMonthlyAttendanceReader && reader is INativeMonthlyExcelExportBackend;
        NotifyAvailability();
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void NotifyAvailability()
    {
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusMessage)));
        OpenPersonnelReportsCommand.NotifyAvailabilityChanged();
        OpenMonthlyReportsCommand.NotifyAvailabilityChanged();
    }
}
public sealed class SettingsPageViewModel : NativePageViewModel, System.ComponentModel.INotifyPropertyChanged
{
    private INativeAdministrativeCatchUpBackend? _backend;
    private INativeClosedDayPolicyBackend? _policyBackend;
    private Func<NativeIdentity?>? _policyActor;
    private INativeHistoricalPeriodReader? _historicalReader;
    private INativeHistoricalRangeBackend? _historicalBackend;
    private INativeClosedDayCorrectionBackend? _historicalCatalogue;
    private Func<NativeIdentity?>? _historicalActor;
    private Func<NativeIdentity?>? _liveActor;
    private INativeManualBackupBackend? _backupBackend;
    private INativeBackupScheduleBackend? _scheduleBackend;
    private INativeMonthlyExportScheduleBackend? _monthlyExportBackend;
    private INativeAttendanceClockBackend? _clockBackend;
    private Func<NativeIdentity?>? _clockActor;
    private Func<NativeIdentity?>? _monthlyExportActor;
    private INativeBackupInspectionBackend? _inspectionBackend;
    private Func<NativeIdentity?>? _inspectionActor;
    private Func<NativeIdentity?>? _scheduleActor;
    private Func<NativeIdentity?>? _backupActor;
    public SettingsPageViewModel() : base(NativeScreen.Settings, "الإعدادات")
    {
        CloseInspectionCommand=new NativeCommand(_=>Inspection.Close(),_=>Inspection.IsOpen);
        Inspection.PropertyChanged+=(_,_)=>NotifySessionChanged();
        OpenScheduleCommand=new NativeCommand(_=>OpenSchedule(),_=>CanOpenSchedule);
        CloseScheduleCommand=new NativeCommand(_=>_ = CloseScheduleAsync(),_=>Schedule.CanClose);
        Schedule.PropertyChanged+=(_,_)=>NotifySessionChanged();
        OpenMonthlyExportCommand=new NativeCommand(_=>OpenMonthlyExport(),_=>CanOpenMonthlyExport);
        CloseMonthlyExportCommand=new NativeCommand(_=>_ = CloseMonthlyExportAsync(),_=>MonthlyExportSchedule.CanClose);
        MonthlyExportSchedule.PropertyChanged+=(_,_)=>NotifySessionChanged();
        OpenDueClockCommand=new NativeCommand(_=>OpenClock(NativeAttendanceClockKind.Due),_=>CanOpenClock);
        OpenLockClockCommand=new NativeCommand(_=>OpenClock(NativeAttendanceClockKind.Lock),_=>CanOpenClock);
        CloseClockCommand=new NativeCommand(_=>_ = CloseClockAsync(),_=>AttendanceClock.CanClose);
        AttendanceClock.PropertyChanged+=(_,_)=>NotifySessionChanged();
        OpenManualBackupCommand=new NativeCommand(_=>OpenBackupReview(),_=>CanOpenBackup && Overview.Snapshot?.DirectoryAvailable==true);
        CloseBackupCommand=new NativeCommand(_=>_ = CloseBackupAsync(),_=>Backup.CanClose);
        Backup.PropertyChanged+=(_,_)=>NotifySessionChanged();
        Overview.PropertyChanged+=(_,_)=>NotifySettingsAvailability();
        Diagnostics.PropertyChanged+=(_,_)=>NotifySessionChanged();
        CatchUp = new NativeAdministrativeCatchUpReview();
        Policy = new NativeClosedDayPolicyReview();
        Historical = new NativeHistoricalPeriodReview();
        HistoricalRange = new NativeHistoricalRangeReview();
        HistoricalSingle = new NativeClosedDayCorrectionReview();
        OpenHistoricalSingleCommand = new NativeCommand(_ => _ = OpenHistoricalSingleAsync(), _ => CanOpenHistoricalSingle);
        CloseHistoricalSingleCommand = new NativeCommand(_ => _ = CloseHistoricalSingleAsync(), _ => HistoricalSingle.CanClose);
        HistoricalSingle.PropertyChanged += (_, _) => Historical.NotifyScopeChanged();
        OpenHistoricalCommand = new NativeCommand(_ => OpenHistorical(), _ => CanOpenHistorical);
        OpenHistoricalRangeCommand = new NativeCommand(_ => _ = OpenHistoricalRangeAsync(), _ => CanOpenHistoricalRange);
        CloseHistoricalRangeCommand = new NativeCommand(_ => _ = CloseHistoricalRangeAsync(), _ => HistoricalRange.CanClose);
        Historical.PropertyChanged += (_, _) => NotifySessionChanged();
        HistoricalRange.PropertyChanged += (_, _) => Historical.NotifyScopeChanged();
        OpenCatchUpCommand = new NativeCommand(_ =>
        {
            if (CanOpenCatchUp && _backend is not null && _liveActor is not null)
                CatchUp.Open(_backend,_liveActor);
        }, _ => CanOpenCatchUp);
        EnablePolicyCommand = new NativeCommand(_ => OpenPolicy(true), _ => CanOpenPolicy);
        DisablePolicyCommand = new NativeCommand(_ => OpenPolicy(false), _ => CanOpenPolicy);
        CatchUp.PropertyChanged += (_, _) => NotifySessionChanged();
        Policy.PropertyChanged += (_, _) => NotifySessionChanged();
    }
    public NativeSettingsOverviewViewModel Overview { get; }=new();
    public NativeProgramDiagnosticsViewModel Diagnostics { get; }=new();
    public NativeManualBackupReview Backup { get; }=new();
    public NativeBackupScheduleReview Schedule { get; }=new();
    public NativeAttendanceClockReview AttendanceClock {get;}=new();
    public NativeCommand OpenDueClockCommand {get;}
    public NativeCommand OpenLockClockCommand {get;}
    public NativeCommand CloseClockCommand {get;}
    public bool CanOpenClock=>!IsReviewOpen && !Overview.IsLoading && _clockBackend is not null &&
        _clockActor?.Invoke() is {} actor && Authorized(actor) &&
        Overview.Snapshot is {HasAttendanceSettings:true} snapshot && snapshot.Matches(actor);
    public void AttachAttendanceClock(INativeAttendanceClockBackend backend,Func<NativeIdentity?> actor)
    {
        if(_clockBackend is not null)throw new InvalidOperationException("Clock backend already attached.");
        _clockBackend=backend;_clockActor=actor;NotifySessionChanged();
    }
    public void OpenClock(NativeAttendanceClockKind kind)
    {
        if(CanOpenClock && _clockBackend is {} backend && _clockActor is {} live &&
            live() is {} actor && Overview.Snapshot is {} snapshot)
            AttendanceClock.Open(backend,actor,snapshot,live,()=>Overview.Snapshot,kind);
    }
    public async Task CloseClockAsync(CancellationToken ct=default)
    {
        bool reload=AttendanceClock.Receipt is not null || AttendanceClock.NeedsReload;
        if(!AttendanceClock.Close())return;
        if(reload)await Overview.RefreshAsync(ct);
    }
    public NativeMonthlyExportScheduleReview MonthlyExportSchedule {get;}=new();
    public NativeCommand OpenMonthlyExportCommand {get;}
    public NativeCommand CloseMonthlyExportCommand {get;}
    public bool CanOpenMonthlyExport=>!IsReviewOpen && !Overview.IsLoading && _monthlyExportBackend is not null &&
        _monthlyExportActor?.Invoke() is {} actor && Authorized(actor) &&
        Overview.Snapshot is {HasMonthlyExport:true} snapshot && snapshot.Matches(actor);
    public void AttachMonthlyExportSchedule(INativeMonthlyExportScheduleBackend backend,Func<NativeIdentity?> actor)
    {
        if(_monthlyExportBackend is not null)throw new InvalidOperationException("Monthly export backend already attached.");
        _monthlyExportBackend=backend;_monthlyExportActor=actor;NotifySessionChanged();
    }
    public void OpenMonthlyExport()
    {
        if(CanOpenMonthlyExport && _monthlyExportBackend is {} backend && _monthlyExportActor is {} live &&
            live() is {} actor && Overview.Snapshot is {} snapshot)
            MonthlyExportSchedule.Open(backend,actor,snapshot,live,()=>Overview.Snapshot);
    }
    public async Task CloseMonthlyExportAsync(CancellationToken ct=default)
    {
        bool reload=MonthlyExportSchedule.Receipt is not null || MonthlyExportSchedule.NeedsReload;
        if(!MonthlyExportSchedule.Close())return;
        if(reload)await Overview.RefreshAsync(ct);
    }
    public NativeBackupInspectionReview Inspection { get; }=new();
    public NativeCommand CloseInspectionCommand { get; }
    public bool CanExportRecovery=>!IsReviewOpen && !Overview.IsLoading &&
        _inspectionActor?.Invoke() is { Role:AlTamam.Domain.Enums.UserRole.GeneralAdministration };
    public NativeIdentity? RecoveryExportActor=>CanExportRecovery?_inspectionActor?.Invoke():null;
    public NativeRecoveryExportIntent? GetRecoveryExportIntent(string destination)
    {
        if(RecoveryExportActor is not { } actor) return null;
        var intent=new NativeRecoveryExportIntent(actor,destination);
        return intent.Matches(RecoveryExportActor)?intent:null;
    }
    public bool CanOpenInspection=>!IsReviewOpen && !Overview.IsLoading && _inspectionBackend is not null &&
        _inspectionActor?.Invoke() is { } actor &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role);
    public void AttachBackupInspection(INativeBackupInspectionBackend backend,Func<NativeIdentity?> actor)
    {
        if(_inspectionBackend is not null) throw new InvalidOperationException("Inspection backend already attached.");
        _inspectionBackend=backend; _inspectionActor=actor; NotifySessionChanged();
    }
    public bool OpenInspection(string path)=>CanOpenInspection && _inspectionBackend is { } backend &&
        _inspectionActor?.Invoke() is { } actor && Inspection.Open(backend,actor,_inspectionActor,path);
    public NativeCommand OpenScheduleCommand { get; }
    public NativeCommand CloseScheduleCommand { get; }
    public bool CanOpenSchedule=>!IsReviewOpen && !Overview.IsLoading && _scheduleBackend is not null &&
        _scheduleActor?.Invoke() is { } actor && Authorized(actor) &&
        Overview.Snapshot is { } snapshot && snapshot.Matches(actor);
    public void AttachBackupSchedule(INativeBackupScheduleBackend backend,Func<NativeIdentity?> actor)
    {
        if(_scheduleBackend is not null) throw new InvalidOperationException("Schedule backend already attached.");
        _scheduleBackend=backend; _scheduleActor=actor; NotifySessionChanged();
    }
    public void OpenSchedule()
    {
        if(CanOpenSchedule && _scheduleBackend is not null && _scheduleActor?.Invoke() is { } actor &&
            Overview.Snapshot is { } snapshot)
            Schedule.Open(_scheduleBackend,actor,snapshot,_scheduleActor,()=>Overview.Snapshot);
    }
    public async Task CloseScheduleAsync(CancellationToken ct=default)
    {
        bool reload=Schedule.Receipt is not null || Schedule.NeedsReload;
        if(!Schedule.Close()) return;
        if(reload) await Overview.RefreshAsync(ct);
    }
    public NativeCommand OpenManualBackupCommand { get; }
    public NativeCommand CloseBackupCommand { get; }
    public bool CanOpenBackup=>!IsReviewOpen && !Overview.IsLoading && _backupBackend is not null &&
        _backupActor?.Invoke() is { } actor && Overview.Snapshot is { } snapshot && snapshot.Matches(actor);
    public void AttachManualBackup(INativeManualBackupBackend backend,Func<NativeIdentity?> actor)
    {
        if(_backupBackend is not null) throw new InvalidOperationException("Backup backend already attached.");
        _backupBackend=backend; _backupActor=actor; NotifySessionChanged();
    }
    public bool OpenBackupReview(string? directory=null)
    {
        if(!CanOpenBackup || _backupBackend is null || _backupActor?.Invoke() is not { } actor ||
            Overview.Snapshot is not { } snapshot) return false;
        return Backup.Open(_backupBackend,actor,snapshot,_backupActor,()=>Overview.Snapshot,directory);
    }
    public async Task CloseBackupAsync(CancellationToken ct=default)
    {
        bool reload=Backup.Receipt is not null || Backup.SavedDirectory is not null || Backup.NeedsReload;
        if(!Backup.Close()) return;
        if(reload) await Overview.RefreshAsync(ct);
    }
    private void NotifyBackupCommands()
    {
        Diagnostics.NotifyAvailability();
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenBackup)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDataConnected)));
        OpenManualBackupCommand.NotifyAvailabilityChanged();
        CloseBackupCommand.NotifyAvailabilityChanged();
        OpenScheduleCommand.NotifyAvailabilityChanged(); CloseScheduleCommand.NotifyAvailabilityChanged();
        OpenMonthlyExportCommand.NotifyAvailabilityChanged(); CloseMonthlyExportCommand.NotifyAvailabilityChanged();
        OpenDueClockCommand.NotifyAvailabilityChanged(); OpenLockClockCommand.NotifyAvailabilityChanged(); CloseClockCommand.NotifyAvailabilityChanged();
        CloseInspectionCommand.NotifyAvailabilityChanged();
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenInspection)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanExportRecovery)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenSchedule)));
    }
    public NativeAdministrativeCatchUpReview CatchUp { get; }
    public System.Windows.Input.ICommand OpenCatchUpCommand { get; }
    public NativeClosedDayPolicyReview Policy { get; }
    public System.Windows.Input.ICommand EnablePolicyCommand { get; }
    public System.Windows.Input.ICommand DisablePolicyCommand { get; }
    public NativeHistoricalPeriodReview Historical { get; }
    public NativeHistoricalRangeReview HistoricalRange { get; }
    public NativeClosedDayCorrectionReview HistoricalSingle { get; }
    public System.Windows.Input.ICommand OpenHistoricalSingleCommand { get; }
    public System.Windows.Input.ICommand CloseHistoricalSingleCommand { get; }
    public System.Windows.Input.ICommand OpenHistoricalCommand { get; }
    public System.Windows.Input.ICommand OpenHistoricalRangeCommand { get; }
    public System.Windows.Input.ICommand CloseHistoricalRangeCommand { get; }
    public bool IsReviewOpen => Overview.IsSavingTheme || Diagnostics.IsBusy || Inspection.IsOpen || Schedule.IsOpen || Backup.IsOpen || CatchUp.IsOpen || Policy.IsOpen || Historical.IsOpen || HistoricalRange.IsOpen || HistoricalSingle.IsOpen || AttendanceClock.IsOpen || MonthlyExportSchedule.IsOpen;
    private static bool Authorized(NativeIdentity? actor) => actor is not null &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);
    private bool HasCatchUpAccess => _backend is not null && Authorized(_liveActor?.Invoke());
    private bool HasPolicyAccess => _policyBackend is not null && Authorized(_policyActor?.Invoke());
    public bool CanOpenCatchUp => HasCatchUpAccess && !IsReviewOpen;
    public bool CanOpenPolicy => HasPolicyAccess && !IsReviewOpen;
    private bool HasHistoricalAccess => _historicalReader is not null && _historicalBackend is not null &&
        _historicalCatalogue is not null && Authorized(_historicalActor?.Invoke());
    public bool CanOpenHistorical => HasHistoricalAccess && !IsReviewOpen;
    public bool CanOpenHistoricalRange => HasHistoricalAccess && Historical.CanEdit && Historical.HasEvidence &&
        !CatchUp.IsOpen && !Policy.IsOpen && !HistoricalRange.IsOpen && !HistoricalSingle.IsOpen;
    public bool CanOpenHistoricalSingle => CanOpenHistoricalRange && Historical.Evidence is { } evidence &&
        NativeClosedDayCorrectionReview.CanUsePeriod(evidence);
    public override bool IsDataConnected => Overview.HasSnapshot || HasCatchUpAccess || HasPolicyAccess || HasHistoricalAccess;
    public override string StatusMessage
    {
        get
        {
            var actor = _liveActor?.Invoke() ?? _policyActor?.Invoke() ?? _historicalActor?.Invoke();
            if (actor is null)
                return "لم يتم تسجيل الدخول أو ربط جلسة معتمدة بخدمات الإعدادات.";
            if (!AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
                return "لا تملك صلاحية استدراك التحضير الناقص؛ بقية الإعدادات قيد التحويل.";
            if (HasHistoricalAccess)
                return "البحث التاريخي وتصحيح فترة محددة متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
            if (HasCatchUpAccess && HasPolicyAccess)
                return "استدراك التحضير الناقص والتحكم في إذن التصحيح الاستثنائي متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
            if (HasPolicyAccess)
                return "التحكم في إذن التصحيح الاستثنائي متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
            return HasCatchUpAccess
                ? "استدراك التحضير الناقص متاح للإدارة؛ بقية الإعدادات قيد التحويل."
                : "لم يتم ربط خدمات الإعدادات بعد.";
        }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void AttachCatchUp(INativeAdministrativeCatchUpBackend backend,Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null) throw new InvalidOperationException("Settings catch-up backend already attached.");
        _backend = backend;
        _liveActor = liveActor;
        NotifySessionChanged();
    }
    public void AttachClosedDayPolicy(INativeClosedDayPolicyBackend backend,Func<NativeIdentity?> liveActor)
    {
        if (_policyBackend is not null) throw new InvalidOperationException("Settings policy backend already attached.");
        _policyBackend = backend;
        _policyActor = liveActor;
        NotifySessionChanged();
    }
    public void AttachHistorical(INativeHistoricalPeriodReader reader,INativeHistoricalRangeBackend backend,
        INativeClosedDayCorrectionBackend catalogue,Func<NativeIdentity?> liveActor)
    {
        if (_historicalReader is not null) throw new InvalidOperationException("Historical settings backend already attached.");
        _historicalReader=reader;
        _historicalBackend=backend;
        _historicalCatalogue=catalogue;
        _historicalActor=liveActor;
        NotifySessionChanged();
    }

    private void OpenHistorical()
    {
        if (CanOpenHistorical && _historicalReader is not null && _historicalActor is not null)
            Historical.Open(_historicalReader,_historicalActor,()=>HistoricalRange.IsOpen || HistoricalSingle.IsOpen);
    }

    public async Task OpenHistoricalRangeAsync(CancellationToken ct=default)
    {
        if (!CanOpenHistoricalRange || _historicalBackend is null || _historicalCatalogue is null ||
            _historicalActor is null || Historical.Evidence is not { } evidence) return;
        if (HistoricalRange.OpenForPeriod(_historicalBackend,_historicalCatalogue,_historicalActor,()=>Historical.Evidence,evidence))
            await HistoricalRange.LoadChoicesAsync(ct);
    }

    public async Task CloseHistoricalRangeAsync(CancellationToken ct=default)
    {
        bool reload=HistoricalRange.Receipt is not null || HistoricalRange.NeedsPeriodReload;
        if (!HistoricalRange.Close()) return;
        if (reload)
        {
            Historical.InvalidateEvidence();
            await Historical.ReadAsync(ct);
        }
    }

    public async Task OpenHistoricalSingleAsync(CancellationToken ct=default)
    {
        if (!CanOpenHistoricalSingle || _historicalCatalogue is null ||
            _historicalActor is null || Historical.Evidence is not { } evidence) return;
        if (HistoricalSingle.OpenForPeriod(_historicalCatalogue,_historicalActor,()=>Historical.Evidence,evidence))
            await HistoricalSingle.LoadChoicesAsync(ct);
    }

    public async Task CloseHistoricalSingleAsync(CancellationToken ct=default)
    {
        bool reload=HistoricalSingle.Receipt is not null || HistoricalSingle.NeedsPeriodReload;
        if (!HistoricalSingle.Close()) return;
        if (reload)
        {
            Historical.InvalidateEvidence();
            await Historical.ReadAsync(ct);
        }
    }

    private void OpenPolicy(bool enabled)
    {
        if (CanOpenPolicy && _policyBackend is not null && _policyActor is not null)
            Policy.Open(_policyBackend,_policyActor,enabled);
    }
    public void NotifySessionChanged()=>Overview.Notify();
    private void NotifySettingsAvailability()
    {
        NotifyBackupCommands();
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenCatchUp)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDataConnected)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusMessage)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenPolicy)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsReviewOpen)));
        ((NativeCommand)OpenCatchUpCommand).NotifyAvailabilityChanged();
        ((NativeCommand)EnablePolicyCommand).NotifyAvailabilityChanged();
        ((NativeCommand)DisablePolicyCommand).NotifyAvailabilityChanged();
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenHistorical)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenHistoricalRange)));
        ((NativeCommand)OpenHistoricalCommand).NotifyAvailabilityChanged();
        ((NativeCommand)OpenHistoricalRangeCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseHistoricalRangeCommand).NotifyAvailabilityChanged();
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenHistoricalSingle)));
        ((NativeCommand)OpenHistoricalSingleCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseHistoricalSingleCommand).NotifyAvailabilityChanged();
    }
    public void Clear()
    {
        Inspection.ForceClear();
        Schedule.ForceClear();
        MonthlyExportSchedule.ForceClear();
        AttendanceClock.ForceClear();
        Backup.ForceClear();
        Diagnostics.Clear();
        Overview.Clear();
        CatchUp.ForceClear();
        Policy.ForceClear();
        HistoricalRange.ForceClear();
        HistoricalSingle.ForceClear();
        Historical.ForceClear();
    }
}
