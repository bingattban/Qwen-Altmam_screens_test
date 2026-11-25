using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed class NativeAdministrativeCatchUpReview : INotifyPropertyChanged
{
    private INativeAdministrativeCatchUpBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _busy, _uncertain;
    private string _date = "", _reason = "", _operationId = "";

    public NativeAdministrativeCatchUpReview()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanEdit);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => !_busy && Preview is not null && ContextValid());
        CloseCommand = new NativeCommand(_ => Close(), _ => CanClose);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }
    public NativeAdministrativeCatchUpPreview? Preview { get; private set; }
    public NativeAdministrativeCatchUpReceipt? Receipt { get; private set; }
    public bool IsOpen => _actor is not null;
    public bool HasPreview => Preview is not null;
    public bool IsBusy => _busy;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanEdit => IsOpen && !_busy && !_uncertain && Receipt is null && ContextValid();
    public bool CanClose => IsOpen && !_busy && !_uncertain;
    public string Error { get; private set; } = "";
    public string ConfirmLabel => _uncertain ? "إعادة تأكيد العملية نفسها" : "اعتماد استدراك اليوم";
    public string DateText { get => _date; set => Edit(ref _date,value); }
    public string Reason { get => _reason; set => Edit(ref _reason,value); }
    public string CompletionMessage => Receipt is { } result
        ? $"تم اعتماد الاستدراك: {result.AppliedCount} من {result.PendingBefore} حالة ناقصة. {result.Summary}"
        : "";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private bool ContextValid() => _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);

    public bool Open(INativeAdministrativeCatchUpBackend backend, Func<NativeIdentity?> liveActor)
    {
        var actor = liveActor();
        if (IsOpen || _busy || _uncertain || actor is null ||
            !RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role)) return false;
        _backend = backend;
        _liveActor = liveActor;
        _actor = actor;
        _date = Today.AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        Notify();
        return true;
    }

    private void Edit(ref string target,string? value)
    {
        if (!CanEdit || target == (value ?? "")) return;
        target = value ?? "";
        RevokePreview();
        Notify();
    }

    private void RevokePreview()
    {
        Preview = null;
        _operationId = "";
        Error = "";
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (_busy || _uncertain || Receipt is not null || _backend is null) return;
        if (!ContextValid()) { Close(); return; }
        RevokePreview();
        if (!DateOnly.TryParseExact(_date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
        {
            Error = "حدد التاريخ بصيغة yyyy-MM-dd.";
            Notify();
            return;
        }
        var request = new NativeAdministrativeCatchUpRequest(day,_reason.Trim());
        if (!request.IsValid(Today))
        {
            Error = "اختر يوما من الأيام السبعة السابقة مع سبب من 10 إلى 400 حرف.";
            Notify();
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation = ++_generation;
        _pending = linked;
        _busy = true;
        Notify();
        try
        {
            var answer = await _backend.PreviewCatchUpAsync(_actor!,request,linked.Token);
            if (generation != _generation || linked.IsCancellationRequested || !ContextValid()) return;
            if (!answer.Matches(request,Today)) throw new InvalidDataException("Mismatched catch-up preview.");
            Preview = answer;
            _operationId = Guid.NewGuid().ToString("N");
        }
        catch { if (generation == _generation) Error = "تعذرت المعاينة؛ تحقق من التاريخ والصلاحيات والحالات الناقصة."; }
        finally { Finish(generation); }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (_busy || _backend is null || Preview is not { } approved) return;
        if (!ContextValid())
        {
            if (!_uncertain) Close();
            else { Error = "تغير سياق الاعتماد؛ تحقق من السجل."; Notify(); }
            return;
        }
        if (ct.IsCancellationRequested) return;
        if (!_uncertain && !approved.Matches(approved.Request,Today))
        {
            RevokePreview();
            Error = "انتهت نافذة الاستدراك؛ أعد المعاينة.";
            Notify();
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation = ++_generation;
        _pending = linked;
        _busy = _uncertain = true;
        Notify();
        try
        {
            var result = await _backend.CommitCatchUpAsync(_actor!,approved,_operationId,linked.Token);
            if (generation != _generation || !ContextValid()) return;
            if (!approved.MatchesReceipt(result)) throw new InvalidDataException("Mismatched catch-up receipt.");
            Receipt = result;
            RevokePreview();
            _uncertain = false;
        }
        catch { if (generation == _generation) Error = "نتيجة الاعتماد غير مؤكدة؛ أعد تأكيد العملية نفسها فقط أو تحقق من السجل."; }
        finally { Finish(generation); }
    }

    private void Finish(int generation)
    {
        if (generation != _generation) return;
        _pending = null;
        _busy = false;
        Notify();
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
        _pending = null;
        _actor = null;
        _liveActor = null;
        _backend = null;
        _busy = _uncertain = false;
        _date = _reason = "";
        Receipt = null;
        RevokePreview();
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(string.Empty));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
