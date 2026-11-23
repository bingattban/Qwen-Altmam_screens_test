using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// One-person native WPF dismissal. Only the original PersonnelSeparationService
/// is allowed to preview or commit this audited, non-deleting transition.
/// </summary>
public sealed class NativePersonnelSeparationViewModel : INotifyPropertyChanged
{
    private INativePersonnelSeparationBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private NativePersonnelDisplayRow? _row;
    private CancellationTokenSource? _pending, _delayCts;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _delayElapsed;
    private string _sourceSiteId = "", _sourceSiteName = "", _reason = "", _error = "";
    private NativePersonnelSeparationDraft? _approvedDraft;
    private NativePersonnelSeparationPreview? _preview;
    private NativePersonnelSeparationReceipt? _receipt;

    public NativePersonnelSeparationViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool WaitingForConfirmation => _preview is not null && !_delayElapsed && !_attempted;
    public bool CanPreview => CanEdit && _backend is not null &&
        IsOriginalContextValid() && CurrentDraft() is not null;
    public bool CanConfirm => CanEdit && _delayElapsed && _preview is not null &&
        _approvedDraft is not null && CurrentDraft() == _approvedDraft &&
        IsOriginalContextValid();
    public string SelectedPersonName => _row?.Name ?? "";
    public string SelectedPersonNumber => _row?.ServiceNumberA ?? "";
    public string SourceSiteName => _sourceSiteName;
    public string Error => _error;
    public string ReviewMessage => _preview is null ? "" :
        $"ستتم عملية فصل «{_preview.Draft.Name}» من «{_preview.Draft.SourceSiteName}» بتاريخ {_preview.BusinessDate}، بسبب: {_preview.Draft.Reason}. يحفظ النظام سجلات الفرد وتاريخه ولا يحذفه.";
    public string CompletionMessage => _receipt is null ? "" :
        $"تم الفصل وحفظ قيد الحركة {_receipt.MovementId} بتاريخ {_receipt.BusinessDate}.";
    public NativePersonnelSeparationPreview? Preview => _preview;
    public NativePersonnelSeparationReceipt? Receipt => _receipt;
    public string FormReason
    {
        get => _reason;
        set
        {
            if (!CanEdit || _reason == value) return;
            _reason = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativePersonnelSeparationBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool> rowIsCurrent)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Personnel separation backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = rowIsCurrent ?? throw new ArgumentNullException(nameof(rowIsCurrent));
        Notify();
    }

