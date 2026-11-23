using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Tests FIRST for atomic internal batch movement on canonical SQLCipher.</summary>
public sealed class NativeBulkInternalTransferPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-BULK", "manager", UserRole.GeneralAdministration, "BULK-SESSION");
    private static readonly NativeDashboardSite[] Sites =
        [new("SITE-SOURCE", "الموقع الأول"), new("SITE-TARGET", "الموقع الثاني")];

    private static NativePersonnelDisplayRow Row(string id, long version = 3) =>
        new(1, id, id == "PRN-A" ? "12345" : id == "PRN-B" ? "54321" : "77777",
            "رقيب", "الفرد " + id, "الموقع الأول", "على القوة", true, version, []);

    private static NativeBulkInternalTransferViewModel Make(
        FakeBackend backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool> isCurrent)
    {
        var vm = new NativeBulkInternalTransferViewModel();
        vm.AttachBackend(backend, actor, isCurrent);
        return vm;
    }

    [Fact]
    public async Task Non_admin_or_missing_session_never_opens_or_writes()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A") };
        var vm = Make(backend, () => actor, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Wrong_source_stale_row_or_duplicate_ids_cannot_open()
    {
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A") };
        var vm = Make(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-TARGET", Sites);
        Assert.False(vm.IsOpen);
        vm.Open([rows[0], rows[0]], "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        vm.Open([rows[0] with { Revision = 9 }], "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Previews);
    }

    [Fact]
    public async Task Explicit_page_selection_1_to_100_only_and_preview_never_writes()
    {
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A"), Row("PRN-B"), Row("PRN-C") };
        var vm = Make(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.True(vm.IsOpen);
        Assert.Equal(0, vm.SelectedCount);
        vm.DestinationSiteId = "SITE-TARGET";
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        vm.Options[0].IsSelected = true;
        vm.Options[2].IsSelected = true;
        Assert.Equal(2, vm.SelectedCount);
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal(["PRN-A", "PRN-C"], backend.Drafts[0].Persons.Select(x => x.PersonId));
        Assert.Equal(2, vm.Preview?.Persons.Count);
        Assert.Equal(0, backend.Writes);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Forged_member_or_revision_invalidates_whole_batch()
    {
        foreach (string mismatch in new[] { "member", "revision", "count", "token" })
        {
            var backend = new FakeBackend { Forge = mismatch };
            var rows = new[] { Row("PRN-A"), Row("PRN-B") };
            var vm = Make(backend, () => Manager, rows.Contains);
            vm.Open(rows, "SITE-SOURCE", Sites);
            vm.Options[0].IsSelected = true;
            vm.Options[1].IsSelected = true;
            vm.DestinationSiteId = "SITE-TARGET";
            await vm.PreviewAsync();
            Assert.Null(vm.Preview);
            Assert.False(vm.CanConfirm);
            Assert.Equal(0, backend.Writes);
        }
    }

    [Fact]
    public async Task Changing_selection_or_destination_invalidates_original_preview_and_consent()
    {
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A"), Row("PRN-B") };
        var vm = Make(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.DestinationSiteId = "SITE-TARGET";
        await vm.PreviewAsync();
        vm.ReviewAcknowledged = true;
        Assert.True(vm.CanConfirm);
        vm.Options[1].IsSelected = true;
        Assert.Null(vm.Preview);
        Assert.False(vm.ReviewAcknowledged);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
        await vm.PreviewAsync();
        vm.ReviewAcknowledged = true;
        vm.DestinationSiteId = "SITE-SOURCE";
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task Explicit_two_person_review_uses_one_canonical_atomic_commit()
    {
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A"), Row("PRN-B") };
        var vm = Make(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.Options[1].IsSelected = true;
        vm.DestinationSiteId = "SITE-TARGET";
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        vm.ReviewAcknowledged = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal(2, vm.Receipt?.Count);
        Assert.Equal("الموقع الثاني", vm.Receipt?.DestinationName);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Single(backend.OperationIds);
    }

    [Fact]
    public async Task Logout_or_replaced_page_drops_delayed_preview_and_prevents_write()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var rows = new[] { Row("PRN-A") };
        bool current = true;
        var vm = Make(backend, () => actor, r => current && rows.Contains(r));
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.DestinationSiteId = "SITE-TARGET";
        var pending = new TaskCompletionSource<NativeBulkInternalTransferPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task task = vm.PreviewAsync();
        current = false;
        actor = Manager with { SessionNonce = "OTHER" };
        vm.ForceCancel();
        pending.SetResult(backend.Generate(backend.Drafts[0]));
        await task;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Commit_lost_reply_or_bad_receipt_never_resends_in_same_session()
    {
        foreach (bool fail in new[] { true, false })
        {
            var backend = new FakeBackend { LostReply = fail, BadReceipt = !fail };
            var rows = new[] { Row("PRN-A") };
            var vm = Make(backend, () => Manager, rows.Contains);
            vm.Open(rows, "SITE-SOURCE", Sites);
            vm.Options[0].IsSelected = true;
            vm.DestinationSiteId = "SITE-TARGET";
            await vm.PreviewAsync();
            vm.ReviewAcknowledged = true;
            await vm.ConfirmAsync();
            Assert.Equal(1, backend.Writes);
            Assert.True(vm.HasUnverifiedCommit);
            Assert.Null(vm.Receipt);
            await vm.ConfirmAsync();
            vm.ForceCancel();
            vm.Open(rows, "SITE-SOURCE", Sites);
            Assert.False(vm.IsOpen);
            Assert.Equal(1, backend.Writes);
        }
    }

    [Fact]
    public async Task Replaced_roster_member_after_preview_prevents_any_commit()
    {
        var backend = new FakeBackend();
        var rows = new[] { Row("PRN-A") };
        bool current = true;
        var vm = Make(backend, () => Manager, r => current && rows.Contains(r));
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.DestinationSiteId = "SITE-TARGET";
        await vm.PreviewAsync();
        vm.ReviewAcknowledged = true;
        current = false;
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    private sealed class FakeBackend : INativeBulkInternalTransferBackend
    {
        public int Previews, Writes;
        public string Forge = "";
        public bool LostReply, BadReceipt;
        public Task<NativeBulkInternalTransferPreview>? Pending;
        public readonly List<NativeBulkInternalTransferDraft> Drafts = [];
        public readonly List<string> OperationIds = [];

        public NativeBulkInternalTransferPreview Generate(NativeBulkInternalTransferDraft draft)
        {
            var people = draft.Persons.Select(x => new NativePersonnelMovementPerson(
                Forge == "member" ? "INJECTED" : x.PersonId,
                x.Name, x.ServiceNumberA,
                Forge == "revision" ? x.Revision + 1 : x.Revision,
                draft.SourceSiteId, draft.SourceSiteName,
                "ASG-" + x.PersonId)).ToArray();
            return new NativeBulkInternalTransferPreview(draft,
                DateTime.Now.ToString("yyyy-MM-dd"),
                Forge == "token" ? "bad" : new string('a', 64),
                "الموقع الثاني",
                Forge == "count" ? people.Take(1).ToArray() : people);
        }

        public Task<NativeBulkInternalTransferPreview> PreviewInternalBatchAsync(
            NativeIdentity actor, NativeBulkInternalTransferDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return Pending ?? Task.FromResult(Generate(draft));
        }

        public Task<NativeBulkInternalTransferReceipt> CommitInternalBatchAsync(
            NativeIdentity actor, NativeBulkInternalTransferPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            OperationIds.Add(operationId);
            if (LostReply) throw new IOException("Network reply lost");
            return Task.FromResult(new NativeBulkInternalTransferReceipt(
                preview.BusinessDate,
                BadReceipt ? 999 : preview.Persons.Count,
                preview.DestinationName));
        }
    }
}
