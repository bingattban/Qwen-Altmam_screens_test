using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Test-first external batch exit: departure also suspends linked accounts
/// and revokes their sessions, and cannot be silently retried.
/// </summary>
public sealed class NativeBulkExternalTransferPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-EXT-BULK", "admin", UserRole.GeneralAdministration, "EXTERNAL-BULK-NONCE");
    private static readonly NativeDashboardSite[] Sites =
        [new("SITE-SOURCE", "الموقع الأول")];
    private static NativePersonnelDisplayRow Person(string id) =>
        new(1, id, id == "PRN-A" ? "12345" : "56789", "رقيب",
            "الفرد " + id, "الموقع الأول", "على القوة", true, 3, []);

    private static NativeBulkExternalTransferViewModel Build(
        FakeBackend backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool> current)
    {
        var vm = new NativeBulkExternalTransferViewModel();
        vm.AttachBackend(backend, actor, current);
        return vm;
    }

    [Fact]
    public async Task Unauthorized_or_no_session_cannot_open_external_batch()
    {
        NativeIdentity? identity = null;
        var backend = new FakeBackend();
        var rows = new[] { Person("PRN-A") };
        var vm = Build(backend, () => identity, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        identity = Manager with { Role = UserRole.Supervisor };
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Duplicate_stale_or_mixed_source_rows_are_never_eligible()
    {
        var rows = new[] { Person("PRN-A") };
        var backend = new FakeBackend();
        var vm = Build(backend, () => Manager, rows.Contains);
        vm.Open([rows[0], rows[0]], "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        vm.Open([rows[0] with { Revision = 99 }], "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        vm.Open(rows, "SITE-NOT-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        vm.Open([rows[0] with { SiteName = "جهة أخرى" }], "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task No_default_selection_and_no_preview_before_explicit_valid_external_destination()
    {
        var rows = new[] { Person("PRN-A"), Person("PRN-B") };
        var backend = new FakeBackend();
        var vm = Build(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.True(vm.IsOpen);
        Assert.Equal(0, vm.SelectedCount);
        vm.ExternalDestinationName = "جهة";
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        vm.Options[0].IsSelected = true;
        vm.ExternalDestinationName = "جهة خارجية معتمدة";
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal(0, backend.Commits);
        Assert.Equal("جهة خارجية معتمدة", vm.Preview?.DestinationName);
        Assert.Equal(["PRN-A"], vm.Preview?.Persons.Select(p => p.PersonId));
    }

    [Fact]
    public async Task Full_100_member_limit_and_exact_one_to_one_canonical_preview()
    {
        var rows = Enumerable.Range(1, 101).Select(i =>
            new NativePersonnelDisplayRow(i, "PERSON-" + i, i.ToString("D5"),
                "رقيب", "فرد " + i, "الموقع الأول", "على القوة", true, 2, []))
            .ToArray();
        var backend = new FakeBackend();
        var vm = Build(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        Assert.False(vm.IsOpen);
        vm.Open(rows.Take(100).ToArray(), "SITE-SOURCE", Sites);
        Assert.True(vm.IsOpen);
        foreach (var option in vm.Options) option.IsSelected = true;
        vm.ExternalDestinationName = "جهة خارجية معتمدة";
        await vm.PreviewAsync();
        Assert.Equal(100, vm.Preview?.Persons.Count);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Altered_member_revision_or_envelope_fails_the_entire_batch()
    {
        foreach (var forged in new[] { "person", "revision", "count", "external", "token", "destination" })
        {
            var rows = new[] { Person("PRN-A"), Person("PRN-B") };
            var backend = new FakeBackend { Forge = forged };
            var vm = Build(backend, () => Manager, rows.Contains);
            vm.Open(rows, "SITE-SOURCE", Sites);
            foreach (var option in vm.Options) option.IsSelected = true;
            vm.ExternalDestinationName = "جهة خارجية معتمدة";
            await vm.PreviewAsync();
            Assert.Null(vm.Preview);
            Assert.False(vm.CanConfirm);
            Assert.Equal(0, backend.Commits);
        }
    }

    [Fact]
    public async Task Editing_destination_or_selected_people_revokes_both_approvals()
    {
        var rows = new[] { Person("PRN-A"), Person("PRN-B") };
        var backend = new FakeBackend();
        var vm = Build(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.ExternalDestinationName = "جهة خارجية معتمدة";
        await vm.PreviewAsync();
        vm.IdentityReviewed = true;
        vm.AccountSuspensionAcknowledged = true;
        Assert.True(vm.CanConfirm);
        vm.Options[1].IsSelected = true;
        Assert.False(vm.CanConfirm);
        Assert.False(vm.IdentityReviewed);
        Assert.False(vm.AccountSuspensionAcknowledged);
        await vm.PreviewAsync();
        vm.IdentityReviewed = true;
        vm.AccountSuspensionAcknowledged = true;
        vm.ExternalDestinationName = "جهة ثانية";
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Linked_accounts_revocation_needs_separate_explicit_acknowledgment()
    {
        var rows = new[] { Person("PRN-A"), Person("PRN-B") };
        var backend = new FakeBackend();
        var vm = Build(backend, () => Manager, rows.Contains);
        vm.Open(rows, "SITE-SOURCE", Sites);
        foreach (var option in vm.Options) option.IsSelected = true;
        vm.ExternalDestinationName = "جهة خارجية معتمدة";
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        vm.IdentityReviewed = true;
        Assert.False(vm.CanConfirm);
        vm.AccountSuspensionAcknowledged = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
        Assert.Equal(2, vm.Receipt?.Count);
        Assert.Equal("جهة خارجية معتمدة", backend.LastDestination);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
        Assert.Single(backend.OperationIds);
    }

    [Fact]
    public async Task Logout_during_delayed_preview_never_reopens_or_commits()
    {
        var rows = new[] { Person("PRN-A") };
        NativeIdentity? actor = Manager;
        bool current = true;
        var backend = new FakeBackend();
        var pending = new TaskCompletionSource<NativeBulkExternalTransferPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        var vm = Build(backend, () => actor, row => current && rows.Contains(row));
        vm.Open(rows, "SITE-SOURCE", Sites);
        vm.Options[0].IsSelected = true;
        vm.ExternalDestinationName = "جهة خارجية معتمدة";
        Task loading = vm.PreviewAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        current = false;
        vm.ForceCancel();
        pending.SetResult(backend.Make(backend.Drafts[0]));
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Unknown_write_or_forged_receipt_locks_same_manager_session()
    {
        foreach (bool loseReply in new[] { true, false })
        {
            var rows = new[] { Person("PRN-A") };
            var backend = new FakeBackend { LostReply = loseReply, BadReceipt = !loseReply };
            var vm = Build(backend, () => Manager, rows.Contains);
            vm.Open(rows, "SITE-SOURCE", Sites);
            vm.Options[0].IsSelected = true;
            vm.ExternalDestinationName = "جهة خارجية معتمدة";
            await vm.PreviewAsync();
            vm.IdentityReviewed = true;
            vm.AccountSuspensionAcknowledged = true;
            await vm.ConfirmAsync();
            Assert.True(vm.HasUnverifiedCommit);
            Assert.Null(vm.Receipt);
            await vm.ConfirmAsync();
            Assert.Equal(1, backend.Commits);
            vm.ForceCancel();
            vm.Open(rows, "SITE-SOURCE", Sites);
            Assert.False(vm.IsOpen);
        }
    }

    private sealed class FakeBackend : INativeBulkExternalTransferBackend
    {
        public int Previews, Commits;
        public string Forge = "";
        public bool LostReply, BadReceipt;
        public string LastDestination = "";
        public Task<NativeBulkExternalTransferPreview>? Pending;
        public List<NativeBulkExternalTransferDraft> Drafts { get; } = [];
        public List<string> OperationIds { get; } = [];

        public NativeBulkExternalTransferPreview Make(NativeBulkExternalTransferDraft d)
        {
            var people = d.Persons.Select(p =>
                new NativePersonnelMovementPerson(
                    Forge == "person" ? "INJECTED" : p.PersonId,
                    p.Name, p.ServiceNumberA,
                    Forge == "revision" ? p.Revision + 1 : p.Revision,
                    d.SourceSiteId, d.SourceSiteName, "ASG-" + p.PersonId)).ToArray();
            if (Forge == "count") people = people.Take(1).ToArray();
            return new NativeBulkExternalTransferPreview(d,
                DateTime.Now.ToString("yyyy-MM-dd"),
                Forge == "token" ? "bad" : new string('a', 64),
                Forge == "destination" ? "جهة مختلفة" : d.ExternalDestinationName,
                Forge != "external", people);
        }

        public Task<NativeBulkExternalTransferPreview> PreviewExternalBatchAsync(
            NativeIdentity actor, NativeBulkExternalTransferDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return Pending ?? Task.FromResult(Make(draft));
        }

        public Task<NativeBulkExternalTransferReceipt> CommitExternalBatchAsync(
            NativeIdentity actor, NativeBulkExternalTransferPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Commits++;
            OperationIds.Add(operationId);
            LastDestination = preview.DestinationName;
            if (LostReply) throw new IOException("Possibly committed: reply lost");
            return Task.FromResult(new NativeBulkExternalTransferReceipt(
                preview.BusinessDate,
                BadReceipt ? 999 : preview.Persons.Count,
                preview.DestinationName, true));
        }
    }
}
