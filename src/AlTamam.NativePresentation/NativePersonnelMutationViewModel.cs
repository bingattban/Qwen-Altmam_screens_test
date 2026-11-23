using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Human-approved identity creation/edit. No SQL, no stand-in identity,
/// no automatic write or retry after an ambiguous response. All successful
/// writes are delegated to canonical SQLCipher services and audited there.
/// </summary>
public sealed class NativePersonnelMutationViewModel : INotifyPropertyChanged
{
    private INativePersonnelMutationBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private CancellationTokenSource? _pending;
    private NativeIdentity? _actor, _unverifiedActor;
    private NativePersonnelDisplayRow? _selectedRow;
    private NativePersonnelMutationDraft? _approvedDraft;
    private NativePersonnelMutationPreview? _preview;
    private NativePersonnelMutationReceipt? _receipt;
    private bool _open, _create, _busy, _writeAttempted, _unverified;
    private int _generation;
    private string _name = "", _number = "", _site = "", _reason = "", _error = "";
    private IReadOnlyList<NativeDashboardSite> _sites = [];

    public NativePersonnelMutationViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !_busy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsCreate => _create;
    public bool IsEdit => _open && !_create;
    public bool IsBusy => _busy;
    public bool HasPreview => _preview is not null;
    public bool HasReceipt => _receipt is not null;
    public bool HasUnverifiedCommit => _unverified;
    public bool CanConfirm => _open && !_busy && !_writeAttempted &&
        _preview is { RequiresIdentityResolution: false, CandidateCount: 0 } &&
        _approvedDraft is not null && CurrentDraft() == _approvedDraft &&
        IsOriginalContextValid();
    public bool CanPreview => _open && !_busy && !_writeAttempted &&
        _backend is not null && IsOriginalContextValid();
    public bool CanEdit => _open && !_busy && !_writeAttempted;
    public string Header => _create ? "إضافة فرد إلى سجل القوة" : "تعديل الاسم والرقم (ع)";
    public string Error => _error;
    public NativePersonnelMutationPreview? Preview => _preview;
    public NativePersonnelMutationReceipt? Receipt => _receipt;
    public string ConfirmationNote => _preview is null ? "" :
        _preview.RequiresIdentityResolution ?
        "وجد النظام هوية مؤرشفة مشابهة. لا يمكن إضافة الفرد من هذه النافذة؛ استخدم مسار التحقق الأصلي لإثبات الهوية أو الاستعادة." :
        _create ? "ستنشأ هوية جديدة مستقلة مع الموقع المعتمد، بعد تأكيدك فقط." :
        "سيحفظ النظام لقطة الهوية السابقة وسجل العملية دون تعديل تحضير الأيام السابقة.";
    public string CompletionNote => _receipt is null ? "" :
        _receipt.IsCreate ? $"تمت إضافة الفرد. المعرف الثابت: {_receipt.PersonId}" :
        $"تم اعتماد تعديل الهوية. رقم المراجعة الجديدة: {_receipt.Revision}";
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;

