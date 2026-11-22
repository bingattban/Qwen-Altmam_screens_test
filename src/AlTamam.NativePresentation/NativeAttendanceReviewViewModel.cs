using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace AlTamam.NativePresentation;

/// <summary>
/// Explicit, account-bound state machine:
/// choose one status -> obtain authoritative C# preview -> inspect exact
/// names/IDs/prior statuses -> human clicks Confirm -> idempotent C# commit.
/// No automatic retries, no new operationId after a possibly lost response.
/// </summary>
public sealed class NativeAttendanceReviewViewModel : INotifyPropertyChanged
{
    private INativeAttendanceReviewBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private DateOnly _businessDate;
    private string _site = "";
    private string[] _personIds = [];
    private IReadOnlyList<NativeAttendanceStatusChoice> _choices = [];
    private NativeAttendanceRevisionPreview? _preview;
    private string _selectedStatusId = "";
    private DateTime? _rangeStartDate;
    private DateTime? _rangeEndDate;
    private string _operationId = "";
    private string _error = "";
    private bool _open, _busy, _committing, _completed, _unverifiedCommit;
    private int _generation, _committedCount;
    private string _auditEventId = "";
    private CancellationTokenSource? _pending;

    public NativeAttendanceReviewViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(),
            _ => IsOpen && !IsBusy && !IsCompleted && !HasUnverifiedCommit &&
                 StatusChoices.Any(x => x.StatusId == SelectedStatusId));
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(),
            _ => IsOpen && HasPreview && !IsBusy && !IsCompleted);
        CancelCommand = new NativeCommand(_ => Cancel(),
            _ => IsOpen && !IsCommitting && !HasUnverifiedCommit);
    }

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool IsCommitting => _committing;
    public bool IsCompleted => _completed;
    public bool HasUnverifiedCommit => _unverifiedCommit;
    public bool HasPreview => _preview is not null;
    public string ErrorMessage => _error;
    public string AuditEventId => _auditEventId;
    public int CommittedCount => _committedCount;
    public int SelectedCount => _personIds.Length;
    public string SelectedStatusId
    {
        get => _selectedStatusId;
        set => ChooseStatus(value);
    }
    public bool RequiresDateRange =>
        _choices.Any(x => x.StatusId == _selectedStatusId && x.RequiresDateRange);
    public DateTime? RangeStartDate
    {
        get => _rangeStartDate;
        set => ChangeRangeDate(value, isStart: true);
    }
    public DateTime? RangeEndDate
    {
        get => _rangeEndDate;
        set => ChangeRangeDate(value, isStart: false);
    }
    public string ConfirmButtonText => _error.Contains("رد الاعتماد", StringComparison.Ordinal) ||
        _error.Contains("انقطع طلب الاعتماد", StringComparison.Ordinal)
        ? "إعادة تأكيد نفس العملية" : "تأكيد واعتماد التغييرات";
    public string TargetStatusName => _preview?.TargetStatusName ?? "";
    public string PreviewMessage => _preview is null ? "" :
        $"ستُغيّر حالة {_preview.Persons.Count} فرداً إلى {_preview.TargetStatusName}." +
        (_preview.DateRange is { } range
            ? $" المدة من {range.Start:yyyy-MM-dd} إلى {range.End:yyyy-MM-dd}."
            : "") +
        " راجع الأسماء والأرقام والمواقع والحالة السابقة قبل الاعتماد.";
    public IReadOnlyList<NativeAttendanceStatusChoice> StatusChoices => _choices;
    public IReadOnlyList<NativeAttendancePreviewPerson> PreviewPersons =>
        _preview?.Persons ?? [];
    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action<NativeAttendanceCommitReceipt>? Committed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachBackend(INativeAttendanceReviewBackend backend,
        Func<NativeIdentity?> currentActor)
    {
        if (_backend is not null) throw new InvalidOperationException("Attendance review backend already initialized.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = currentActor ?? throw new ArgumentNullException(nameof(currentActor));
    }

    public async Task OpenAsync(DateOnly date, string site,
        IReadOnlyList<NativeAutoRegisterRow> selected, CancellationToken ct = default)
    {
        if (_committing) return; // do not abandon an in-flight approved write
        Cancel();
        var actor = _liveActor?.Invoke();
        if (actor is null || _backend is null || selected is null ||
            selected.Count is < 1 or > 100 ||
            date != DateOnly.FromDateTime(DateTime.Now) ||
            selected.Any(x => string.IsNullOrWhiteSpace(x.PersonId)) ||
            selected.Select(x => x.PersonId).Distinct(StringComparer.Ordinal).Count() != selected.Count)
            return;

        _businessDate = date;
        _rangeStartDate = date.ToDateTime(TimeOnly.MinValue);
        _rangeEndDate = null;
        _site = site ?? "";
        _actor = actor;
        _personIds = selected.Select(x => x.PersonId)
            .Order(StringComparer.Ordinal).ToArray();
        _open = true;
        int version = ++_generation;
        _busy = true;
        Update();
        try
        {
            IReadOnlyList<NativeAttendanceStatusChoice> statuses =
                await _backend.ReadChoicesAsync(actor, ct);
            if (version != _generation || _liveActor?.Invoke() != actor) return;
            // A server status cannot be offered twice with conflicting labels.
            if (statuses.Count > 300 || statuses.Any(x =>
                    string.IsNullOrWhiteSpace(x.StatusId) ||
                    string.IsNullOrWhiteSpace(x.DisplayName)) ||
                statuses.Select(x => x.StatusId).Distinct(StringComparer.Ordinal).Count() != statuses.Count)
                throw new InvalidDataException("Untrusted attendance status choices.");
            _choices = statuses;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { Cancel(); }
        catch { if (version == _generation) _error = "تعذر تحميل حالات التحضير المعتمدة؛ لم يجر أي تعديل."; }
        finally
        {
            if (version == _generation)
            {
                _busy = false;
                Update();
            }
        }
    }

    public void ChooseStatus(string? id)
    {
        if (!_open || _busy || _committing || _unverifiedCommit || _completed) return;
        string selected = id ?? "";
        if (_selectedStatusId == selected) return;
        _selectedStatusId = _choices.Any(x => x.StatusId == selected) ? selected : "";
        _rangeStartDate = _businessDate.ToDateTime(TimeOnly.MinValue);
        _rangeEndDate = null;
        // Choosing a different status revokes the previous server preview.
        InvalidatePreview();
        _error = _selectedStatusId == "" && selected != ""
            ? "الحالة المختارة غير نشطة أو غير معتمدة؛ لم يتم إنشاء معاينة." : "";
        Update();
    }

    private void ChangeRangeDate(DateTime? date, bool isStart)
    {
        // A lost commit response is an unresolved write: dates and intent MUST remain frozen.
        if (!_open || _busy || _committing || _unverifiedCommit || _completed ||
            !RequiresDateRange) return;
        DateTime? normalized = date?.Date;
        if (isStart)
        {
            if (_rangeStartDate == normalized) return;
            _rangeStartDate = normalized;
        }
        else
        {
            if (_rangeEndDate == normalized) return;
            _rangeEndDate = normalized;
        }
        InvalidatePreview();
        _error = "";
        Update();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!_open || _busy || _unverifiedCommit || _backend is null ||
            _actor is not { } actor ||
            !_choices.Any(x => x.StatusId == _selectedStatusId)) return;
        if (!HasValidActor(actor)) { ForceCancel(); return; }
        NativeAttendanceDateRange? selectedRange = null;
        if (RequiresDateRange)
        {
            if (_rangeStartDate is not { } from || _rangeEndDate is not { } until)
            {
                _error = "حدد تاريخ البداية والنهاية للإجازة أو الإذن قبل المعاينة.";
                Update();
                return;
            }
            var start = DateOnly.FromDateTime(from);
            var end = DateOnly.FromDateTime(until);
            if (start != _businessDate || end < start ||
                end.DayNumber - start.DayNumber > 365)
            {
                _error = "يجب أن تبدأ المدة اليوم، وأن تكون النهاية بعد البداية أو مساوية لها، وبحد أقصى 366 يوماً.";
                Update();
                return;
            }
            selectedRange = new NativeAttendanceDateRange(start, end);
        }
        InvalidatePreview();
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        Update();
        try
        {
            NativeAttendanceRevisionPreview draft = await _backend.PreviewAsync(
                actor, _businessDate, _site, _personIds, _selectedStatusId,
                linked.Token, selectedRange);
            if (version != _generation || linked.IsCancellationRequested ||
                !HasValidActor(actor)) return;
            string[] returned = draft.Persons.Select(x => x.PersonId)
                .Order(StringComparer.Ordinal).ToArray();
            if (draft.BusinessDate != _businessDate ||
                draft.TargetStatusId != _selectedStatusId ||
                draft.DateRange != selectedRange ||
                draft.PreviewToken.Length < 32 ||
                draft.Persons.Count != _personIds.Length ||
                !returned.SequenceEqual(_personIds, StringComparer.Ordinal) ||
                draft.Persons.Any(x => string.IsNullOrWhiteSpace(x.Name) ||
                    string.IsNullOrWhiteSpace(x.ServiceNumberA) ||
                    string.IsNullOrWhiteSpace(x.SiteName)))
                throw new InvalidDataException("Canonical preview does not match selected personnel.");
            _preview = draft;
            // One immutable operation ID for this explicit approved intent.
            // This ID MUST survive an ambiguous network/bridge response.
            _operationId = Guid.NewGuid().ToString("N");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذر التحقق من معاينة الخادم أو تطابق الأفراد؛ لم يُحفظ أي تعديل.";
        }
        finally
        {
            if (version == _generation)
            {
                _pending = null;
                _busy = false;
                Update();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!_open || _busy || _backend is null || _actor is not { } actor ||
            _preview is not { } draft || _operationId.Length is < 16 or > 128 ||
            _completed) return;
        if (!HasValidActor(actor)) { ForceCancel(); return; }

        int version = ++_generation;
        _committing = true;
        _busy = true;
        _error = "";
        Update();
        try
        {
            // Only this explicit action makes the canonical audited write.
            // NEVER manufacture a new preview or operation ID on failure.
            NativeAttendanceCommitReceipt receipt = await _backend.CommitAsync(
                actor, draft.PreviewToken, _operationId, ct);
            if (version != _generation || !HasValidActor(actor)) return;
            if (receipt.Count != _personIds.Length ||
                string.IsNullOrWhiteSpace(receipt.AuditEventId))
                throw new InvalidDataException("Could not verify complete attendance commit receipt.");
            _completed = true;
            _unverifiedCommit = false;
            _committedCount = receipt.Count;
            _auditEventId = receipt.AuditEventId;
            _preview = null;
            _operationId = "";
            Committed?.Invoke(receipt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (version == _generation)
            {
                _unverifiedCommit = true;
                _error = "انقطع طلب الاعتماد، وقد تكون العملية نُفذت؛ راجع السجل. إعادة التأكيد ستستخدم نفس معرف العملية.";
            }
        }
        catch
        {
            if (version == _generation)
            {
                _unverifiedCommit = true;
                _error = "رد الاعتماد غير مؤكد، وقد تكون العملية حُفظت. لا تبدأ معاينة جديدة؛ أعد تأكيد نفس العملية فقط بعد مراجعة السجل.";
            }
        }
        finally
        {
            if (version == _generation)
            {
                _busy = false;
                _committing = false;
                Update();
            }
        }
    }

    public void Cancel()
    {
        if (_unverifiedCommit)
        {
            _error = "عملية الاعتماد غير مؤكدة؛ لا يمكن إلغاء المعاينة أو إصدار عملية جديدة. تحقق من السجل أو أعد تأكيد نفس العملية.";
            Update();
            return;
        }
        if (_committing)
        {
            _error = "يوجد اعتماد جار؛ لا يمكن إلغاء نافذة المراجعة حتى تظهر النتيجة.";
            Update();
            return;
        }
        ForceCancel();
    }

    public void ForceCancel()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _businessDate = default;
        _rangeStartDate = null;
        _rangeEndDate = null;
        _site = "";
        _personIds = [];
        _choices = [];
        _preview = null;
        _selectedStatusId = "";
        _operationId = "";
        _error = "";
        _committing = false;
        _busy = false;
        _open = false;
        _completed = false;
        _unverifiedCommit = false;
        _committedCount = 0;
        _auditEventId = "";
        Update();
    }

    private bool HasValidActor(NativeIdentity expected) =>
        _liveActor?.Invoke() == expected &&
        _businessDate == DateOnly.FromDateTime(DateTime.Now);

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _preview = null;
        _operationId = "";
    }

    private void Update()
    {
        foreach (string name in new[] {
            nameof(IsOpen), nameof(IsBusy), nameof(IsCommitting),
            nameof(IsCompleted), nameof(HasUnverifiedCommit),
            nameof(HasPreview), nameof(ErrorMessage),
            nameof(AuditEventId), nameof(CommittedCount), nameof(SelectedCount),
            nameof(SelectedStatusId), nameof(RequiresDateRange),
            nameof(RangeStartDate), nameof(RangeEndDate), nameof(TargetStatusName),
            nameof(PreviewMessage), nameof(StatusChoices), nameof(PreviewPersons),
            nameof(ConfirmButtonText)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand cmd in new[] { PreviewCommand, ConfirmCommand, CancelCommand })
            if (cmd is NativeCommand native) native.NotifyAvailabilityChanged();
    }
}
