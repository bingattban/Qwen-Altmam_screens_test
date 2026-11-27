using System.ComponentModel;
using System.Globalization;
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
    private INativeAttendanceDraftWriter? _draftWriter;
    private bool _savingDraft,_draftAttempted;
    private string _savedDraftId="",_draftMessage="";
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor;
    private DateOnly _businessDate;
    private string _site = "";
    private string[] _personIds = [];
    private bool _initial;
    private IReadOnlyList<NativeAttendanceStatusChoice> _choices = [];
    private NativeAttendanceRevisionPreview? _preview;
    private string _selectedStatusId = "";
    private DateTime? _rangeStartDate, _rangeEndDate;
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
            _ => IsOpen && !IsCommitting && !HasUnverifiedCommit && !IsSavingDraft);
        SaveDraftCommand=new NativeCommand(_=>_ = SaveDraftAsync(),_=>CanSaveDraft);
    }

    public bool IsOpen => _open;
    public bool DraftSaveAvailable=>_draftWriter is not null;
    public bool IsSavingDraft=>_savingDraft;
    public string SavedDraftId=>_savedDraftId;
    public string DraftMessage=>_draftMessage;
    public bool CanSaveDraft=>_draftWriter is not null && _open && !_busy &&
        !_committing && !_completed && !_unverifiedCommit && !_draftAttempted &&
        _actor is { } actor && HasValidActor(actor) &&
        _choices.Any(x=>x.StatusId==_selectedStatusId) && TryGetDraftRange(out _) &&
        _personIds.Length is >=1 and <=100;
    public ICommand SaveDraftCommand {get;}
    public void AttachDraftWriter(INativeAttendanceDraftWriter writer)
    {
        if(_draftWriter is not null)throw new InvalidOperationException("Draft writer already attached.");
        _draftWriter=writer??throw new ArgumentNullException(nameof(writer));
        Update();
    }

    private bool TryGetDraftRange(out NativeAttendanceDraftRangeEvidence? evidence)
    {
        evidence=null;
        if(!NativeAttendanceDateRange.TryCreate(_selectedStatusId,_businessDate,
            _rangeStartDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            _rangeEndDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            out var range,out _))return false;
        if(range is not null)evidence=new(range.StartDate,range.EndDate,_initial);
        return true;
    }

    public async Task SaveDraftAsync(CancellationToken ct=default)
    {
        if(!CanSaveDraft || _draftWriter is not { } writer || _actor is not { } actor ||
            !TryGetDraftRange(out var evidence))return;
        var intent=new NativeAttendanceDraftIntent("DRF-"+Guid.NewGuid().ToString("N"),
            _businessDate,_site,_selectedStatusId,Array.AsReadOnly(_personIds.ToArray())){RangeEvidence=evidence};
        if(!intent.IsValid(DateOnly.FromDateTime(DateTime.Now)))return;
        int version=++_generation;
        _pending?.Cancel();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending=linked;_draftAttempted=true;_savingDraft=true;_busy=true;
        _savedDraftId="";_draftMessage="";Update();
        try
        {
            var receipt=await writer.SaveAttendanceDraftAsync(actor,intent,linked.Token);
            if(version!=_generation)return;
            if(!HasValidActor(actor)){ForceCancel();return;}
            if(linked.IsCancellationRequested || !intent.Matches(receipt))
                throw new InvalidDataException("Unverified draft save.");
            _savedDraftId=receipt.DraftId;
            _draftMessage="تم حفظ المسودة والتحقق منها. لم يعتمد أي حضور؛ يمكنك إغلاق المراجعة ثم فتح المسودات.";
        }
        catch
        {
            if(version==_generation)
            {
                if(!HasValidActor(actor)){ForceCancel();return;}
                _draftMessage="لم يتم التحقق من حفظ المسودة. راجع قائمة المسودات بعد إغلاق المراجعة؛ لا إعادة إرسال تلقائية.";
            }
        }
        finally
        {
            if(version==_generation){_pending=null;_savingDraft=false;_busy=false;Update();}
        }
    }

    public bool IsInitial => _initial;
    public string ReviewTitle => _initial ? "اعتماد التحضير الأولي" : "تعديل حالة التحضير";
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
    public bool RequiresDateRange => NativeAttendanceDateRange.RequiresRange(_selectedStatusId);
    public bool CanEditSelection => IsOpen && !IsBusy && !_completed && !_unverifiedCommit;
    public bool CanEditRange => CanEditSelection && RequiresDateRange;
    public DateTime? RangeStartDate
    {
        get => _rangeStartDate;
        set
        {
            if (!CanEditRange || _rangeStartDate == value) return;
            _rangeStartDate = value;
            InvalidatePreview();
            _error = "";
            Update();
        }
    }
    public DateTime? RangeEndDate
    {
        get => _rangeEndDate;
        set
        {
            if (!CanEditRange || _rangeEndDate == value) return;
            _rangeEndDate = value;
            InvalidatePreview();
            _error = "";
            Update();
        }
    }
    public string ConfirmButtonText => _error.Contains("رد الاعتماد", StringComparison.Ordinal) ||
        _error.Contains("انقطع طلب الاعتماد", StringComparison.Ordinal)
        ? "إعادة تأكيد نفس العملية" : "تأكيد واعتماد التغييرات";
    public string TargetStatusName => _preview?.TargetStatusName ?? "";
    public string PreviewMessage => _preview is null ? "" :
        (_initial ? $"سيُعتمد تحضير {_preview.Persons.Count} فرداً بالحالة {_preview.TargetStatusName}" :
            $"ستُغيّر حالة {_preview.Persons.Count} فرداً إلى {_preview.TargetStatusName}") +
        (_preview.RangeStartDate is not null
            ? $" خلال المدة من {_preview.RangeStartDate} إلى {_preview.RangeEndDate}. "
            : ". ") +
        "راجع الأسماء والأرقام والمواقع والحالة السابقة قبل الاعتماد.";
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

    // Preserve the auto-register entry point, but share the exact same
    // immutable, audited preview/commit pipeline with the daily roster.
    public Task OpenAsync(DateOnly date, string site,
        IReadOnlyList<NativeAutoRegisterRow> selected, CancellationToken ct = default)
        => OpenPersonsAsync(date, site,
            selected?.Select(row => row.PersonId).ToArray() ?? [], ct);

    public Task OpenPersonsAsync(DateOnly date, string site,
        IReadOnlyList<string> personIds, CancellationToken ct = default) =>
        OpenCoreAsync(date, site, personIds, false, ct);

    public Task OpenInitialPersonsAsync(DateOnly date, string site,
        IReadOnlyList<string> personIds, CancellationToken ct = default) =>
        OpenCoreAsync(date, site, personIds, true, ct);

    private async Task OpenCoreAsync(DateOnly date, string site,
        IReadOnlyList<string> personIds, bool initial, CancellationToken ct)
    {
        // Never replace an approved intent after a possibly lost commit reply.
        if (_committing || _unverifiedCommit) { Cancel(); return; }
        if (_savingDraft) return;
        Cancel();
        var actor = _liveActor?.Invoke();
        if (actor is null || _backend is null || personIds is null ||
            personIds.Count is < 1 or > 100 ||
            date != DateOnly.FromDateTime(DateTime.Now) ||
            personIds.Any(string.IsNullOrWhiteSpace) ||
            personIds.Distinct(StringComparer.Ordinal).Count() != personIds.Count)
            return;

        _initial = initial;
        _businessDate = date;
        _site = site ?? "";
        _actor = actor;
        _personIds = personIds
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
        // Date values cannot leak between status choices (even timed statuses).
        _rangeStartDate = null;
        _rangeEndDate = null;
        // Choosing a different status revokes the previous server preview.
        InvalidatePreview();
        _error = _selectedStatusId == "" && selected != ""
            ? "الحالة المختارة غير نشطة أو غير معتمدة؛ لم يتم إنشاء معاينة." : "";
        Update();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!_open || _busy || _unverifiedCommit || _backend is null ||
            _actor is not { } actor ||
            !_choices.Any(x => x.StatusId == _selectedStatusId)) return;
        if (!HasValidActor(actor)) { ForceCancel(); return; }
        if (!NativeAttendanceDateRange.TryCreate(_selectedStatusId, _businessDate,
                _rangeStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _rangeEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                out var range, out var rangeError))
        {
            _error = rangeError;
            Update();
            return;
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
            NativeAttendanceRevisionPreview draft = _initial
                ? await _backend.PreviewInitialAsync(actor, _businessDate, _site, _personIds,
                    _selectedStatusId, linked.Token, range)
                : await _backend.PreviewAsync(actor, _businessDate, _site, _personIds, _selectedStatusId,
                    linked.Token, range);
            if (version != _generation || linked.IsCancellationRequested ||
                !HasValidActor(actor)) return;
            string[] returned = draft.Persons.Select(x => x.PersonId)
                .Order(StringComparer.Ordinal).ToArray();
            if (draft.IsInitial != _initial ||
                (_initial && draft.Persons.Any(person => person.PreviousStatusId != "")) ||
                draft.BusinessDate != _businessDate ||
                draft.TargetStatusId != _selectedStatusId ||
                draft.RangeStartDate != range?.StartDate ||
                draft.RangeEndDate != range?.EndDate ||
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
            NativeAttendanceCommitReceipt receipt = _initial
                ? await _backend.CommitInitialAsync(actor, draft.PreviewToken, _operationId, ct)
                : await _backend.CommitAsync(actor, draft.PreviewToken, _operationId, ct);
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
        if (_savingDraft) return;
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
        _savingDraft=false;_draftAttempted=false;_savedDraftId="";_draftMessage="";
        _businessDate = default;
        _site = "";
        _personIds = [];
        _initial = false;
        _choices = [];
        _preview = null;
        _selectedStatusId = "";
        _rangeStartDate = null;
        _rangeEndDate = null;
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
            nameof(IsOpen), nameof(IsInitial), nameof(ReviewTitle), nameof(IsBusy), nameof(IsCommitting),
            nameof(IsCompleted), nameof(HasUnverifiedCommit),
            nameof(HasPreview), nameof(ErrorMessage),
            nameof(AuditEventId), nameof(CommittedCount), nameof(SelectedCount),
            nameof(SelectedStatusId), nameof(TargetStatusName),
            nameof(RequiresDateRange), nameof(CanEditSelection), nameof(CanEditRange),
            nameof(RangeStartDate), nameof(RangeEndDate),
            nameof(PreviewMessage), nameof(StatusChoices), nameof(PreviewPersons),
            nameof(ConfirmButtonText), nameof(DraftSaveAvailable), nameof(IsSavingDraft),
            nameof(SavedDraftId), nameof(DraftMessage), nameof(CanSaveDraft)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand cmd in new[] { PreviewCommand, ConfirmCommand, CancelCommand, SaveDraftCommand })
            if (cmd is NativeCommand native) native.NotifyAvailabilityChanged();
    }
}
