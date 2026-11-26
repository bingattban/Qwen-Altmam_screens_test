using System.ComponentModel;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeManualBackupReceipt(string BackupId,string Path,string Sha256,long FileSize,bool Verified)
{
    public static bool ValidDirectory(string? directory)=>directory is { Length:>0 and <=600 } &&
        !directory.Any(char.IsControl) && System.IO.Path.IsPathFullyQualified(directory);
    public static bool SameDirectory(string? left,string? right)
    {
        if(!ValidDirectory(left) || !ValidDirectory(right)) return false;
        try
        {
            return string.Equals(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(left!)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(right!)),StringComparison.OrdinalIgnoreCase);
        }
        catch(ArgumentException) { return false; }
        catch(NotSupportedException) { return false; }
        catch(IOException) { return false; }
    }
    public bool Matches(string directory)=>Verified && FileSize>0 &&
        BackupId is { Length:36 } && BackupId.StartsWith("BKP-",StringComparison.Ordinal) &&
        Guid.TryParseExact(BackupId[4..],"N",out _) &&
        Sha256 is { Length:64 } && Sha256.All(Uri.IsHexDigit) &&
        Path is { Length:>0 and <=1024 } && !Path.Any(char.IsControl) &&
        System.IO.Path.IsPathFullyQualified(Path) &&
        SameDirectory(System.IO.Path.GetDirectoryName(Path),directory) &&
        System.IO.Path.GetFileName(Path).StartsWith("AlTamam-",StringComparison.Ordinal) &&
        System.IO.Path.GetFileName(Path).EndsWith("-"+BackupId+".dbbackup",StringComparison.Ordinal);
}

public sealed class NativeBackupRejectedException:Exception
{
    public NativeBackupRejectedException():base("Backup operation rejected before execution.") { }
}
public interface INativeManualBackupBackend
{
    Task<NativeManualBackupReceipt> CreateManualBackupAsync(NativeIdentity actor,string directory,CancellationToken ct=default);
    Task<string> SaveBackupDirectoryAsync(NativeIdentity actor,string directory,CancellationToken ct=default);
}

public sealed class NativeManualBackupReview : INotifyPropertyChanged
{
    private INativeManualBackupBackend? _backend;
    private NativeIdentity? _actor;
    private NativeSettingsOverview? _snapshot;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeSettingsOverview?>? _current;
    private long _generation;
    private bool _busy,_uncertain;
    public NativeManualBackupReview()
    {
        ConfirmCommand=new NativeCommand(_=>_ = ConfirmAsync(),_=>CanConfirm);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeCommand ConfirmCommand { get; }
    public bool IsOpen=>_actor is not null && _actor==_liveActor?.Invoke();
    private bool ContextValid()=>IsOpen && _snapshot is not null && ReferenceEquals(_current?.Invoke(),_snapshot);
    public bool IsBusy=>IsOpen && _busy;
    public bool HasUnverifiedResult=>IsOpen && _uncertain;
    public bool IsFolderChange { get; private set; }
    public string Directory { get; private set; }="";
    public string Error { get; private set; }="";
    public string? SavedDirectory { get; private set; }
    public NativeManualBackupReceipt? Receipt { get; private set; }
    public bool NeedsReload { get; private set; }
    public bool HasReceipt=>Receipt is not null;
    public bool CanConfirm=>ContextValid() && !_busy && !_uncertain && !NeedsReload && Receipt is null && SavedDirectory is null;
    public bool CanClose=>IsOpen && !_busy && !_uncertain;
    public string ReviewTitle=>IsFolderChange?"مراجعة تغيير مجلد الحفظ":"مراجعة إنشاء نسخة احتياطية يدوية";
    public string EffectLabel=>IsFolderChange
        ?"سيتم تغيير المجلد المشترك للنسخ وتقارير Excel وتفعيل إعداد النسخ التلقائي وفق السلوك الأصلي. قد يبدأ النسخ عند حلول الموعد أثناء تشغيل البرنامج؛ لا يعني الحفظ نجاح نسخة تلقائية."
        :"سيتم إنشاء نسخة SQLCipher مشفرة في المجلد المحدد، والتحقق من سلامتها وبصمتها وتسجيلها في سجل العمليات. لا يتم استعادة أو حذف البيانات.";
    public string CompletionMessage=>SavedDirectory is not null?"تم حفظ المجلد المعتمد.":Receipt is not null?"تم إنشاء نسخة مشفرة والتحقق منها وتسجيلها.":"";

    public bool Open(INativeManualBackupBackend backend,NativeIdentity actor,NativeSettingsOverview snapshot,
        Func<NativeIdentity?> liveActor,Func<NativeSettingsOverview?> current,string? selectedDirectory=null)
    {
        if(_actor is not null || liveActor()!=actor || !ReferenceEquals(current(),snapshot) ||
            !snapshot.Matches(actor) || !RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role) ||
            (selectedDirectory is null && !snapshot.DirectoryAvailable) ||
            !NativeManualBackupReceipt.ValidDirectory(selectedDirectory??snapshot.BackupDirectory)) return false;
        _backend=backend; _actor=actor; _snapshot=snapshot; _liveActor=liveActor; _current=current;
        IsFolderChange=selectedDirectory is not null;
        Directory=selectedDirectory??snapshot.BackupDirectory;
        Notify(); return true;
    }
    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if(!CanConfirm || _backend is null || _actor is not { } actor || ct.IsCancellationRequested) return;
        long generation=++_generation;
        _busy=true; _uncertain=true; Error=""; Notify();
        try
        {
            if(IsFolderChange)
            {
                string saved=await _backend.SaveBackupDirectoryAsync(actor,Directory,ct);
                if(generation!=_generation || !ContextValid()) return;
                if(!NativeManualBackupReceipt.SameDirectory(saved,Directory))
                    throw new InvalidOperationException("Unverified directory receipt.");
                SavedDirectory=saved;
            }
            else
            {
                var result=await _backend.CreateManualBackupAsync(actor,Directory,ct);
                if(generation!=_generation || !ContextValid()) return;
                if(result is null || !result.Matches(Directory))
                    throw new InvalidOperationException("Unverified backup receipt.");
                Receipt=result;
            }
            _uncertain=false;
        }
        catch(NativeBackupRejectedException)
        {
            if(generation==_generation && ContextValid())
            {
                _uncertain=false; NeedsReload=true;
                Error="رفضت الخدمة المسار قبل التنفيذ. أغلق المراجعة لتحديث الإعدادات.";
            }
        }
        catch
        {
            if(generation==_generation && ContextValid())
                Error="النتيجة غير مؤكدة. لا تكرر العملية. سجل الخروج ثم تحقق من المجلد وسجل العمليات قبل أي محاولة جديدة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _busy=false;
                if(!IsOpen) ForceClear(); else Notify();
            }
        }
    }
    public bool Close()
    {
        if(!CanClose) return false;
        ForceClear(); return true;
    }
    public void ForceClear()
    {
        ++_generation; _backend=null; _actor=null; _snapshot=null; _liveActor=null; _current=null;
        _busy=_uncertain=NeedsReload=IsFolderChange=false; Directory=Error=""; SavedDirectory=null; Receipt=null;
        Notify();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        ConfirmCommand.NotifyAvailabilityChanged();
    }
}
