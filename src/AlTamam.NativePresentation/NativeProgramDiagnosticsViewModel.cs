using System.ComponentModel;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeProgramInformation(string Version,string Architecture,string Runtime,bool Offline,int SchemaVersion)
{
    public bool IsValid=>!string.IsNullOrWhiteSpace(Version) && Version!="unknown" &&
        Version.Length<=128 && !Version.Any(char.IsControl) &&
        Architecture is "X86" or "X64" or "Arm64" or "Arm" &&
        System.Version.TryParse(Runtime,out _) && Offline && SchemaVersion>0;
    public string OfflineLabel=>Offline?"محلي — دون اتصال بالإنترنت":"غير متاح";
}

public enum NativeIntegrityStatus { Passed,Failed }

public interface INativeProgramDiagnosticsBackend
{
    Task<NativeProgramInformation> ReadProgramInformationAsync(NativeIdentity actor,CancellationToken ct=default);
    Task<NativeIntegrityStatus> CheckDatabaseIntegrityAsync(NativeIdentity actor,CancellationToken ct=default);
}

public sealed class NativeProgramDiagnosticsViewModel : INotifyPropertyChanged
{
    private INativeProgramDiagnosticsBackend? _backend;
    private Func<NativeIdentity?>? _actor;
    private Func<bool>? _canRun;
    private NativeIdentity? _bound;
    private NativeProgramInformation? _information;
    private NativeIntegrityStatus? _integrity;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _busy;
    private string _error="";
    public NativeProgramDiagnosticsViewModel()
    {
        RefreshCommand=new NativeCommand(_=>_ = RefreshAsync(),_=>CanRun);
        CheckCommand=new NativeCommand(_=>_ = CheckAsync(),_=>CanRun && Information is not null);
    }
    private NativeIdentity? Authorized=>_actor?.Invoke() is { } actor &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role)?actor:null;
    public bool CanRun=>_backend is not null && Authorized is not null && !_busy && (_canRun?.Invoke()??true);
    public bool IsBusy=>_busy;
    public NativeProgramInformation? Information=>_bound is not null && _bound==Authorized?_information:null;
    public bool HasInformation=>Information is not null;
    public NativeIntegrityStatus? Integrity=>Information is not null?_integrity:null;
    public string IntegrityLabel=>Integrity switch
    {
        NativeIntegrityStatus.Passed=>"اجتاز فحص سلامة قاعدة البيانات",
        NativeIntegrityStatus.Failed=>"فشل فحص سلامة قاعدة البيانات",
        _=>"لا توجد نتيجة فحص مؤكدة"
    };
    public string Error=>Authorized is not null?_error:"";
    public NativeCommand RefreshCommand { get; }
    public NativeCommand CheckCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Attach(INativeProgramDiagnosticsBackend backend,Func<NativeIdentity?> actor,Func<bool> canRun)
    {
        if(_backend is not null) throw new InvalidOperationException("Diagnostics already attached.");
        _backend=backend; _actor=actor; _canRun=canRun; Notify();
    }
    public Task RefreshAsync(CancellationToken ct=default)=>RunAsync(false,ct);
    public Task CheckAsync(CancellationToken ct=default)=>RunAsync(true,ct);
    private async Task RunAsync(bool check,CancellationToken ct)
    {
        if(!CanRun || (check && Information is null) || _backend is not { } backend ||
            Authorized is not { } actor) return;
        long generation=++_generation;
        using var pending=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=pending; _busy=true; _error=""; _integrity=null;
        if(!check) { _information=null; _bound=null; }
        Notify();
        try
        {
            if(check)
            {
                var result=await backend.CheckDatabaseIntegrityAsync(actor,pending.Token);
                if(generation!=_generation || pending.IsCancellationRequested || Authorized!=actor) return;
                if(!Enum.IsDefined(result)) throw new InvalidOperationException("Invalid integrity result.");
                _integrity=result;
            }
            else
            {
                var info=await backend.ReadProgramInformationAsync(actor,pending.Token);
                if(generation!=_generation || pending.IsCancellationRequested || Authorized!=actor) return;
                if(info is null || !info.IsValid) throw new InvalidOperationException("Invalid program information.");
                _bound=actor; _information=info;
            }
        }
        catch(OperationCanceledException) when(pending.IsCancellationRequested) { }
        catch
        {
            if(generation==_generation && Authorized==actor)
                _error="تعذر التحقق من المعلومات أو إتمام الفحص. أعد المحاولة.";
        }
        finally
        {
            if(generation==_generation)
            {
                _pending=null; _busy=false; Notify();
            }
        }
    }
    public void Clear()
    {
        ++_generation; _pending?.Cancel(); _pending=null; _bound=null;
        _information=null; _integrity=null; _error=""; _busy=false; Notify();
    }
    public void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        NotifyAvailability();
    }
    public void NotifyAvailability()
    {
        RefreshCommand.NotifyAvailabilityChanged(); CheckCommand.NotifyAvailabilityChanged();
    }
}
