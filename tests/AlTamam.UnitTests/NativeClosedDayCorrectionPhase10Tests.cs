using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeClosedDayCorrectionPhase10Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "CLOSED-DAY-NONCE");
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
    private static NativeMonthlyCellDetails Detail => new("P-1", Day, "أحمد", "123",
        "المركز", "موجود", "م", "المركز", "يدوي", "2026-01-01T00:00:00Z",
        "", "", []);

    [Fact]
    public async Task Historical_review_requires_valid_reason_and_explicit_confirmation()
    {
        var detail = Detail;
        var backend = new Fake();
        var vm = new NativeClosedDayCorrectionReview();
        Assert.True(vm.Open(backend, () => Owner, () => detail, detail));
        await vm.PreviewAsync("STS-ABSENT", "قصير");
        Assert.Equal(0, backend.Previews);
        await vm.PreviewAsync("STS-ABSENT", "تصحيح موثق من الإدارة");
        Assert.NotNull(vm.Preview);
        Assert.Empty(backend.Commits);
        await vm.ConfirmAsync();
        Assert.Single(backend.Commits);
        Assert.Equal(8, vm.Receipt!.RevisionNumber);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task Wrong_role_future_day_and_missing_saved_record_are_rejected()
    {
        var detail = Detail;
        var backend = new Fake();
        var vm = new NativeClosedDayCorrectionReview();
        Assert.False(vm.Open(backend, () => Owner with { Role = UserRole.Supervisor },
            () => detail, detail));
        var today = detail with { BusinessDate = Day.AddDays(1) };
        Assert.False(vm.Open(backend, () => Owner, () => today, today));
        var empty = detail with { AttendanceCommittedAtUtc = "" };
        Assert.False(vm.Open(backend, () => Owner, () => empty, empty));
        Assert.False(vm.Open(backend, () => Owner, () => detail with { }, detail));
        Assert.Equal(0, backend.Previews);
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
    }

    [Fact]
    public async Task Forged_preview_and_changed_context_cannot_commit()
    {
        var detail = Detail;
        NativeMonthlyCellDetails? current = detail;
        var backend = new Fake { Forge = true };
        var vm = new NativeClosedDayCorrectionReview();
        Assert.True(vm.Open(backend, () => Owner, () => current, detail));
        await vm.PreviewAsync("STS-ABSENT", "تصحيح موثق من الإدارة");
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
        backend.Forge = false;
        await vm.PreviewAsync("STS-ABSENT", "تصحيح موثق من الإدارة");
        Assert.NotNull(vm.Preview);
        current = detail with { };
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public async Task Uncertain_receipt_reuses_same_operation_and_disallows_new_preview()
    {
        var detail = Detail;
        var backend = new Fake { Lose = true };
        var vm = new NativeClosedDayCorrectionReview();
        Assert.True(vm.Open(backend, () => Owner, () => detail, detail));
        await vm.PreviewAsync("STS-ABSENT", "تصحيح موثق من الإدارة");
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.Close());
        await vm.PreviewAsync("STS-OTHER", "سبب آخر موثق للتعديل");
        Assert.Equal(1, backend.Previews);
        backend.Lose = false;
        await vm.ConfirmAsync();
        Assert.Equal(2, backend.Commits.Count);
        Assert.Equal(backend.Commits[0], backend.Commits[1]);
        Assert.False(vm.HasUnverifiedCommit);
        Assert.NotNull(vm.Receipt);
    }

    private sealed class Fake : INativeClosedDayCorrectionBackend
    {
        public int Previews;
        public bool Forge, Lose;
        public List<(string Token, string OperationId)> Commits = [];

        public Task<NativeClosedDayCorrectionPreview> PreviewAsync(NativeIdentity actor,
            NativeClosedDayCorrectionRequest request, CancellationToken ct = default)
        {
            Previews++;
            return Task.FromResult(new NativeClosedDayCorrectionPreview(
                Forge ? request with { PersonId = "OTHER" } : request,
                new string('a', 64), 7, "أحمد", "123", "المركز",
                "STS-PRESENT", "موجود", "غائب", true));
        }

        public Task<NativeClosedDayCorrectionReceipt> CommitAsync(NativeIdentity actor,
            string token, string operationId, CancellationToken ct = default)
        {
            Commits.Add((token, operationId));
            if (Lose) throw new IOException("Lost response.");
            return Task.FromResult(new NativeClosedDayCorrectionReceipt("P-1", Day, 8));
        }
    }
}
