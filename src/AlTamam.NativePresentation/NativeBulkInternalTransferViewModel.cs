using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Selectable immutable reference to a person on the live CURRENT page.</summary>
public sealed class NativeBulkTransferOption : INotifyPropertyChanged
{
    private readonly Func<bool> _mayChange;
    private readonly Action _changed;
    private bool _selected;

    public NativeBulkTransferOption(NativePersonnelDisplayRow row,
        Func<bool> mayChange, Action changed)
    {
        Row = row;
        _mayChange = mayChange;
        _changed = changed;
    }

    internal NativePersonnelDisplayRow Row { get; }
    public string PersonId => Row.PersonId;
    public string Name => Row.Name;
    public string Number => Row.ServiceNumberA;
    public string Rank => Row.Rank;
    public long Revision => Row.Revision;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value || !_mayChange()) return;
            _selected = value;
            PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
            _changed();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Only selected CURRENT visible active people can move; no hidden-row
/// selection, no loop of individual writes. One original SQLCipher preview
/// freezes all PersonIds and row revisions; one explicit commit is atomic.
/// </summary>
public sealed class NativeBulkInternalTransferViewModel : INotifyPropertyChanged
{
    private INativeBulkInternalTransferBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    private Func<NativePersonnelDisplayRow, bool>? _onPage;
    private NativeIdentity? _actor, _uncertainActor;
    private CancellationTokenSource? _pending;
    private int _generation;
    private bool _open, _busy, _attempted, _uncertain, _acknowledged;
    private string _sourceSiteId = "", _sourceSiteName = "", _destinationSiteId = "";
    private string _error = "";
    private IReadOnlyList<NativeDashboardSite> _sites = [];
    private IReadOnlyList<NativeBulkTransferOption> _options = [];
    private IReadOnlyList<NativeBulkInternalPerson>? _approvedPeople;
    private NativeBulkInternalTransferDraft? _approvedDraft;
    private NativeBulkInternalTransferPreview? _preview;
    private NativeBulkInternalTransferReceipt? _receipt;