    public string FormName
    {
        get => _name;
        set { if (_name == value) return; if (!CanEdit) return;
            _name = value ?? ""; ChangedForm(nameof(FormName)); }
    }
    public string FormNumber
    {
        get => _number;
        set { if (_number == value) return; if (!CanEdit) return;
            _number = value ?? ""; ChangedForm(nameof(FormNumber)); }
    }
    public string FormSiteId
    {
        get => _site;
        set { if (_site == value) return; if (!CanEdit || !_create) return;
            _site = value ?? ""; ChangedForm(nameof(FormSiteId)); }
    }
    public string FormReason
    {
        get => _reason;
        set { if (_reason == value) return; if (!CanEdit || _create) return;
            _reason = value ?? ""; ChangedForm(nameof(FormReason)); }
    }

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativePersonnelMutationBackend backend,
        Func<NativeIdentity?> currentActor,
        Func<NativePersonnelDisplayRow, bool> rowIsCurrent)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Personnel mutation backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = currentActor ?? throw new ArgumentNullException(nameof(currentActor));
        _onPage = rowIsCurrent ?? throw new ArgumentNullException(nameof(rowIsCurrent));
        Notify();
    }

    public void OpenCreate(IReadOnlyList<NativeDashboardSite> sites)
    {
        var actor = _liveActor?.Invoke();
        if (_backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _unverifiedActor == actor || sites is null ||
            sites.Count > 10000)
            return;
        Reset();
        _actor = actor;
        _create = true;
        _sites = [new NativeDashboardSite("", "غير محدد"),
            .. sites.Where(s => !string.IsNullOrWhiteSpace(s.SiteId))];
        _site = "";
        _open = true;
        Notify();
    }

    public void OpenEdit(NativePersonnelDisplayRow row)
    {
        var actor = _liveActor?.Invoke();
        if (_backend is null || actor is null || row is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _unverifiedActor == actor ||
            _onPage?.Invoke(row) != true ||
            !row.Active || row.Revision < 1)
            return;
        Reset();
        _actor = actor;
        _selectedRow = row;
        _create = false;
        _name = row.Name;
        _number = row.ServiceNumberA;
        _site = "";
        _reason = "";
        _open = true;
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        if (!CanPreview || _backend is null || _actor is not { } actor) return;
        var draft = CurrentDraft();
        if (draft is null)
        {
            _error = "أدخل اسما كاملا صحيحا ورقما (ع) من 4 إلى 70 رقما؛ والتعديل يتطلب سبباً واضحاً.";
            InvalidatePreview();
            Notify();
            return;
        }

        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _error = "";
        _preview = null;
        _approvedDraft = null;
        Notify();
        try
        {
            var authoritative = await _backend.PreviewAsync(actor, draft, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid())
            {
                ForceCancel();
                return;
            }
            ValidatePreview(authoritative, draft);
            _approvedDraft = draft;
            _preview = authoritative;
            if (authoritative.RequiresIdentityResolution)
                _error = "يجب تسوية تطابق هوية الفرد مع الأرشيف عبر المسار الأصلي قبل اعتماد الإضافة.";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
                _error = "تعذرت معاينة الهوية الأصلية أو توجد بيانات متعارضة. لم يتم حفظ أي تعديل.";
        }
        finally
        {
            if (version == _generation)
            {
                _busy = false;
                _pending = null;
                Notify();
            }
        }
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || _backend is null ||
            _actor is not { } actor || _preview is not { } preview)
            return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _writeAttempted = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            // Canonical service stores the durable idempotency receipt.
            // A lost reply does not authorize a second operationId or retry.
            var receipt = await _backend.CommitAsync(actor, preview, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _unverified = true;
                _unverifiedActor = actor;
                return;
            }
            if (receipt.IsCreate != preview.Draft.IsCreate ||
                string.IsNullOrWhiteSpace(receipt.PersonId) ||
                receipt.PersonId.Length > 128 ||
                (!receipt.IsCreate && receipt.PersonId != preview.Draft.PersonId) ||
                receipt.Revision != (receipt.IsCreate ? 1 : preview.Draft.BaseRevision + 1))
                throw new InvalidDataException("Mismatched canonical person receipt.");
            _receipt = receipt;
            _preview = null;
            _approvedDraft = null;
            _unverified = false;
        }
        catch
        {
            if (version == _generation)
            {
                _unverified = true;
                _unverifiedActor = actor;
                _preview = null;
                _approvedDraft = null;
                _error = "تعذر تأكيد نتيجة الحفظ. راجع سجل الأفراد والعمليات قبل أي محاولة أخرى؛ لن تُعاد العملية تلقائيا.";
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
        if (_writeAttempted && _receipt is null && _actor is { } actor)
            _unverifiedActor = actor;
        Reset();
    }

    private void Reset()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _actor = null;
        _selectedRow = null;
        _approvedDraft = null;
        _preview = null;
        _receipt = null;
        _open = false;
        _busy = false;
        _writeAttempted = false;
        _unverified = false;
        _create = false;
        _name = "";
        _number = "";
        _site = "";
        _reason = "";
        _error = "";
        _sites = [];
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _actor is { } actor && _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        (_create || (_selectedRow is not null &&
            _onPage?.Invoke(_selectedRow) == true));

    private NativePersonnelMutationDraft? CurrentDraft()
    {
        string name = _name.Trim();
        string number = _number.Trim();
        string reason = _reason.Trim();
        if (name.Length is < 3 or > 200 || name.Any(char.IsControl) ||
            number.Length is < 4 or > 70 ||
            number.Any(c => !((c >= '0' && c <= '9') ||
                (c >= '\u0660' && c <= '\u0669') ||
                (c >= '\u06F0' && c <= '\u06F9'))) ||
            (!_create && (reason.Length is < 3 or > 500 ||
                           reason.Any(char.IsControl))) ||
            (_create && (_site.Length > 128 ||
                          _site.Any(char.IsControl) ||
                          !_sites.Any(x => x.SiteId == _site))))
            return null;
        return new NativePersonnelMutationDraft(_create,
            _create ? "" : _selectedRow?.PersonId ?? "",
            _create ? 0 : _selectedRow?.Revision ?? 0,
            _create ? "" : _selectedRow?.Name ?? "",
            _create ? "" : _selectedRow?.ServiceNumberA ?? "",
            name, number, _create ? _site : "", _create ? "" : reason);
    }

    private static void ValidatePreview(NativePersonnelMutationPreview preview,
        NativePersonnelMutationDraft draft)
    {
        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (preview.Draft != draft || preview.EffectiveLocalDate != today ||
            preview.CanonicalNumber.Length is < 4 or > 70 ||
            preview.CanonicalNumber.Any(c => c is < '0' or > '9') ||
            preview.CandidateCount is < 0 or > 50 ||
            (preview.RequiresIdentityResolution != (preview.CandidateCount > 0)) ||
            (draft.IsCreate &&
                (preview.OldName.Length != 0 || preview.OldNumber.Length != 0 ||
                 preview.PreviewToken.Length != 0)) ||
            (!draft.IsCreate &&
                (preview.OldName != draft.OriginalName ||
                 preview.OldNumber != draft.OriginalNumber ||
                 preview.PreviewToken.Length != 64 ||
                 preview.CandidateCount != 0)))
            throw new InvalidDataException("Forged canonical person preview.");
    }

    private void ChangedForm(string propertyName)
    {
        InvalidatePreview();
        Changed(propertyName);
        Notify();
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _preview = null;
        _approvedDraft = null;
        _error = "";
        _busy = false;
    }

    private void Changed(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Notify()
    {
        Changed(nameof(IsOpen), nameof(IsCreate), nameof(IsEdit),
            nameof(IsBusy), nameof(CanEdit), nameof(CanPreview), nameof(CanConfirm),
            nameof(HasUnverifiedCommit), nameof(HasPreview), nameof(HasReceipt),
            nameof(Header), nameof(Error),
            nameof(Preview), nameof(Receipt), nameof(ConfirmationNote),
            nameof(CompletionNote), nameof(SiteOptions),
            nameof(FormName), nameof(FormNumber),
            nameof(FormSiteId), nameof(FormReason));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
