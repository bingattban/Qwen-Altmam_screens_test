using System.ComponentModel;
using System.Globalization;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeSettingsOverview(string Username,UserRole Role,string BackupDirectory,
    bool DirectoryAvailable,string BackupTime,bool BackupEnabled,int RetentionCount,
    string? LastBackupDate,string LastBackupFailure,string Theme)
{
    public bool Matches(NativeIdentity actor)=>RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role) &&
        Username==actor.Username && Role==actor.Role &&
        !string.IsNullOrWhiteSpace(BackupDirectory) && !BackupDirectory.Any(char.IsControl) &&
        TimeOnly.TryParseExact(BackupTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _) &&
        (RetentionCount==0 || RetentionCount is >=2 and <=365) &&
        (LastBackupDate is null || DateOnly.TryParseExact(LastBackupDate,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _)) &&
        LastBackupFailure is not null && Theme is "light" or "dark";
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

public sealed class NativeSettingsOverviewViewModel : INotifyPropertyChanged
{
    private INativeSettingsOverviewReader? _reader;
    private Func<NativeIdentity?>? _actor;
    private NativeIdentity? _bound;
    private NativeSettingsOverview? _snapshot;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _loading;
    private string _error="";
    public NativeSettingsOverviewViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRead && !IsLoading);
    }
    private NativeIdentity? Authorized=>_actor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role)?actor:null;
    public bool CanRead=>_reader is not null && Authorized is not null;
    public NativeSettingsOverview? Snapshot=>_bound is not null && _bound==Authorized?_snapshot:null;
    public bool HasSnapshot=>Snapshot is not null;
    public bool IsLoading=>_loading;
    public string Error=>CanRead?_error:"";
    public NativeCommand RefreshCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Attach(INativeSettingsOverviewReader reader,Func<NativeIdentity?> actor)
    {
        if(_reader is not null) throw new InvalidOperationException("Settings reader already attached.");
        _reader=reader; _actor=actor; Notify();
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
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
        _snapshot=null; _bound=null; _loading=false; _error=""; Notify();
    }
    public void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        RefreshCommand.NotifyAvailabilityChanged();
    }
}
