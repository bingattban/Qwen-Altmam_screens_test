using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 slice 9 RED tests for native individual-history XLSX: approval
/// bound to immutable PersonId, current report totals, and exact filters.
/// The UI is never allowed to write a file or replay an uncertain commit.
/// </summary>
public sealed class NativeIndividualExcelPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("RPT-XLSX", "admin", UserRole.GeneralAdministration, "RPT-XLSX-SESSION");
    private static NativeIndividualReportPages Request() =>
        new("PER-IMMUTABLE-0001", DateFrom: "2026-01-01", DateTo: "2026-11-30",
            MovementType: 2, SiteId: "SITE-001", StatusId: "STATUS-1");
    private static NativePersonnelReportSnapshot Report(NativeIndividualReportPages pages) =>
        new(pages.PersonId, "الاسم الحالي", "000012", "2024-01-01", true,
            12, 3, 1, 100, 1, false, 1, false, 1, false,
            [new("STATUS-1", "حاضر", 12)],
            [new("2026-01-01","حاضر","الموقع القديم","MANUAL","الاسم القديم","001",1)],
            [new("نقل خارجي","موقع أ","موقع ب","2026-02-02","الاسم القديم","001","المدير")],
            [new("الاسم القديم","001","2024-01-01",null,"تغيير الاسم")])
        {
            DateFrom = pages.DateFrom, DateTo = pages.DateTo,
            MovementType = pages.MovementType, SiteId = pages.SiteId,
            StatusId = pages.StatusId,
            SiteTotal = 1, RevisionTotal = 2, AuditTotal = 1,
            AvailableSites = [new("SITE-001","موقع قديم")],
            AvailableStatuses = [new("STATUS-1","حاضر")],
            MonthlyTotals = [new("2026-01","STATUS-1","حاضر",12)]
        };

    private static NativeIndividualExcelPreview Preview(NativeIndividualReportPages p) =>
        new(p.PersonId, "الاسم الحالي", "000012", p.DateFrom, p.DateTo,
            p.SiteId, p.StatusId, p.MovementType, 12, 3, 1, 1, 2, 1,
            new string('a',64), "تقرير_فردي_20261124.xlsx");

    private sealed class Fake : INativePersonnelIndividualExcelBackend
    {
        public int Previews, Exports;
        public NativeIndividualReportPages? Last;
        public string? Approved;
        public bool ForgePreview, ForgeReceipt, FailExport;
        public Task<NativeIndividualExcelPreview>? Pending;
        public Task<NativeIndividualExcelPreview> PreviewAsync(
            NativeIdentity actor, NativeIndividualReportPages p,
            CancellationToken ct = default)
        {
            Previews++; Last = p;
            if (Pending is { } pending) return pending;
            var preview = Preview(p);
            if (ForgePreview) preview = preview with { PersonId = "PER-FORGED" };
            return Task.FromResult(preview);
        }
        public Task<NativeIndividualExcelReceipt> ExportAsync(
            NativeIdentity actor, NativeIndividualReportPages p,
            string fingerprint, CancellationToken ct = default)
        {
            Exports++; Last = p; Approved = fingerprint;
            if (FailExport) throw new IOException("Synthetic failure");
            var answer = new NativeIndividualExcelReceipt(
                @"C:\Reports\تقرير_فردي_20261124.xlsx", fingerprint, 12, 3, 1, 1, 2, 1, 8);
            if (ForgeReceipt) answer = answer with { Fingerprint = new string('b',64) };
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task Explicit_preview_then_confirmation_preserves_immutable_person_and_filters()
    {
        var backend = new Fake();
        var request = Request();
        NativeIndividualReportPages? current = request;
        NativePersonnelReportSnapshot? shown = Report(request);
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => Manager, () => current, () => shown);
        await vm.OpenPreviewAsync();
        Assert.Equal(0, backend.Exports);
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Equal(request.PersonId, backend.Last!.PersonId);
        Assert.Equal(request.SiteId, backend.Last.SiteId);
        Assert.Equal(request.StatusId, backend.Last.StatusId);
        Assert.Equal(request.DateFrom, backend.Last.DateFrom);
        Assert.Equal(new string('a',64), backend.Approved);
        Assert.Equal(8, vm.Receipt!.SheetCount);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Displayed_source_totals_or_person_must_match_excel_preview()
    {
        var backend = new Fake();
        var request = Request();
        NativePersonnelReportSnapshot shown = Report(request) with { AttendanceTotal = 77 };
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => Manager, Request, () => shown);
        await vm.OpenPreviewAsync();
        Assert.Null(vm.Preview);
        Assert.NotEmpty(vm.Error);
        shown = Report(request);
        vm.Close();
        await vm.OpenPreviewAsync();
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public async Task Wrong_person_echo_and_forged_export_receipt_fail_closed()
    {
        var backend = new Fake { ForgePreview = true };
        var vm = new NativeIndividualExcelViewModel();
        var req = Request();
        vm.Attach(backend, () => Manager, () => req, () => Report(req));
        await vm.OpenPreviewAsync();
        Assert.Null(vm.Preview);
        Assert.NotEmpty(vm.Error);
        vm.Close();
        backend.ForgePreview = false;
        backend.ForgeReceipt = true;
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        Assert.Null(vm.Receipt);
        Assert.NotEmpty(vm.Error);
    }

    [Fact]
    public async Task Session_or_filter_change_between_preview_and_confirm_denies_export()
    {
        var backend = new Fake();
        NativeIdentity? actor = Manager;
        NativeIndividualReportPages? req = Request();
        NativePersonnelReportSnapshot? shown = Report(req);
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => actor, () => req, () => shown);
        await vm.OpenPreviewAsync();
        req = req with { DateFrom = "2026-02-01" };
        await vm.ConfirmAsync();
        Assert.Equal(0,backend.Exports);
        req = Request();
        vm.Close();
        await vm.OpenPreviewAsync();
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.ConfirmAsync();
        Assert.Equal(0,backend.Exports);
    }

    [Fact]
    public async Task Failure_never_auto_replays_uncertain_Excel_write()
    {
        var backend = new Fake { FailExport = true };
        var req = Request();
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => Manager, () => req, () => Report(req));
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Exports);
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Exports);
        Assert.Null(vm.Receipt);
    }

    [Fact]
    public async Task Late_inflight_preview_cannot_reopen_closed_individual_export()
    {
        var backend = new Fake();
        var promise = new TaskCompletionSource<NativeIndividualExcelPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = promise.Task;
        var req = Request();
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => Manager, () => req, () => Report(req));
        Task old = vm.OpenPreviewAsync();
        vm.Close();
        promise.SetResult(Preview(req));
        await old;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public void Command_notifies_after_original_individual_history_finishes_loading()
    {
        var backend = new Fake();
        NativePersonnelReportSnapshot? shown = null;
        var req = Request();
        var vm = new NativeIndividualExcelViewModel();
        vm.Attach(backend, () => Manager, () => shown is null?null:req, () => shown);
        Assert.False(vm.OpenPreviewCommand.CanExecute(null));
        int notifications=0;
        vm.OpenPreviewCommand.CanExecuteChanged += (_, _) => notifications++;
        shown=Report(req);
        vm.NotifySourceChanged();
        Assert.True(vm.OpenPreviewCommand.CanExecute(null));
        Assert.True(notifications>0);
    }
}
