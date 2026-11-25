using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeClosedDayPolicyRequest(bool Enabled, string Reason)
{
    public bool IsValid => Reason is not null && Reason.Trim() == Reason &&
        (Reason.Length == 0 || (Reason.Length is >= 10 and <= 400 && !Reason.Any(char.IsControl)));
}

public sealed record NativeClosedDayPolicyReceipt(bool Enabled);

public interface INativeClosedDayPolicyBackend
{
    Task<NativeClosedDayPolicyReceipt> SetClosedDayPolicyAsync(NativeIdentity actor,
        NativeClosedDayPolicyRequest approved,string operationId,CancellationToken ct=default);
}

/// <summary>Local review of an explicit setting intent, not a server preview or policy writer.</summary>
public sealed class NativeClosedDayPolicyReview : INotifyPropertyChanged
{
    private INativeClosedDayPolicyBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _enabled, _busy, _uncertain;
    private string _reason="", _operationId="";

    public NativeClosedDayPolicyReview()
    {
        PrepareCommand = new NativeCommand(_ => Prepare(),_ => CanEdit);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(),_ => !_busy && Approved is not null && ContextValid());
        CloseCommand = new NativeCommand(_ => Close(),_ => CanClose);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand PrepareCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }
    public NativeClosedDayPolicyRequest? Approved { get; private set; }
    public NativeClosedDayPolicyReceipt? Receipt { get; private set; }
    public bool IsOpen => _actor is not null;
    public bool IsBusy => _busy;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanEdit => IsOpen && !_busy && !_uncertain && Receipt is null && ContextValid();
    public bool CanClose => IsOpen && !_busy && !_uncertain;
    public string Error { get; private set; }="";
    public string TargetLabel => _enabled ? "تفعيل إذن التصحيح الاستثنائي" : "إيقاف إذن التصحيح الاستثنائي";
    public string ConfirmLabel => _uncertain ? "إعادة تأكيد العملية نفسها" : "تأكيد تغيير الإذن";
    public string CompletionMessage => Receipt is { } receipt
        ? (receipt.Enabled ? "أكد الخادم عملية تفعيل الإذن." : "أكد الخادم عملية إيقاف الإذن.")
        : "";
    public string Reason
    {
        get => _reason;
        set
        {
            if (!CanEdit || _reason == (value ?? "")) return;
            _reason = value ?? "";
            RevokeIntent();
            Notify();
        }
    }

    private bool ContextValid() => _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);

    public bool Open(INativeClosedDayPolicyBackend backend,Func<NativeIdentity?> liveActor,bool enabled)
    {
        var actor=liveActor();
        if (IsOpen || _busy || _uncertain || actor is null ||
            !RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role)) return false;
        _backend=backend;
        _liveActor=liveActor;
        _actor=actor;
        _enabled=enabled;
        Notify();
        return true;
    }

    public void Prepare()
    {
        if (!CanEdit) return;
        RevokeIntent();
        var request=new NativeClosedDayPolicyRequest(_enabled,_reason.Trim());
        if (!request.IsValid)
            Error="الملاحظة اختيارية؛ إذا أدخلت ملاحظة يجب أن تكون من 10 إلى 400 حرف.";
        else
        {
            Approved=request;
            _operationId=Guid.NewGuid().ToString("N");
        }
        Notify();
    }

    public async Task ConfirmAsync(CancellationToken ct=default)
    {
        if (_busy || _backend is null || Approved is not { } approved) return;
        if (!ContextValid())
        {
            if (!_uncertain) Close();
            else { Error="تغير سياق الاعتماد؛ تحقق من سجل العمليات."; Notify(); }
            return;
        }
        if (ct.IsCancellationRequested) return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation=++_generation;
        _pending=linked;
        _busy=_uncertain=true;
        Notify();
        try
        {
            var result=await _backend.SetClosedDayPolicyAsync(_actor!,approved,_operationId,linked.Token);
            if (generation != _generation || !ContextValid()) return;
            if (result.Enabled != approved.Enabled)
                throw new InvalidDataException("Canonical policy receipt mismatch.");
            Receipt=result;
            RevokeIntent();
            _uncertain=false;
        }
        catch { if (generation == _generation) Error="نتيجة تغيير الإذن غير مؤكدة؛ أعد تأكيد العملية نفسها فقط."; }
        finally
        {
            if (generation == _generation)
            {
                _pending=null;
                _busy=false;
                Notify();
            }
        }
    }

    private void RevokeIntent()
    {
        Approved=null;
        _operationId="";
        Error="";
    }

    public bool Close()
    {
        if (_busy || _uncertain) return false;
        ForceClear();
        return true;
    }

    public void ForceClear()
    {
        ++_generation;
        _pending?.Cancel();
        _pending=null;
        _backend=null;
        _actor=null;
        _liveActor=null;
        _enabled=_busy=_uncertain=false;
        _reason="";
        Receipt=null;
        RevokeIntent();
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        ((NativeCommand)PrepareCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
