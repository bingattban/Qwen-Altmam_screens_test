using System.ComponentModel;
using System.Globalization;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeAutomaticMonthlyExcelState(bool Enabled,string ExportTime,string Directory,
    string LastSuccessDate,string LastFailure)
{
    public bool IsValid=>TimeOnly.TryParseExact(ExportTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        Directory is not null && !Directory.Any(char.IsControl) && (!Enabled || !string.IsNullOrWhiteSpace(Directory)) &&
        LastSuccessDate is not null && (LastSuccessDate.Length==0 || DateOnly.TryParseExact(LastSuccessDate,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) && LastFailure is not null;
    public string EnabledLabel=>Enabled?"مفعّل في الإعدادات المحفوظة":"متوقف في الإعدادات المحفوظة";
    public string DirectoryLabel=>string.IsNullOrWhiteSpace(Directory)?"لم يحدد مجلد للتصدير":Directory;
    public string LastSuccessLabel=>LastSuccessDate.Length==0?"لا يوجد تصدير ناجح مسجل":LastSuccessDate;
}

// Read-only evidence from the original policy service. A retired or bounded saved
// status can be displayed as unconfigured, never silently replaced.
public sealed record NativeAttendanceSettingsState(string DueTime,string LockTime,string? DefaultStatusId,
    string? DefaultStatusName,bool Configured)
{
    private static bool ValidClock(string value)=>value is {Length:5} &&
        TimeOnly.TryParseExact(value,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
    public bool IsValid=>ValidClock(DueTime) && ValidClock(LockTime) &&
        (DefaultStatusId is null || (DefaultStatusId.Length<=128 && !DefaultStatusId.Any(char.IsControl))) &&
        (DefaultStatusName is null || !DefaultStatusName.Any(char.IsControl)) &&
        (!Configured || (!string.IsNullOrWhiteSpace(DefaultStatusId) &&
            DefaultStatusId is not ("STS-LEAVE" or "STS-PERMISSION") &&
            !string.IsNullOrWhiteSpace(DefaultStatusName)));
    public string DefaultLabel=>Configured?DefaultStatusName??"":
        string.IsNullOrEmpty(DefaultStatusId)?"لم تحدد حالة افتراضية":
        $"الحالة المحفوظة غير صالحة للتطبيق التلقائي: {DefaultStatusName??DefaultStatusId}";
}

public sealed record NativeSettingsOverview(string Username,UserRole Role,string BackupDirectory,
    bool DirectoryAvailable,string BackupTime,bool BackupEnabled,int RetentionCount,
    string? LastBackupDate,string LastBackupFailure,string Theme)
{
    public NativeAutomaticMonthlyExcelState? MonthlyExport {get;init;}
    public bool HasMonthlyExport=>MonthlyExport is not null;
    public NativeAttendanceSettingsState? AttendanceSettings {get;init;}
    public bool HasAttendanceSettings=>AttendanceSettings is not null;
    public bool Matches(NativeIdentity actor)=>RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role) &&
        Username==actor.Username && Role==actor.Role &&
        !string.IsNullOrWhiteSpace(BackupDirectory) && !BackupDirectory.Any(char.IsControl) &&
        TimeOnly.TryParseExact(BackupTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        (RetentionCount==0 || RetentionCount is >=2 and <=365) &&
        (LastBackupDate is null || DateOnly.TryParseExact(LastBackupDate,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) &&
        LastBackupFailure is not null && (MonthlyExport is not {} monthly || monthly.IsValid) &&
        (AttendanceSettings is not {} policy || (RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) && policy.IsValid)) &&
        Theme is "light" or "dark";
    public string RoleLabel=>Role.ToArabicLabel();
    public string DirectoryLabel=>DirectoryAvailable?"متاح وفق فحص المسار الحالي":"غير متاح";
    public string BackupEnabledLabel=>BackupEnabled?"مفعّل في الإعدادات":"متوقف في الإعدادات";
    public string RetentionLabel=>RetentionCount==0?"الاحتفاظ بجميع النسخ التلقائية":$"{RetentionCount} نسخة تلقائية";
    public string LastBackupLabel=>LastBackupDate??"لا توجد نسخة تلقائية ناجحة مسجلة";
    public string ThemeLabel=>Theme=="dark"?"ليلي":"نهاري";
}

public interface INativeSettingsOverviewReader
{
    Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,CancellationToken ct=default);
}

public interface INativeThemeBackend
{
    Task<NativeSettingsOverview> SaveThemeAsync(NativeIdentity actor,string theme,CancellationToken ct=default);
}

public sealed class NativeSettingsOverviewViewModel : INotifyPropertyChanged
{
    private INativeSettingsOverviewReader? _reader;
    private Func<NativeIdentity?>? _actor;
    private Func<bool>? _canRefresh;
    private NativeIdentity? _bound;
    private NativeSettingsOverview? _snapshot;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _loading;
    private bool _savingTheme;
    private INativeThemeBackend? _themeBackend;
    private Func<bool>? _canSaveTheme;
    private string _error="";
    public NativeSettingsOverviewViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !IsLoading && !IsSavingTheme && (_canRefresh?.Invoke()??true));
        SaveLightCommand=new NativeCommand(_=>_ = SaveThemeAsync("light"),_=>CanSaveTheme("light"));
        SaveDarkCommand=new NativeCommand(_=>_ = SaveThemeAsync("dark"),_=>CanSaveTheme("dark"));
    }
    private NativeIdentity? Authorized=>_actor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role)?actor:null;
    public bool CanRead=>_reader is not null && Authorized is not null;
    public NativeSettingsOverview? Snapshot=>_bound is not null && _bound==Authorized?_snapshot:null;
    public bool HasSnapshot=>Snapshot is not null;
    public bool IsLoading=>_loading;
    public bool IsSavingTheme=>_savingTheme;
    public NativeCommand SaveLightCommand { get; }
    public NativeCommand SaveDarkCommand { get; }
    public void AttachTheme(INativeThemeBackend backend,Func<bool> canSave)
    {
        if(_themeBackend is not null) throw new InvalidOperationException("Theme backend already attached.");
        _themeBackend=backend; _canSaveTheme=canSave; Notify();
    }
    private bool CanSaveTheme(string theme)=>theme is "light" or "dark" &&
        _themeBackend is not null && !IsLoading && !IsSavingTheme &&
        (_canSaveTheme?.Invoke()??false) && Snapshot is { } snapshot && snapshot.Theme!=theme;

    public async Task SaveThemeAsync(string theme,CancellationToken ct=default)
    {
        if(!CanSaveTheme(theme) || _themeBackend is not { } backend || Authorized is not { } actor) return;
        long generation=++_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _savingTheme=true; _error=""; Notify();
        try
        {
            var result=await backend.SaveThemeAsync(actor,theme,pending.Token);
            if(generation!=_generation || pending.IsCancellationRequested || Authorized!=actor) return;
            if(result is null || !result.Matches(actor) || result.Theme!=theme)
                throw new InvalidOperationException("Invalid canonical theme receipt.");
            _snapshot=result; _bound=actor;
        }
        catch
        {
            if(generation==_generation && Authorized==actor)
            {
                _snapshot=null; _bound=null;
                _error="تعذر تأكيد حفظ المظهر. حدّث الإعدادات قبل إعادة المحاولة.";
            }
        }
        finally
        {
            if(generation==_generation)
            {
                if(pending.IsCancellationRequested) { _snapshot=null; _bound=null; }
                _pending=null; _savingTheme=false; Notify();
            }
        }
    }
    public string Error=>CanRead?_error:"";
    public NativeCommand RefreshCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Attach(INativeSettingsOverviewReader reader,Func<NativeIdentity?> actor,Func<bool>? canRefresh=null)
    {
        if(_reader is not null) throw new InvalidOperationException("Settings reader already attached.");
        _reader=reader; _actor=actor; _canRefresh=canRefresh; Notify();
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        if(IsSavingTheme || _canRefresh?.Invoke()==false) return;
        Clear();
        if(_reader is null || Authorized is not { } actor) return;
        long generation=_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _loading=true; Notify();
        try
        {
            var result=await _reader.ReadSettingsOverviewAsync(actor,pending.Token);
            if(generation!=_generation || pending.IsCancellationRequested || Authorized!=actor) return;
            if(result is null || !result.Matches(actor))
                throw new InvalidOperationException("Invalid canonical settings overview.");
            _snapshot=result; _bound=actor;
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
        catch
        {
            if(generation==_generation && Authorized==actor)
                _error="تعذر قراءة الإعدادات الموثوقة. أعد المحاولة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _loading=false; Notify();
            }
        }
    }
    public void Clear()
    {
        ++_generation; _pending?.Cancel(); _pending=null;
        _snapshot=null; _bound=null; _loading=false; _savingTheme=false; _error=""; Notify();
    }
    public void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged();
        SaveLightCommand.NotifyAvailabilityChanged(); SaveDarkCommand.NotifyAvailabilityChanged();
    }
}