    public NativeBulkInternalTransferViewModel()
    {
        PreviewCommand = new NativeCommand(_ => _ = PreviewAsync(), _ => CanPreview);
        ConfirmCommand = new NativeCommand(_ => _ = ConfirmAsync(), _ => CanConfirm);
        CloseCommand = new NativeCommand(_ => Close(), _ => IsOpen && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? CommittedAndClosed;
    public bool IsOpen => _open;
    public bool IsBusy => _busy;
    public bool CanEdit => _open && !_busy && !_attempted &&
        IsOriginalContextValid();
    public bool CanPreview => CanEdit && _backend is not null &&
        CurrentDraft() is not null;
    public bool HasPreview => _preview is not null;
    public bool HasUnverifiedCommit => _uncertain;
    public bool CanConfirm => CanEdit && _acknowledged &&
        _preview is not null && _approvedDraft is not null &&
        _approvedPeople is not null &&
        CurrentDraft() is { } current &&
        SameDraft(current, _approvedDraft) &&
        CurrentPeople().SequenceEqual(_approvedPeople);
    public int SelectedCount => _options.Count(x => x.IsSelected);
    public string SourceSiteName => _sourceSiteName;
    public string SourceSiteId => _sourceSiteId;
    public string DestinationSiteId
    {
        get => _destinationSiteId;
        set
        {
            if (!CanEdit || _destinationSiteId == value) return;
            _destinationSiteId = value ?? "";
            InvalidatePreview();
            Notify();
        }
    }
    public bool ReviewAcknowledged
    {
        get => _acknowledged;
        set
        {
            if (!CanEdit || _preview is null || _acknowledged == value)
                return;
            _acknowledged = value;
            Notify();
        }
    }
    public string Error => _error;
    public NativeBulkInternalTransferPreview? Preview => _preview;
    public NativeBulkInternalTransferReceipt? Receipt => _receipt;
    public IReadOnlyList<NativeBulkTransferOption> Options => _options;
    public IReadOnlyList<NativeDashboardSite> SiteOptions => _sites;
    public string ReviewMessage => _preview is null ? "" :
        $"تمت معاينة {_preview.Persons.Count} فردا محددين بالاسم والرقم (ع) والمعرف. " +
        $"سيُنقلون من «{_sourceSiteName}» إلى «{_preview.DestinationName}» بتاريخ {_preview.BusinessDate} " +
        "في عملية واحدة، دون المساس بالتحضير السابق. لا تشمل العملية بقية الموقع أو صفحات أخرى.";
    public string CompletionMessage => _receipt is null ? "" :
        $"تم اعتماد حركة داخلية واحدة لـ {_receipt.Count} فرد إلى «{_receipt.DestinationName}» بتاريخ {_receipt.BusinessDate}.";

    public ICommand PreviewCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CloseCommand { get; }

    public void AttachBackend(INativeBulkInternalTransferBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool> currentRow)
    {
        if (_backend is not null)
            throw new InvalidOperationException("Bulk transfer backend already attached.");
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _liveActor = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
        _onPage = currentRow ?? throw new ArgumentNullException(nameof(currentRow));
    }

    public void Open(IReadOnlyList<NativePersonnelDisplayRow> pageRows,
        string sourceSiteId, IReadOnlyList<NativeDashboardSite> sites)
    {
        NativeIdentity? actor = _liveActor?.Invoke();
        if (_backend is null || actor is null ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
            _open || _uncertainActor == actor ||
            string.IsNullOrWhiteSpace(sourceSiteId) ||
            sourceSiteId.Length > 128 || sourceSiteId.Any(char.IsControl) ||
            pageRows is null || pageRows.Count is < 1 or > 100 ||
            sites is null || sites.Count is < 2 or > 10000 ||
            pageRows.Any(r => r is null || _onPage?.Invoke(r) != true ||
                !r.Active || r.StatusName != "على القوة" ||
                r.Revision < 1 ||
                r.PersonId is not { Length: >= 1 and <= 128 } ||
                r.PersonId.Any(char.IsControl) ||
                r.Name is not { Length: >= 2 and <= 200 } ||
                r.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                r.ServiceNumberA.Any(ch => ch is < '0' or > '9')) ||
            pageRows.Select(r => r.PersonId).Distinct(StringComparer.Ordinal).Count() !=
                pageRows.Count ||
            sites.Select(x => x.SiteId).Distinct(StringComparer.Ordinal).Count() != sites.Count)
            return;
        var matching = sites.Where(x => x.SiteId == sourceSiteId).ToArray();
        if (matching.Length != 1 || string.IsNullOrWhiteSpace(matching[0].Name) ||
            pageRows.Any(r => r.SiteName != matching[0].Name))
            return;
        Reset();
        _actor = actor;
        _sourceSiteId = sourceSiteId;
        _sourceSiteName = matching[0].Name;
        _sites = sites.Where(x => x.SiteId != sourceSiteId &&
            x.SiteId is { Length: > 0 and <= 128 } &&
            !string.IsNullOrWhiteSpace(x.Name)).ToArray();
        if (_sites.Count == 0) { Reset(); return; }
        _open = true;
        _options = pageRows.Select(r => new NativeBulkTransferOption(r,
            () => CanEdit, OnSelectionChanged)).ToArray();
        Notify();
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        NativeBulkInternalTransferDraft? draft = CurrentDraft();
        if (!CanEdit || draft is null || _backend is null ||
            _actor is not { } actor) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _busy = true;
        _preview = null;
        _approvedPeople = null;
        _approvedDraft = null;
        _acknowledged = false;
        _error = "";
        Notify();
        try
        {
            var preview = await _backend.PreviewInternalBatchAsync(
                actor, draft, linked.Token);
            if (version != _generation || linked.IsCancellationRequested) return;
            if (!IsOriginalContextValid()) { ForceCancel(); return; }
            ValidatePreview(preview, draft);
            _approvedPeople = draft.Persons.ToArray();
            _approvedDraft = draft;
            _preview = preview;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            if (version == _generation)
            {
                _preview = null;
                _approvedPeople = null;
                _approvedDraft = null;
                _error = "تعذرت معاينة التنقلات أو تغير فرد/انتسابه. لم تُحفظ حركة؛ حدّث الكشف قبل المحاولة.";
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

    public async Task ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm || _backend is null || _actor is not { } actor ||
            _preview is not { } preview) return;
        int version = ++_generation;
        _pending?.Cancel();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pending = linked;
        _attempted = true; // Before FIRST awaited original SQLCipher bulk commit.
        _busy = true;
        _error = "";
        string operationId = Guid.NewGuid().ToString("N");
        Notify();
        try
        {
            var result = await _backend.CommitInternalBatchAsync(
                actor, preview, operationId, linked.Token);
            if (version != _generation || linked.IsCancellationRequested ||
                !IsOriginalContextValid())
            {
                _uncertainActor = actor;
                _uncertain = true;
                return;
            }
            if (result is null ||
                result.BusinessDate != preview.BusinessDate ||
                result.Count != preview.Persons.Count ||
                result.DestinationName != preview.DestinationName)
                throw new InvalidDataException("Original bulk movement receipt changed.");
            _receipt = result;
            _preview = null;
            _approvedDraft = null;
            _approvedPeople = null;
            _acknowledged = false;
        }
        catch
        {
            if (version == _generation)
            {
                _uncertainActor = actor;
                _uncertain = true;
                _preview = null;
                _approvedDraft = null;
                _approvedPeople = null;
                _acknowledged = false;
                _error = "قد تكون التنقلات الجماعية قد حُفظت رغم فقدان الرد. راجع أرشيف التنقلات وسجل العمليات قبل أي إجراء؛ لا إعادة إرسال تلقائية.";
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
        _actor = null;
        _options = [];
        _sites = [];
        _open = false;
        _busy = false;
        _attempted = false;
        _uncertain = false;
        _acknowledged = false;
        _sourceSiteId = "";
        _sourceSiteName = "";
        _destinationSiteId = "";
        _error = "";
        _approvedPeople = null;
        _approvedDraft = null;
        _preview = null;
        _receipt = null;
        Notify();
    }

    private bool IsOriginalContextValid() =>
        _open && _actor is { } actor &&
        _liveActor?.Invoke() == actor &&
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) &&
        _options.Count is >= 1 and <= 100 &&
        _options.All(x => _onPage?.Invoke(x.Row) == true &&
            x.Row.Active && x.Row.Revision >= 1 &&
            x.Row.StatusName == "على القوة" &&
            x.Row.SiteName == _sourceSiteName);

    private IReadOnlyList<NativeBulkInternalPerson> CurrentPeople() =>
        _options.Where(x => x.IsSelected)
            .Select(x => new NativeBulkInternalPerson(
                x.Row.PersonId, x.Row.Name, x.Row.ServiceNumberA,
                x.Row.Revision)).ToArray();

    private NativeBulkInternalTransferDraft? CurrentDraft()
    {
        if (!IsOriginalContextValid()) return null;
        var persons = CurrentPeople();
        if (persons.Count is < 1 or > 100 ||
            persons.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != persons.Count ||
            _sites.Count(x => x.SiteId == _destinationSiteId) != 1 ||
            _destinationSiteId == _sourceSiteId)
            return null;
        return new NativeBulkInternalTransferDraft(
            _sourceSiteId, _sourceSiteName, _destinationSiteId, persons);
    }

    private static bool SameDraft(NativeBulkInternalTransferDraft a,
        NativeBulkInternalTransferDraft b) =>
        a.SourceSiteId == b.SourceSiteId &&
        a.SourceSiteName == b.SourceSiteName &&
        a.DestinationSiteId == b.DestinationSiteId &&
        a.Persons.SequenceEqual(b.Persons);

    private void ValidatePreview(NativeBulkInternalTransferPreview p,
        NativeBulkInternalTransferDraft draft)
    {
        string today = DateOnly.FromDateTime(DateTime.Now)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (p is null || !SameDraft(p.Draft, draft) ||
            p.BusinessDate != today ||
            p.PreviewToken is not { Length: 64 } ||
            p.PreviewToken.Any(x => !char.IsAsciiHexDigit(x)) ||
            _sites.Count(x => x.SiteId == draft.DestinationSiteId &&
                x.Name == p.DestinationName) != 1 ||
            p.Persons is null || p.Persons.Count != draft.Persons.Count ||
            p.Persons.Count is < 1 or > 100)
            throw new InvalidDataException("Malformed canonical multi-person preview.");
        for (int i = 0; i < draft.Persons.Count; i++)
        {
            var expected = draft.Persons[i];
            var actual = p.Persons[i];
            if (actual is null ||
                actual.PersonId != expected.PersonId ||
                actual.Name != expected.Name ||
                actual.ServiceNumberA != expected.ServiceNumberA ||
                actual.Revision != expected.Revision ||
                actual.SourceSiteId != draft.SourceSiteId ||
                actual.SourceSiteName != draft.SourceSiteName ||
                actual.SourceAssignmentId is not { Length: >= 3 and <= 128 })
                throw new InvalidDataException("A bulk transfer PersonId/version changed.");
        }
    }

    private void OnSelectionChanged()
    {
        InvalidatePreview();
        Notify();
    }

    private void InvalidatePreview()
    {
        ++_generation;
        _pending?.Cancel();
        _pending = null;
        _preview = null;
        _approvedDraft = null;
        _approvedPeople = null;
        _acknowledged = false;
        _error = "";
    }

    private void Notify()
    {
        foreach (string name in new[]
        {
            nameof(IsOpen), nameof(IsBusy), nameof(CanEdit),
            nameof(CanPreview), nameof(CanConfirm), nameof(HasPreview),
            nameof(HasUnverifiedCommit), nameof(SelectedCount),
            nameof(SourceSiteId), nameof(SourceSiteName),
            nameof(DestinationSiteId), nameof(ReviewAcknowledged),
            nameof(Error), nameof(Preview), nameof(Receipt),
            nameof(Options), nameof(SiteOptions),
            nameof(ReviewMessage), nameof(CompletionMessage)
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ((NativeCommand)PreviewCommand).NotifyAvailabilityChanged();
        ((NativeCommand)ConfirmCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseCommand).NotifyAvailabilityChanged();
    }
}