    public void Open(NativePersonnelDisplayRow row, string sourceSiteId,
        IReadOnlyList<NativeDashboardSite> sites)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_backend is null || actor is null || row is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _uncertainActor == actor || sites is null ||
            sites.Count is < 1 or > 10000 ||
            string.IsNullOrWhiteSpace(sourceSiteId) ||
            sourceSiteId.Length > 128 || sourceSiteId.Any(char.IsControl) ||
            _onPage?.Invoke(row) != true ||
            !row.Active || row.StatusName != "على القوة" ||
            row.Revision < 1 || string.IsNullOrWhiteSpace(row.PersonId) ||
            string.IsNullOrWhiteSpace(row.Name) ||
            string.IsNullOrWhiteSpace(row.ServiceNumberA))
            return;
        var matching = sites.Where(site => site.SiteId == sourceSiteId).ToArray();
        if (matching.Length != 1 || matching[0].Name != row.SiteName)
            return;
        Reset();
        _row = row;
        _actor = actor;
        _open = true;
        _sourceSiteId = sourceSiteId;
        _sourceSiteName = matching[0].Name;
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null || _actor is not { } actor) return;
        NativePersonnelSeparationDraft? draft = CurrentDraft();
        if (draft is null) return;
        int version = ++_generation;
        _pending?.Cancel();
        CancelDelay();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _approvedDraft = null;
        _error = "";
        Notify();
        try
        {
            NativePersonnelSeparationPreview result =
                await _backend.PreviewSeparationAsync(actor, draft, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(result, draft);
            _preview = result;
            _approvedDraft = draft;
            _delayElapsed = false;
            _delayCts = new CancellationTokenSource();
            _ = UnlockApprovalAfterDelayAsync(version, result, _delayCts.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذرت معاينة الفصل؛ تغيّر الفرد أو موقعه أو حالة اليوم. لم تُحفظ أي حركة.";
        }
        finally
        {
            if (version == _generation)
            {
                _pending = null;
                _busy = false;
                Notify();
            }
        }
    }

    private async Task UnlockApprovalAfterDelayAsync(int version,
        NativePersonnelSeparationPreview expected, CancellationToken ct)
    {
        try
        {
            // A conservative client-side wait. The canonical SQLCipher service
            // independently enforces FIVE seconds from original token issuance.
            await Task.Delay(TimeSpan.FromSeconds(expected.MinimumConfirmationSeconds), ct);
            if (ct.IsCancellationRequested || version != _generation ||
                !ReferenceEquals(_preview, expected) || !IsOriginalContextValid())
                return;
            _delayElapsed = true;
            Notify();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || _backend is null || _preview is not { } preview ||
            _actor is not { } actor) return;
        int version = ++_generation;
        _pending?.Cancel();
        CancelDelay();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true;
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            NativePersonnelSeparationReceipt receipt =
                await _backend.CommitSeparationAsync(actor, preview, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (!receipt.Separated || receipt.PersonId != preview.Draft.PersonId ||
                receipt.BusinessDate != preview.BusinessDate ||
                string.IsNullOrWhiteSpace(receipt.MovementId) ||
                !receipt.MovementId.StartsWith("MOV-", StringComparison.Ordinal))
                throw new InvalidDataException("Canonical personnel separation receipt mismatch.");
            _receipt = receipt;
            _preview = null;
            _approvedDraft = null;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _preview = null;
                _approvedDraft = null;
                _error = "تعذّر إثبات نتيجة الفصل؛ قد تكون العملية حُفظت. راجع أرشيف الفصل وسجل العمليات قبل أي محاولة جديدة. لا يوجد إعادة إرسال تلقائية.";
            }
        }
        finally
        {
            if (version == _generation)
            {
                _pending = null;
                _busy = false;
                Notify();
            }
        }
    }

    public void Close()
    {
        if (!_open || _busy) return;
        bool completed = _receipt is not null;
        Reset();
        if (completed) CommittedAndClosed?.Invoke();
    }

    public void ForceCancel()
    {
        if (_attempted && _receipt is null && _actor is { } actor)
            _uncertainActor = actor;
        Reset();
    }

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        CancelDelay();
        _actor = null;
        _row = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _sourceSiteId = "";
        _sourceSiteName = "";
        _reason = "";
        _error = "";
        _approvedDraft = null;
        _preview = null;
        _receipt = null;
        Notify();
    }

    private void CancelDelay()
    {
        _delayCts?.Cancel();
        _delayCts?.Dispose();
        _delayCts = null;
        _delayElapsed = false;
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _row is { Active: true } row && row.StatusName == "على القوة" &&
        _onPage?.Invoke(row) == true;

    private NativePersonnelSeparationDraft? CurrentDraft()
    {
        if (_row is null || !IsOriginalContextValid()) return null;
        string reason = _reason.Trim();
        if (reason.Length is < 5 or > 1000 || reason.Any(char.IsControl))
            return null;
        return new NativePersonnelSeparationDraft(
            _row.PersonId, _row.Name, _row.ServiceNumberA,
            _row.Revision, _sourceSiteId, _sourceSiteName, reason);
    }

    private static void ValidatePreview(NativePersonnelSeparationPreview preview,
        NativePersonnelSeparationDraft draft)
    {
        string today = DateOnly.FromDateTime(DateTime.Now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (preview is null || preview.Draft != draft ||
            preview.BusinessDate != today ||
            preview.PreviewToken.Length != 64 ||
            preview.PreviewToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            preview.SourceAssignmentId.Length is < 3 or > 128 ||
            preview.MinimumConfirmationSeconds != 5)
            throw new InvalidDataException("Invalid canonical separation preview.");
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        CancelDelay();
        _approvedDraft = null;
        _preview = null;
        _error = "";
        Notify();
    }

    private void Notify()
    {
        foreach (string property in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit), nameof(CanPreview),
            nameof(CanConfirm), nameof(HasPreview), nameof(HasUnverifiedCommit),
            nameof(WaitingForConfirmation), nameof(SelectedPersonName),
            nameof(SelectedPersonNumber), nameof(SourceSiteName), nameof(FormReason),
            nameof(Error), nameof(ReviewMessage), nameof(CompletionMessage),
            nameof(Preview), nameof(Receipt)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
