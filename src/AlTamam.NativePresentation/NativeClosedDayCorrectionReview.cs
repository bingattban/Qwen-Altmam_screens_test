using System.ComponentModel;
using System.IO;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeClosedDayCorrectionRequest(
    string PersonId, DateOnly BusinessDate, string StatusId, string Reason);

public sealed record NativeClosedDayCorrectionPreview(
    NativeClosedDayCorrectionRequest Request, string Token, long PreviousRevision,
    string PersonName, string ServiceNumberA, string SiteName,
    string BeforeStatusId, string BeforeStatusName, string AfterStatusName,
    bool RequiresExplicitConfirmation);

public sealed record NativeClosedDayCorrectionReceipt(
    string PersonId, DateOnly BusinessDate, long RevisionNumber);

public interface INativeClosedDayCorrectionBackend
{
    Task<NativeClosedDayCorrectionPreview> PreviewAsync(NativeIdentity actor,
        NativeClosedDayCorrectionRequest request, CancellationToken ct = default);
    Task<NativeClosedDayCorrectionReceipt> CommitAsync(NativeIdentity actor,
        string token, string operationId, CancellationToken ct = default);
}

/// <summary>
/// Single saved historical day only. The canonical service must enforce
/// the enabled policy, closure and persisted assignment/revision checks.
/// Not a policy switch, range correction or independent database writer.
/// </summary>
public sealed class NativeClosedDayCorrectionReview : INotifyPropertyChanged
{
    private INativeClosedDayCorrectionBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativeMonthlyCellDetails?>? _currentDetail;
    private NativeIdentity? _actor;
    private NativeMonthlyCellDetails? _detail;
    private string _operationId = "";
    private bool _busy;
    private bool _uncertain;
    private int _generation;
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativeClosedDayCorrectionPreview? Preview { get; private set; }
    public NativeClosedDayCorrectionReceipt? Receipt { get; private set; }
    public string Error { get; private set; } = "";
    public bool IsOpen => _detail is not null;
    public bool IsBusy => _busy;
    public bool HasUnverifiedCommit => _uncertain;

    private bool ContextValid() =>
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role) &&
        _detail is { } detail && ReferenceEquals(_currentDetail?.Invoke(), detail) &&
        detail.BusinessDate < DateOnly.FromDateTime(DateTime.Now);

    public bool Open(INativeClosedDayCorrectionBackend backend,
        Func<NativeIdentity?> liveActor, Func<NativeMonthlyCellDetails?> currentDetail,
        NativeMonthlyCellDetails detail)
    {
        var actor = liveActor();
        if (IsOpen || _busy || _uncertain || actor is null ||
            !RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role) ||
            !ReferenceEquals(currentDetail(), detail) ||
            detail.BusinessDate >= DateOnly.FromDateTime(DateTime.Now) ||
            !ValidId(detail.PersonId) || string.IsNullOrWhiteSpace(detail.AttendanceCommittedAtUtc) ||
            string.IsNullOrWhiteSpace(detail.AttendanceStatus))
            return false;
        _backend = backend;
        _liveActor = liveActor;
        _currentDetail = currentDetail;
        _actor = actor;
        _detail = detail;
        Notify();
        return true;
    }

    private static bool ValidId(string value) => value.Length is >= 1 and <= 128 &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    public async Task PreviewAsync(string statusId, string reason, CancellationToken ct = default)
    {
        if (_busy || _uncertain || Receipt is not null || _backend is null) return;
        if (!ContextValid()) { Close(); return; }
        Preview = null;
        _operationId = "";
        Error = "";
        reason = reason.Trim();
        if (!ValidId(statusId) || reason.Length is < 10 or > 400 || reason.Any(char.IsControl))
        {
            Error = "اختر حالة معتمدة وأدخل سببا واضحا من 10 إلى 400 حرف.";
            Notify();
            return;
        }
        var detail = _detail!;
        var request = new NativeClosedDayCorrectionRequest(detail.PersonId,
            detail.BusinessDate, statusId, reason);
        int generation = ++_generation;
        _busy = true;
        Notify();
        try
        {
            var response = await _backend.PreviewAsync(_actor!, request, ct);
            if (generation != _generation || !ContextValid()) return;
            ct.ThrowIfCancellationRequested();
            if (response.Request != request || response.Token.Length != 64 ||
                !response.Token.All(Uri.IsHexDigit) ||
                response.PreviousRevision is < 1 or long.MaxValue ||
                !response.RequiresExplicitConfirmation ||
                response.PersonName != detail.PersonName ||
                response.ServiceNumberA != detail.ServiceNumberA ||
                response.SiteName != detail.AttendanceSiteName ||
                response.BeforeStatusName != detail.AttendanceStatus ||
                !ValidId(response.BeforeStatusId) ||
                response.BeforeStatusId == statusId ||
                string.IsNullOrWhiteSpace(response.AfterStatusName))
                throw new InvalidDataException("Mismatched canonical closed-day preview.");
            Preview = response;
            _operationId = Guid.NewGuid().ToString("N");
        }
        catch
        {
            if (generation == _generation)
                Error = "تعذرت معاينة التصحيح؛ تحقق من السياسة وإغلاق اليوم وسلامة السجل.";
        }
        finally
        {
            if (generation == _generation)
            {
                _busy = false;
                Notify();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (_busy || _backend is null || Preview is not { } preview) return;
        if (!ContextValid())
        {
            if (!_uncertain) Close();
            else { Error = "تغير سياق الاعتماد؛ تحقق من السجل قبل أي محاولة أخرى."; Notify(); }
            return;
        }
        if (ct.IsCancellationRequested) return;
        int generation = ++_generation;
        _busy = true;
        _uncertain = true;
        Notify();
        try
        {
            var result = await _backend.CommitAsync(_actor!, preview.Token, _operationId, ct);
            if (generation != _generation || !ContextValid()) return;
            if (result.PersonId != preview.Request.PersonId ||
                result.BusinessDate != preview.Request.BusinessDate ||
                result.RevisionNumber != preview.PreviousRevision + 1)
                throw new InvalidDataException("Mismatched closed-day receipt.");
            Receipt = result;
            Preview = null;
            _operationId = "";
            _uncertain = false;
            Error = "";
        }
        catch
        {
            if (generation == _generation)
                Error = "نتيجة الاعتماد غير مؤكدة؛ راجع السجل أو أعد تأكيد العملية نفسها فقط.";
        }
        finally
        {
            if (generation == _generation)
            {
                _busy = false;
                Notify();
            }
        }
    }

    public bool Close()
    {
        if (_busy || _uncertain) return false;
        ++_generation;
        _backend = null;
        _actor = null;
        _detail = null;
        _liveActor = null;
        _currentDetail = null;
        Preview = null;
        Receipt = null;
        _operationId = "";
        Error = "";
        Notify();
        return true;
    }

    private void Notify() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
