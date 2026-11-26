using System.ComponentModel;
using AlTamam.Domain.Policies;
using AlTamam.Domain.Enums;

namespace AlTamam.NativePresentation;

public sealed record NativeBackupCounts(long People,long Attendance,long AuditEvents,long Users)
{
    public bool IsValid=>People>=0 && Attendance>=0 && AuditEvents>=0 && Users>=0;
}

public sealed record NativeBackupInspection(string Path,string Sha256,long FileSize,int SchemaVersion,
    bool Compatible,bool Registered,NativeBackupCounts Counts)
{
    public static bool ValidPath(string? path)
    {
        if(path is not { Length:>0 and <=1024 } || path.Any(char.IsControl) ||
            !System.IO.Path.IsPathFullyQualified(path)) return false;
        try
        {
            return !path.StartsWith(@"\\",StringComparison.Ordinal) &&
                !path.StartsWith("//",StringComparison.Ordinal) &&
                (System.IO.Path.GetExtension(path) is ".dbbackup" or ".db") &&
                System.IO.Path.GetFullPath(path)==path;
        }
        catch(ArgumentException) { return false; }
        catch(NotSupportedException) { return false; }
        catch(IOException) { return false; }
    }
    public static bool SamePath(string? left,string? right)=>ValidPath(left) && ValidPath(right) &&
        string.Equals(left,right,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    public bool Matches(string path)=>SamePath(Path,path) && Sha256 is { Length:64 } &&
        Sha256.All(Uri.IsHexDigit) && FileSize>0 && SchemaVersion>0 && Counts is { IsValid:true };
    public string CompatibilityLabel=>Compatible?"متوافق مع الإصدار الحالي":"إصدار مختلف؛ المعاينة غير متاحة";
    public string CatalogLabel=>Registered?"مسجلة في سجل النسخ الحالي":"غير مسجلة في السجل الحالي؛ تحقق مستقل فقط";
}

public sealed record NativeRestoreComparison(string Path,string Sha256,int SchemaVersion,
    NativeBackupCounts Incoming,NativeBackupCounts Current)
{
    public bool Matches(NativeBackupInspection evidence)=>evidence.Matches(evidence.Path) && evidence.Compatible &&
        NativeBackupInspection.SamePath(Path,evidence.Path) &&
        string.Equals(Sha256,evidence.Sha256,StringComparison.OrdinalIgnoreCase) &&
        SchemaVersion==evidence.SchemaVersion && Incoming==evidence.Counts && Current is { IsValid:true };
}

public sealed record NativeRestoreIntent(NativeIdentity Actor,NativeBackupInspection Evidence,NativeRestoreComparison Comparison)
{
    public bool IsValid=>Actor is { Role:UserRole.GeneralAdministration } &&
        !string.IsNullOrWhiteSpace(Actor.SessionNonce) && Evidence is not null &&
        Comparison is not null && Comparison.Matches(Evidence);
}

public interface INativeBackupInspectionBackend
{
    Task<NativeBackupInspection> InspectBackupAsync(NativeIdentity actor,string path,CancellationToken ct=default);
    Task<NativeRestoreComparison> PreviewBackupRestoreAsync(NativeIdentity actor,NativeBackupInspection evidence,CancellationToken ct=default);
}

public sealed class NativeBackupInspectionReview:INotifyPropertyChanged
{
    private INativeBackupInspectionBackend? _backend;
    private NativeIdentity? _actor;
    private Func<NativeIdentity?>? _liveActor;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _busy;
    public NativeBackupInspectionReview()
    {
        InspectCommand=new NativeCommand(_=>_ = InspectAsync(),_=>CanInspect);
        PreviewCommand=new NativeCommand(_=>_ = PreviewAsync(),_=>CanPreview);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand InspectCommand { get; }
    public NativeCommand PreviewCommand { get; }
    public bool IsOpen=>_actor is not null && _actor==_liveActor?.Invoke();
    public bool IsBusy=>IsOpen && _busy;
    public bool CanInspect=>IsOpen && !_busy;
    public bool CanPreview=>CanInspect && Evidence is { Compatible:true } &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(_actor!.Role);
    public bool CanRequestRestore=>CanInspect && _actor?.Role==UserRole.GeneralAdministration &&
        Evidence is { } evidence && Comparison is { } comparison && comparison.Matches(evidence);
    public NativeRestoreIntent? GetRestoreIntent()=>CanRequestRestore
        ?new(_actor!,Evidence!,Comparison!):null;
    public bool MatchesRestoreIntent(NativeRestoreIntent intent)=>CanRequestRestore && intent.IsValid &&
        _actor==intent.Actor && ReferenceEquals(Evidence,intent.Evidence) &&
        ReferenceEquals(Comparison,intent.Comparison);
    public bool HasEvidence=>IsOpen && Evidence is not null;
    public bool HasComparison=>IsOpen && Comparison is not null;
    public string SelectedPath { get; private set; }="";
    public string Error { get; private set; }="";
    public NativeBackupInspection? Evidence { get; private set; }
    public NativeRestoreComparison? Comparison { get; private set; }
    public bool Open(INativeBackupInspectionBackend backend,NativeIdentity actor,Func<NativeIdentity?> liveActor,string path)
    {
        if(_actor is not null || liveActor()!=actor || !RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role) ||
            !NativeBackupInspection.ValidPath(path)) return false;
        _backend=backend; _actor=actor; _liveActor=liveActor; SelectedPath=path; Notify(); return true;
    }
    public async Task InspectAsync(CancellationToken ct=default)
    {
        if(!CanInspect || _backend is not { } backend || _actor is not { } actor || ct.IsCancellationRequested) return;
        string path=SelectedPath;
        await RunAsync(async token=>
        {
            var result=await backend.InspectBackupAsync(actor,path,token);
            if(result is null || !result.Matches(path)) throw new InvalidOperationException("Invalid backup inspection.");
            return result;
        },null,ct);
    }
    public async Task PreviewAsync(CancellationToken ct=default)
    {
        if(!CanPreview || _backend is not { } backend || _actor is not { } actor ||
            Evidence is not { } evidence || ct.IsCancellationRequested) return;
        await RunAsync(null,async token=>
        {
            var result=await backend.PreviewBackupRestoreAsync(actor,evidence,token);
            if(result is null || !result.Matches(evidence)) throw new InvalidOperationException("Invalid backup comparison.");
            return result;
        },ct);
    }
    private async Task RunAsync(Func<CancellationToken,Task<NativeBackupInspection>>? inspect,
        Func<CancellationToken,Task<NativeRestoreComparison>>? preview,CancellationToken ct)
    {
        long generation=++_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _busy=true; Error=""; Comparison=null;
        if(inspect is not null) Evidence=null;
        Notify();
        try
        {
            if(inspect is not null)
            {
                var evidence=await inspect(pending.Token);
                if(generation==_generation && IsOpen && !pending.IsCancellationRequested) Evidence=evidence;
            }
            else if(preview is not null)
            {
                var comparison=await preview(pending.Token);
                if(generation==_generation && IsOpen && !pending.IsCancellationRequested) Comparison=comparison;
            }
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested)
        {
            if(generation==_generation) Evidence=null;
        }
        catch
        {
            if(generation==_generation && IsOpen)
            {
                Evidence=null; Comparison=null;
                Error="تعذر التحقق من النسخة أو تغير محتواها أو انتهت الجلسة. أعد فحص الملف. لم يتم تنفيذ الاستعادة.";
            }
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _busy=false;
                if(!IsOpen) ForceClear(); else Notify();
            }
        }
    }
    public bool Close()
    {
        if(!IsOpen) return false;
        ForceClear(); return true;
    }
    public void ForceClear()
    {
        ++_generation; _pending?.Cancel(); _pending=null; _backend=null; _actor=null; _liveActor=null;
        _busy=false; SelectedPath=Error=""; Evidence=null; Comparison=null; Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        InspectCommand.NotifyAvailabilityChanged(); PreviewCommand.NotifyAvailabilityChanged();
    }
}
