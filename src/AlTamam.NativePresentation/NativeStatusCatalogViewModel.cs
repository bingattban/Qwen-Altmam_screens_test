using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>
/// WPF manager status catalogue: canonical SQLCipher read and audited create,
/// update, logical disable. The original service remains authoritative for
/// unique symbols, historical snapshots and the auto-attendance default.
/// </summary>
public sealed class NativeStatusCatalogViewModel : INotifyPropertyChanged
{
    private enum ActionKind { None, Create, Update, Deactivate }

    private sealed record ReviewDraft(
        ActionKind Action, string StatusId, long BaseRevision,
        string OldName, string OldSymbol, string OldColor,
        string Name, string Symbol, string Color, long TodayCount);

    private INativeStatusCatalogBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _reviewing, _deactivateConfirmed;
    private ActionKind _action;
    private string _name = "", _symbol = "", _color = "", _error = "";
    private NativeStatusRow? _selected;
    private ReviewDraft? _reviewDraft;
    private NativeStatusMutationReceipt? _receipt;
    private IReadOnlyList<NativeStatusRow> _statuses = [];

    public NativeStatusCatalogViewModel()
    {
        RefreshCommand = new NativeCommand(_ => _ = RefreshAsync(), _ => CanEdit);
        CreateCommand = new NativeCommand(_ => BeginCreate(), _ => CanEdit);
        EditCommand = new NativeCommand(p => {
            if (p is NativeStatusRow row) BeginEdit(row);
        }, p => CanEdit && p is NativeStatusRow row && MayEdit(row));
        DeactivateCommand = new NativeCommand(p => {
            if (p is NativeStatusRow row) BeginDeactivate(row);
        }, p => CanEdit && p is NativeStatusRow row && MayEdit(row));
        ReviewCommand = new NativeCommand(_ => Review(), _ => CanReview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CancelDraftCommand = new NativeCommand(_ => CancelDraft(), _ => CanEdit);
        CloseCommand = new NativeCommand(_ => Close(), _ => _open && !_busy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanRead => _open && _actor is { } actor && _backend is not null &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role);
    public bool CanEdit => CanRead && !_busy && !_attempted;
    public bool CanEditFields => CanEdit && _action is ActionKind.Create or ActionKind.Update;
    public bool IsCreating => _action == ActionKind.Create;
    public bool IsDeactivating => _action == ActionKind.Deactivate;
    public bool HasDraft => _action != ActionKind.None;
    public bool IsReviewing => _reviewing;
    public bool HasReceipt => _receipt is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanReview => CanEdit && CurrentDraft() is not null;
    public bool CanConfirm => CanEdit && _reviewing && _reviewDraft is not null &&
        CurrentDraft() == _reviewDraft &&
        (!IsDeactivating || _deactivateConfirmed);
    public string Error => _error;
    public IReadOnlyList<NativeStatusRow> Statuses => _statuses;
    public NativeStatusMutationReceipt? Receipt => _receipt;

    public string Heading => _action switch
    {
        ActionKind.Create => "إضافة حالة تحضير",
        ActionKind.Update => "تعديل الاسم والرمز واللون",
        ActionKind.Deactivate => "تعطيل حالة التحضير",
        _ => "إدارة حالات التحضير"
    };
    public string ReviewNote => _reviewDraft is not { } review ? "" :
        review.Action switch
        {
            ActionKind.Create =>
                $"ستُضاف حالة «{review.Name}» بالرمز «{review.Symbol}» واللون {review.Color}. لن يُحفظ أي تغيير إلا بعد تأكيدك.",
            ActionKind.Update =>
                $"ستُحدّث الحالة «{review.OldName}» بالمعرف الثابت {review.StatusId} وإصدار {review.BaseRevision}: «{review.Name}»، الرمز «{review.Symbol}»، اللون {review.Color}. تبقى سجلات الماضي كما هي.",
            ActionKind.Deactivate =>
                $"سيُوقف استخدام الحالة «{review.OldName}» (المعرف {review.StatusId}) من القوائم الجديدة. عدد مرات استخدامها اليوم: {review.TodayCount}. لا تُحذف سجلات التحضير السابقة. يمنع النظام تعطيل الحالة الافتراضية للتحضير التلقائي.",
            _ => ""
        };
    public string CompletionNote => _receipt is not { } receipt ? "" :
        $"تم اعتماد الحالة «{receipt.Name}»، المعرف {receipt.StatusId}، الإصدار {receipt.Revision}، نشطة: {(receipt.Active ? "نعم" : "لا")}.";
    public string DraftName
    {
        get => _name;
        set
        {
            if (!CanEditFields || _name == value) return;
            _name = value ?? "";
            InvalidateReview();
            Notify();
        }
    }
    public string DraftSymbol
    {
        get => _symbol;
        set
        {
            if (!CanEditFields || _symbol == value) return;
            _symbol = value ?? "";
            InvalidateReview();
            Notify();
        }
    }
    public string DraftColor
    {
        get => _color;
        set
        {
            if (!CanEditFields || _color == value) return;
            _color = value ?? "";
            InvalidateReview();
            Notify();
        }
    }
    public bool DeactivationConfirmed
    {
        get => _deactivateConfirmed;
        set
        {
            if (!CanEdit || !_reviewing || !IsDeactivating ||
                _deactivateConfirmed == value) return;
            _deactivateConfirmed = value;
            Notify();
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelDraftCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(
        INativeStatusCatalogBackend backend, Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Status catalogue backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        Notify();
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_open || _backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _uncertainActor == actor) return;
        Reset();
        _actor = actor;
        _open = true;
        Notify();
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CanEdit || _backend is null || _actor is not { } actor) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _statuses = [];
        CancelDraftInternal();
        _error = "";
        Notify();
        try
        {
            var rows = await _backend.ReadActiveStatusesAsync(actor, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!CanRead) { ForceCancel(); return; }
            if (rows is null || rows.Count > 10000 ||
                rows.Any(r => r is null ||
                    r.StatusId is not { Length: >= 1 and <= 128 } ||
                    r.StatusId.Any(char.IsControl) ||
                    r.Name is not { Length: >= 2 and <= 75 } ||
                    r.Name.Any(char.IsControl) ||
                    r.Symbol is not { Length: >= 1 and <= 5 } ||
                    r.Symbol.Any(char.IsControl) ||
                    !IsColor(r.Color) || r.Revision < 1 || r.TodayCount < 0) ||
                rows.Select(r => r.StatusId).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                throw new InvalidDataException("Malformed or duplicated active status catalogue.");
            _statuses = rows.ToArray();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _statuses = [];
                _error = "تعذرت قراءة الحالات الأصلية؛ تم إخفاء البيانات غير الموثوقة.";
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
        _action = ActionKind.Create;
        _color = "#0D9488";
        Notify();
    }

    public void BeginEdit(NativeStatusRow? row)
    {
        if (!CanEdit || row is null || !MayEdit(row)) return;
        CancelDraftInternal();
        _action = ActionKind.Update;
        _selected = row;
        _name = row.Name;
        _symbol = row.Symbol;
        _color = row.Color;
        Notify();
    }

    public void BeginDeactivate(NativeStatusRow? row)
    {
        if (!CanEdit || row is null || !MayEdit(row)) return;
        CancelDraftInternal();
        _action = ActionKind.Deactivate;
        _selected = row;
        _name = row.Name;
        _symbol = row.Symbol;
        _color = row.Color;
        Notify();
    }

    public void Review()
    {
        if (!CanReview || CurrentDraft() is not { } draft)
        {
            _error = "تحقق من اسم الحالة (2–75)، الرمز (1–5) ولون HEX (#RRGGBB)، أو حدّث بيانات الحالة المختارة.";
            Notify();
            return;
        }
        _reviewDraft = draft;
        _reviewing = true;
        _deactivateConfirmed = false;
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
        _attempted = true; // Set before FIRST awaited SQLCipher mutation.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            NativeStatusMutationReceipt receipt = draft.Action switch
            {
                ActionKind.Create => await _backend.CreateStatusAsync(
                    actor, draft.Name, draft.Symbol, draft.Color,
                    operationId, linked.Token),
                ActionKind.Update => await _backend.UpdateStatusAsync(
                    actor, draft.StatusId, draft.BaseRevision, draft.Name,
                    draft.Symbol, draft.Color, operationId, linked.Token),
                ActionKind.Deactivate => await _backend.DeactivateStatusAsync(
                    actor, draft.StatusId, draft.BaseRevision,
                    operationId, linked.Token),
                _ => throw new InvalidDataException("Unsupported status action.")
            };
            if (version != _generation || linked.IsCancellationRequested ||
                !CanRead)
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            bool create = draft.Action == ActionKind.Create;
            bool deactivate = draft.Action == ActionKind.Deactivate;
            if (receipt is null ||
                receipt.Name != draft.Name ||
                receipt.Symbol != draft.Symbol ||
                receipt.Color != draft.Color ||
                receipt.Revision != (create ? 1 : draft.BaseRevision + 1) ||
                receipt.Active != !deactivate ||
                receipt.TodayCount < 0 ||
                (create && (receipt.TodayCount != 0 ||
                    receipt.StatusId is not { Length: >= 5 and <= 128 } ||
                    !receipt.StatusId.StartsWith("STS-", StringComparison.Ordinal) ||
                    _statuses.Any(x => x.StatusId == receipt.StatusId))) ||
                (!create && receipt.StatusId != draft.StatusId))
                throw new InvalidDataException("Status commit receipt changed.");
            _receipt = receipt;
            _reviewing = false;
            _deactivateConfirmed = false;
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
                _deactivateConfirmed = false;
                _error = "قد يكون التعديل قد اعتمد رغم فقدان الاستجابة. تحقق من الحالات وسجل العمليات قبل أي محاولة جديدة؛ لا تكرار تلقائي.";
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
        if (!_open || _busy) return;
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
        _statuses = [];
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _receipt = null;
        _error = "";
        CancelDraftInternal();
        Notify();
    }

    private void CancelDraftInternal()
    {
        _action = ActionKind.None;
        _selected = null;
        _name = "";
        _symbol = "";
        _color = "";
        InvalidateReview();
    }

    private void InvalidateReview()
    {
        _reviewing = false;
        _reviewDraft = null;
        _deactivateConfirmed = false;
        _error = "";
    }

    private ReviewDraft? CurrentDraft()
    {
        if (!CanRead || _action == ActionKind.None ||
            !ValidFields(_name, _symbol, _color)) return null;
        if (_action == ActionKind.Create)
            return new ReviewDraft(ActionKind.Create, "", 0, "", "", "",
                _name, _symbol, _color, 0);
        if (_selected is not { } row || !MayEdit(row)) return null;
        if (_action == ActionKind.Update &&
            row.Name == _name && row.Symbol == _symbol && row.Color == _color)
            return null;
        return new ReviewDraft(_action, row.StatusId, row.Revision,
            row.Name, row.Symbol, row.Color, _name, _symbol, _color,
            row.TodayCount);
    }

    private bool MayEdit(NativeStatusRow row) =>
        row.Revision >= 1 &&
        _statuses.Any(current => ReferenceEquals(current, row));

    private static bool ValidFields(string? name, string? symbol, string? color) =>
        name is { Length: >= 2 and <= 75 } && name == name.Trim() &&
        !name.Any(char.IsControl) &&
        symbol is { Length: >= 1 and <= 5 } && symbol == symbol.Trim() &&
        !symbol.Any(char.IsControl) && IsColor(color);

    private static bool IsColor(string? color) =>
        color is not null && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$",
            RegexOptions.CultureInvariant);

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanRead), nameof(CanEdit),
            nameof(CanEditFields), nameof(IsCreating), nameof(IsDeactivating),
            nameof(HasDraft), nameof(IsReviewing), nameof(HasReceipt),
            nameof(HasUnverifiedCommit), nameof(CanReview), nameof(CanConfirm),
            nameof(DraftName), nameof(DraftSymbol), nameof(DraftColor),
            nameof(DeactivationConfirmed), nameof(Heading), nameof(Error),
            nameof(ReviewNote), nameof(CompletionNote), nameof(Receipt),
            nameof(Statuses)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        foreach (ICommand command in new[]
        {
            RefreshCommand, CreateCommand, EditCommand, DeactivateCommand,
            ReviewCommand, ConfirmCommand, CancelDraftCommand, CloseCommand
        })
            ((NativeCommand)command).NotifyAvailabilityChanged();
    }
}
