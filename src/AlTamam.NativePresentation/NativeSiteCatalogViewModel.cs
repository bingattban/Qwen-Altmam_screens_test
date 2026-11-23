using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// Sites catalogue: original SQLCipher read and original audited create/rename
/// only. Separate local human review before one immutable operationId. A lost
/// commit reply is never interpreted as failure and is never auto-retried.
/// </summary>
public sealed class NativeSiteCatalogViewModel : INotifyPropertyChanged
{
    private INativeSiteCatalogBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _reviewing, _create;
    private string _name = "", _error = "";
    private NativeSiteRow? _selected;
    private NativeSiteMutationReceipt? _receipt;
    private IReadOnlyList<NativeSiteRow> _sites = [];
    private NativeSiteDraft? _reviewDraft;

    private sealed record NativeSiteDraft(
        bool IsCreate, string SiteId, long Revision, string OldName, string NewName);

    public NativeSiteCatalogViewModel()
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanRead && !_busy);
        CreateCommand = new NativeCommand(_ => BeginCreate(), _ => CanEdit);
        RenameCommand = new NativeCommand(x => {
            if (x is NativeSiteRow row) BeginRename(row);
        }, x => CanEdit && x is NativeSiteRow row && MayRename(row));
        ReviewCommand = new NativeCommand(_ => Review(), _ => CanReview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CancelDraftCommand = new NativeCommand(_ => CancelDraft(), _ => CanEdit);
        CloseCommand = new NativeCommand(_ => Close(), _ => _open && !_busy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;

    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanRead => _open && _backend is not null && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public bool CanEdit => CanRead && !_busy && !_attempted;
    public bool IsCreating => _create;
    public bool IsReviewing => _reviewing;
    public bool HasDraft => _selected is not null || _create;
    public bool HasReceipt => _receipt is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanReview => CanEdit && HasDraft && ValidName(_name) &&
        (_create || _selected is { } site && MayRename(site) && site.Name != _name);
    public bool CanConfirm => CanEdit && _reviewing && _reviewDraft == CurrentDraft();
    public string DraftName
    {
        get => _name;
        set
        {
            if (!CanEdit || _name == value) return;
            _name = value ?? "";
            InvalidateReview();
            Notify();
        }
    }
    public string Error => _error;
    public string Heading => _create ? "إضافة موقع جديد" :
        _selected is not null ? "تعديل اسم الموقع المعتمد" : "إدارة المواقع";
    public string ReviewNote => _reviewDraft is null ? "" :
        _reviewDraft.IsCreate
            ? $"بعد التأكيد سيُنشأ موقع مستقل باسم «{_reviewDraft.NewName}» دون نقل أي فرد أو تغيير الكشوف."
            : $"سيُغيّر اسم الموقع «{_reviewDraft.OldName}» إلى «{_reviewDraft.NewName}» بالمعرف {_reviewDraft.SiteId} وإصدار {_reviewDraft.Revision}، مع حفظ تاريخه السابق.";
    public string CompletionNote => _receipt is null ? "" :
        $"تم اعتماد الموقع {_receipt.Name} — المعرف {_receipt.SiteId}، الإصدار {_receipt.Revision}.";
    public NativeSiteMutationReceipt? Receipt => _receipt;
    public IReadOnlyList<NativeSiteRow> Sites => _sites;
    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelDraftCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(
        INativeSiteCatalogBackend backend, Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Sites backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (_open || _backend is null || _liveActor?.Invoke() is not { } actor ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _uncertainActor == actor) return;
        Close();
        _actor = actor;
        _open = true;
        Notify();
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CanRead || _backend is null || _actor is not { } actor)
        {
            Close();
            return;
        }
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _sites = [];
        CancelDraftInternal();
        _error = "";
        Notify();
        try
        {
            var rows = await _backend.ReadSitesAsync(actor, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanRead) { Close(); return; }
            if (rows is null || rows.Count > 10001 ||
                rows.Any(row => row is null ||
                    row.SiteId is not { Length: >= 1 and <= 128 } ||
                    row.SiteId.Any(char.IsControl) ||
                    row.Name is not { Length: >= 1 and <= 200 } ||
                    row.Name.Any(char.IsControl) ||
                    row.Revision < 1) ||
                rows.Select(x => x.SiteId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                throw new InvalidDataException("Invalid canonical site catalogue.");
            _sites = rows.ToArray();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _sites = [];
                _error = "تعذرت قراءة المواقع المعتمدة؛ أُخفيت البيانات القديمة.";
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

    public void BeginCreate()
    {
        if (!CanEdit) return;
        CancelDraftInternal();
        _create = true;
        _name = "";
        Notify();
    }

    public void BeginRename(NativeSiteRow? row)
    {
        if (!CanEdit || row is null || !MayRename(row)) return;
        CancelDraftInternal();
        _create = false;
        _selected = row;
        _name = row.Name;
        Notify();
    }

    public void Review()
    {
        if (!CanReview)
        {
            _error = "اسم الموقع يجب أن يكون من حرفين إلى 100 حرف، غير محمي ومختلفا عن الاسم السابق.";
            Notify();
            return;
        }
        _reviewDraft = CurrentDraft();
        _reviewing = _reviewDraft is not null;
        _error = "";
        Notify();
    }

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || _backend is null || _actor is not { } actor ||
            _reviewDraft is not { } draft) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // MUST precede first await of SQLCipher writer.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            NativeSiteMutationReceipt receipt = draft.IsCreate
                ? await _backend.CreateSiteAsync(actor, draft.NewName,
                    operationId, linked.Token)
                : await _backend.RenameSiteAsync(actor, draft.SiteId,
                    draft.Revision, draft.NewName, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !CanRead)
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (receipt is null ||
                receipt.Name != draft.NewName ||
                receipt.Revision != (draft.IsCreate ? 1 : draft.Revision + 1) ||
                (draft.IsCreate
                    ? receipt.SiteId is not { Length: >= 6 and <= 128 } ||
                      !receipt.SiteId.StartsWith("SITE-", StringComparison.Ordinal) ||
                      receipt.SiteId == "SITE-UNSPECIFIED" ||
                      _sites.Any(row => row.SiteId == receipt.SiteId)
                    : receipt.SiteId != draft.SiteId))
                throw new InvalidDataException("Canonical site write receipt mismatch.");
            _receipt = receipt;
            _reviewing = false;
            _reviewDraft = null;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _receipt = null;
                _reviewing = false;
                _reviewDraft = null;
                _error = "قد يكون تغيير الموقع قد حُفظ رغم انقطاع الرد. راجع المواقع وسجل العمليات قبل أي إعادة محاولة؛ لا إعادة إرسال تلقائية.";
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

    public void CancelDraft()
    {
        if (!CanEdit) return;
        CancelDraftInternal();
        Notify();
    }

    public void Close()
    {
        if (_open && _busy && _attempted) return; // no hiding possible write
        bool success = _receipt is not null;
        Reset();
        if (success) CommittedAndClosed?.Invoke();
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
        _actor = null;
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _sites = [];
        _receipt = null;
        _error = "";
        CancelDraftInternal();
        Notify();
    }

    private void CancelDraftInternal()
    {
        _selected = null;
        _create = false;
        _name = "";
        _reviewing = false;
        _reviewDraft = null;
    }

    private void InvalidateReview()
    {
        _reviewing = false;
        _reviewDraft = null;
        _error = "";
    }

    private NativeSiteDraft? CurrentDraft()
    {
        if (!CanRead || !ValidName(_name)) return null;
        if (_create) return new NativeSiteDraft(true, "", 0, "", _name);
        if (_selected is not { } row || !MayRename(row) || row.Name == _name)
            return null;
        return new NativeSiteDraft(false, row.SiteId, row.Revision, row.Name, _name);
    }

    private bool MayRename(NativeSiteRow row) =>
        row.Active && row.Revision >= 1 &&
        row.SiteId != "SITE-UNSPECIFIED" &&
        _sites.Any(candidate => ReferenceEquals(candidate, row));

    private static bool ValidName(string? name) =>
        name is { Length: >= 2 and <= 100 } &&
        name == name.Trim() && name != "غير محدد" &&
        !name.Any(char.IsControl);

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanRead), nameof(CanEdit),
            nameof(IsCreating), nameof(IsReviewing), nameof(HasDraft),
            nameof(HasReceipt), nameof(HasUnverifiedCommit),
            nameof(CanReview), nameof(CanConfirm), nameof(DraftName),
            nameof(Error), nameof(Heading), nameof(ReviewNote),
            nameof(CompletionNote), nameof(Receipt), nameof(Sites)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand command in new[]
        {
            RefreshCommand, CreateCommand, RenameCommand,
            ReviewCommand, ConfirmCommand, CancelDraftCommand, CloseCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
