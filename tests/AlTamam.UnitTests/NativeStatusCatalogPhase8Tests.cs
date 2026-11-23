using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Tests committed FIRST: no new attendance status SQL writer or silent destructive action.</summary>
public sealed class NativeStatusCatalogPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-STATUSES", "manager", UserRole.GeneralAdministration, "STATUSES-NONCE");
    private static readonly NativeStatusRow Present =
        new("STS-PRESENT", "حاضر", "ح", "#16A34A", 3, 17);
    private static readonly NativeStatusRow Holiday =
        new("STS-HOLIDAY", "إجازة", "إ", "#1D4ED8", 2, 0);

    private static NativeStatusCatalogViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativeStatusCatalogViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task Logged_out_or_supervisor_cannot_read_or_mutate_attendance_statuses()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Reads);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Original_active_statuses_have_color_symbol_revision_and_today_count()
    {
        var vm = Create(new FakeBackend(), () => Manager);
        await vm.OpenAsync();
        Assert.Equal(2, vm.Statuses.Count);
        Assert.Equal("#16A34A", vm.Statuses[0].Color);
        Assert.Equal("ح", vm.Statuses[0].Symbol);
        Assert.Equal(17, vm.Statuses[0].TodayCount);
        Assert.Equal(3, vm.Statuses[0].Revision);
    }

    [Fact]
    public async Task Create_is_nonwriting_until_separate_validated_human_confirmation()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginCreate();
        vm.DraftName = "x";
        vm.DraftSymbol = "";
        vm.DraftColor = "red";
        vm.Review();
        Assert.False(vm.IsReviewing);
        Assert.Equal(0, backend.Writes);
        vm.DraftName = "حالة طبية";
        vm.DraftSymbol = "ط";
        vm.DraftColor = "#D97706";
        vm.Review();
        Assert.True(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("STS-NEW", vm.Receipt?.StatusId);
        Assert.Equal(1, vm.Receipt?.Revision);
        Assert.Equal("حالة طبية", backend.LastName);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Update_keeps_original_status_id_expected_revision_and_historical_snapshots()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginEdit(vm.Statuses[0] with { Name = "forged" });
        Assert.False(vm.HasDraft);
        vm.BeginEdit(vm.Statuses[0]);
        vm.DraftName = "حاضر في الموقع";
        vm.DraftSymbol = "حض";
        vm.DraftColor = "#15803D";
        vm.Review();
        await vm.ConfirmAsync();
        Assert.Equal("STS-PRESENT", backend.LastStatusId);
        Assert.Equal(3, backend.LastRevision);
        Assert.Equal("حاضر في الموقع", backend.LastName);
        Assert.Equal("حض", backend.LastSymbol);
        Assert.Equal("#15803D", backend.LastColor);
        Assert.Equal(4, vm.Receipt?.Revision);
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Deactivation_requires_separate_explicit_checkbox_and_never_deletes_history()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginDeactivate(vm.Statuses[1]);
        vm.Review();
        Assert.True(vm.IsReviewing);
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
        vm.DeactivationConfirmed = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("STS-HOLIDAY", backend.LastStatusId);
        Assert.Equal(2, backend.LastRevision);
        Assert.Equal(false, vm.Receipt?.Active);
        Assert.Equal(0, vm.Receipt?.TodayCount);
        Assert.Equal(3, vm.Receipt?.Revision);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Changed_name_symbol_or_color_revokes_preview_and_disables_commit()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginEdit(vm.Statuses[0]);
        vm.DraftName = "اسم حالة جديد";
        vm.Review();
        Assert.True(vm.CanConfirm);
        vm.DraftColor = "#FFFFFF";
        Assert.False(vm.IsReviewing);
        Assert.False(vm.CanConfirm);
        vm.Review();
        vm.DraftSymbol = "ك";
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Forged_or_duplicate_canonical_status_rows_fail_closed()
    {
        var backend = new FakeBackend { InvalidRows = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        Assert.Empty(vm.Statuses);
        Assert.NotEmpty(vm.Error);
        vm.BeginCreate();
        Assert.False(vm.IsReviewing);
    }

    [Fact]
    public async Task Pending_read_after_logout_cannot_repopulate_closed_dialogue()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var signal = new TaskCompletionSource<IReadOnlyList<NativeStatusRow>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingRead = signal.Task;
        var vm = Create(backend, () => actor);
        Task loading = vm.OpenAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        vm.ForceCancel();
        signal.SetResult([Present]);
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Statuses);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Ambiguous_or_forged_status_write_receipt_never_autoretries()
    {
        var backend = new FakeBackend { ForgeReceipt = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginEdit(vm.Statuses[0]);
        vm.DraftName = "تعديل جديد";
        vm.Review();
        await vm.ConfirmAsync();
        Assert.Null(vm.Receipt);
        Assert.True(vm.HasUnverifiedCommit);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        vm.ForceCancel();
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(1, backend.Writes);
    }

    private sealed class FakeBackend : INativeStatusCatalogBackend
    {
        public int Reads, Writes;
        public bool InvalidRows, ForgeReceipt;
        public Task<IReadOnlyList<NativeStatusRow>>? PendingRead;
        public string LastStatusId = "", LastName = "", LastSymbol = "", LastColor = "";
        public long LastRevision;
        public Task<IReadOnlyList<NativeStatusRow>> ReadActiveStatusesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            Reads++;
            return PendingRead ?? Task.FromResult<IReadOnlyList<NativeStatusRow>>(
                InvalidRows ? [Present, Present] : [Present, Holiday]);
        }
        public Task<NativeStatusMutationReceipt> CreateStatusAsync(
            NativeIdentity actor, string name, string symbol, string color,
            string operationId, CancellationToken ct = default)
        {
            Writes++; LastName = name; LastSymbol = symbol; LastColor = color;
            return Task.FromResult(new NativeStatusMutationReceipt(
                ForgeReceipt ? "STS-FORGED" : "STS-NEW",
                name, symbol, color, 1, true, 0));
        }
        public Task<NativeStatusMutationReceipt> UpdateStatusAsync(
            NativeIdentity actor, string statusId, long revision,
            string name, string symbol, string color, string operationId,
            CancellationToken ct = default)
        {
            Writes++; LastStatusId = statusId; LastRevision = revision;
            LastName = name; LastSymbol = symbol; LastColor = color;
            return Task.FromResult(new NativeStatusMutationReceipt(
                ForgeReceipt ? "STS-OTHER" : statusId,
                name, symbol, color, revision + 1, true, 17));
        }
        public Task<NativeStatusMutationReceipt> DeactivateStatusAsync(
            NativeIdentity actor, string statusId, long revision,
            string operationId, CancellationToken ct = default)
        {
            Writes++; LastStatusId = statusId; LastRevision = revision;
            return Task.FromResult(new NativeStatusMutationReceipt(
                statusId, Holiday.Name, Holiday.Symbol, Holiday.Color,
                revision + 1, false, 0));
        }
    }
}
