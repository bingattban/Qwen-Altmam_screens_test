using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 slice 8 RED tests written BEFORE the native export command/backend.
/// Group export requires a new operator preview, exact source evidence and
/// explicit confirmation. No file write, path or retry originates in the VM.
/// </summary>
public sealed class NativeGroupExcelExportPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("G-EXPORT", "admin", UserRole.GeneralAdministration, "G-EXPORT-NONCE");
    private static readonly string[] Ids = ["PER-0001", "PER-0002"];
    private static NativePersonnelGroupReportRequest Request() =>
        new(Ids.ToArray(), "2026-01-01", "2026-11-30", 2, 1);

    private static NativeGroupExcelPreview Preview(NativePersonnelGroupReportRequest r) =>
        new(r.PersonIds.ToArray(), r.DateFrom, r.DateTo, r.SiteId, r.StatusId,
            r.MovementType, r.PersonIds.Count, 30, 4,
            new string('a', 64), "تقرير_جماعي_20261124.xlsx");

    private static NativeGroupExcelReceipt Receipt(NativeGroupExcelPreview preview) =>
        new(@"C:\Exports\تقرير_جماعي_20261124.xlsx", preview.PersonCount,
            preview.AttendanceTotal, preview.MovementTotal,
            preview.Fingerprint, 4, 21);

    [Fact]
    public async Task Preview_then_explicit_confirm_uses_exact_ids_filters_and_fingerprint()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        NativePersonnelGroupReportRequest? request = Request();
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => actor, () => request);
        await vm.OpenPreviewAsync();
        Assert.True(vm.IsOpen);
        Assert.True(vm.CanConfirm);
        Assert.Equal(request!.PersonIds, backend.Last!.PersonIds);
        Assert.Equal(0, backend.Exports);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Equal(new string('a', 64), backend.Fingerprint);
        Assert.Equal(4, vm.Receipt!.SheetCount);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Preview_and_confirm_are_denied_for_empty_or_duplicate_ids()
    {
        var backend = new FakeBackend();
        NativePersonnelGroupReportRequest? request = Request() with
            { PersonIds = ["PER-0001", "PER-0001"] };
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => Manager, () => request);
        await vm.OpenPreviewAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Null(vm.Preview);
        request = Request() with { PersonIds = [] };
        await vm.OpenPreviewAsync();
        Assert.Equal(0, backend.Previews);
    }

    [Fact]
    public async Task Changed_group_selection_or_date_after_preview_refuses_stale_export()
    {
        var backend = new FakeBackend();
        NativePersonnelGroupReportRequest? request = Request();
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => Manager, () => request);
        await vm.OpenPreviewAsync();
        request = request with { DateFrom = "2026-02-01" };
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Exports);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Revoked_live_role_or_new_session_cannot_export_previous_preview()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => actor, Request);
        await vm.OpenPreviewAsync();
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Exports);
        Assert.False(vm.CanConfirm);
        actor = Manager with { SessionNonce = "NEW-NONCE" };
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Exports);
    }

    [Fact]
    public async Task Forged_source_preview_or_receipt_is_not_shown_as_success()
    {
        var backend = new FakeBackend { ForgePreview = true };
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => Manager, Request);
        await vm.OpenPreviewAsync();
        Assert.Null(vm.Preview);
        Assert.NotEmpty(vm.Error);
        backend.ForgePreview = false;
        backend.ForgeReceipt = true;
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        Assert.Null(vm.Receipt);
        Assert.NotEmpty(vm.Error);
    }

    [Fact]
    public async Task Failing_export_clears_approval_and_never_auto_retries()
    {
        var backend = new FakeBackend { FailExport = true };
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => Manager, Request);
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        Assert.Null(vm.Preview);
        Assert.Null(vm.Receipt);
        Assert.False(vm.CanConfirm);
        Assert.Equal(1, backend.Exports);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
    }

    [Fact]
    public async Task Late_preview_after_close_cannot_repopulate_sensitive_export()
    {
        var backend = new FakeBackend();
        var delay = new TaskCompletionSource<NativeGroupExcelPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = delay.Task;
        var vm = new NativePersonnelGroupExcelViewModel();
        vm.Attach(backend, () => Manager, Request);
        Task old = vm.OpenPreviewAsync();
        vm.Close();
        delay.SetResult(Preview(Request()));
        await old;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Null(vm.Receipt);
    }

    private sealed class FakeBackend : INativePersonnelGroupExcelBackend
    {
        public int Previews, Exports;
        public string? Fingerprint;
        public NativePersonnelGroupReportRequest? Last;
        public bool ForgePreview, ForgeReceipt, FailExport;
        public Task<NativeGroupExcelPreview>? Pending;

        public Task<NativeGroupExcelPreview> PreviewGroupExcelAsync(
            NativeIdentity actor, NativePersonnelGroupReportRequest request,
            CancellationToken ct = default)
        {
            Previews++;
            Last = request;
            if (Pending is not null) return Pending;
            NativeGroupExcelPreview answer = Preview(request);
            if (ForgePreview) answer = answer with { DateTo = "1900-01-01" };
            return Task.FromResult(answer);
        }

        public Task<NativeGroupExcelReceipt> ExportGroupExcelAsync(
            NativeIdentity actor, NativePersonnelGroupReportRequest request,
            string fingerprint, CancellationToken ct = default)
        {
            Exports++;
            Last = request;
            Fingerprint = fingerprint;
            if (FailExport) throw new IOException("synthetic output error");
            NativeGroupExcelReceipt answer = Receipt(Preview(request));
            if (ForgeReceipt) answer = answer with { Fingerprint = new string('b', 64) };
            return Task.FromResult(answer);
        }
    }
}
