using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Review only the original canonical range operation, never a second writer.</summary>
public sealed class NativeHistoricalRangeReview : INotifyPropertyChanged
{
    private INativeHistoricalRangeBackend? _backend;
    private INativeClosedDayCorrectionBackend? _catalogue;
    private NativeIdentity? _actor;
    private NativeMonthlyCellDetails? _detail;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeMonthlyCellDetails?>? _currentDetail;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _busy, _uncertain;
    private string _from = "", _to = "", _status = "", _reason = "", _operationId = "";
    private IReadOnlyList<NativeAttendanceStatusChoice> _choices = [];

    public NativeHistoricalRangeReview()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanEdit && _choices.Any(x => x.StatusId == _status));
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => !_busy && Preview is not null && ContextValid());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public NativeHistoricalRangePreview? Preview { get; private set; }
    public NativeHistoricalRangeReceipt? Receipt { get; private set; }
    public bool IsOpen => _detail is not null;
    public bool IsBusy => _busy;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanEdit => IsOpen && !_busy && !_uncertain && Receipt is null && ContextValid();
    public bool CanClose => IsOpen && !_busy && !_uncertain;
    public string Error { get; private set; } = "";
    public string CompletionMessage { get; private set; } = "";
    public string ConfirmLabel => _uncertain ? "إعادة تأكيد العملية نفسها" : "تأكيد تعديل الفترة";
    public IReadOnlyList<NativeAttendanceStatusChoice> StatusChoices => _choices;
    public string FromText { get => _from; set => Edit(ref _from, value); }
    public string ToText { get => _to; set => Edit(ref _to, value); }
    public string SelectedStatusId { get => _status; set => Edit(ref _status, value); }
    public string Reason { get => _reason; set => Edit(ref _reason, value); }
    public string PreviewSummary => Preview is { } preview
        ? $"الأيام التي ستتغير: {preview.Days.Count} — الأيام غير المدرجة في المعاينة: {preview.SkippedDays}"
        : "";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private bool ContextValid() => _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role) &&
        _detail is { } detail && ReferenceEquals(_currentDetail?.Invoke(), detail) &&
        detail.BusinessDate <= Today;

    public bool Open(INativeHistoricalRangeBackend backend, INativeClosedDayCorrectionBackend catalogue,
        Func<NativeIdentity?> liveActor, Func<NativeMonthlyCellDetails?> currentDetail, NativeMonthlyCellDetails detail)
    {
        var actor = liveActor();
        if (IsOpen || _busy || _uncertain || actor is null ||
            !RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role) ||
            !ReferenceEquals(currentDetail(), detail) || detail.BusinessDate > Today ||
            !NativeHistoricalRangeRequest.IsIdentifier(detail.PersonId)) return false;
        _backend = backend;
        _catalogue = catalogue;
        _actor = actor;
        _detail = detail;
        _liveActor = liveActor;
        _currentDetail = currentDetail;
        _from = _to = detail.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Notify();
        return true;
    }

    private void Edit(ref string target, string? value)
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

    public async Task LoadChoicesAsync(CancellationToken ct = default)
    {
        if (!CanEdit || _catalogue is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation = ++_generation;
        _pending = linked;
        _busy = true;
        _choices = [];
        _status = "";
        RevokePreview();
        Notify();
        try
        {
            var choices = await _catalogue.ReadCorrectionChoicesAsync(_actor!, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested || !ContextValid()) return;
            if (choices is null || choices.Count > 300 ||
                choices.Any(x => x is null || !NativeHistoricalRangeRequest.IsIdentifier(x.StatusId) ||
                    string.IsNullOrWhiteSpace(x.DisplayName)) ||
                choices.Select(x => x.StatusId).Distinct(StringComparer.Ordinal).Count() != choices.Count)
                throw new InvalidDataException("Invalid range correction status catalogue.");
            _choices = Array.AsReadOnly(choices.ToArray());
        }
        catch { if (generation == _generation) Error = "تعذر تحميل الحالات المعتمدة للتعديل."; }
        finally { Finish(generation); }
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (_busy || _uncertain || Receipt is not null || _backend is null) return;
        if (!ContextValid()) { Close(); return; }
        RevokePreview();
        if (!DateOnly.TryParseExact(_from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(_to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) ||
            !_choices.Any(x => x.StatusId == _status))
        {
            Error = "حدد تاريخي البداية والنهاية بصيغة yyyy-MM-dd واختر حالة معتمدة.";
            Notify();
            return;
        }
        var request = new NativeHistoricalRangeRequest(_detail!.PersonId, from, to, _status, _reason.Trim());
        if (!request.IsValid(Today))
        {
            Error = "الفترة من يوم إلى 62 يوما دون تواريخ مستقبلية؛ السبب من 10 إلى 400 حرف.";
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
            var response = await _backend.PreviewRangeAsync(_actor!, request, linked.Token);
            if (generation != _generation || linked.IsCancellationRequested || !ContextValid()) return;
            if (!response.Matches(request, Today))
                throw new InvalidDataException("Mismatched canonical historical range preview.");
            // Historical names/numbers can differ across the selected dates;
            // immutable PersonId is the authority. Preserve canonical identity and day snapshots.
            Preview = response with { Days = Array.AsReadOnly(response.Days.ToArray()) };
            _operationId = Guid.NewGuid().ToString("N");
        }
        catch { if (generation == _generation) Error = "تعذرت معاينة الفترة؛ تحقق من السجل والحالة والصلاحيات."; }
        finally { Finish(generation); }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (_busy || _backend is null || Preview is not { } approved) return;
        if (!ContextValid())
        {
            if (!_uncertain) Close();
            else { Error = "تغير سياق الاعتماد؛ تحقق من السجل قبل أي محاولة أخرى."; Notify(); }
            return;
        }
        if (ct.IsCancellationRequested) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int generation = ++_generation;
        _pending = linked;
        _busy = true;
        _uncertain = true;
        Notify();
        try
        {
            var result = await _backend.CommitRangeAsync(_actor!, approved, _operationId, linked.Token);
            if (generation != _generation || !ContextValid()) return;
            if (!approved.MatchesReceipt(result))
                throw new InvalidDataException("Mismatched historical range receipt.");
            Receipt = result;
            CompletionMessage = $"تم اعتماد تعديل {approved.Days.Count} يوم؛ أغلق المراجعة لتحديث الكشف.";
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
        _backend = null;
        _catalogue = null;
        _actor = null;
        _detail = null;
        _liveActor = null;
        _currentDetail = null;
        _busy = _uncertain = false;
        _choices = [];
        _from = _to = _status = _reason = "";
        Receipt = null;
        CompletionMessage = "";
        RevokePreview();
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
    }
}
