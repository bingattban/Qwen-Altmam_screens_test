using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAdministrativeCatchUpPhase10Tests
{
    private static readonly DateOnly Today = new(2026,11,25);
    private static readonly NativeAdministrativeCatchUpRequest Request =
        new(Today.AddDays(-1), "استدراك موثق للتحضير الناقص");
    private static NativeAdministrativeCatchUpPreview Preview =>
        new(Request, new string('a',64), new string('B',64), 5, 12, true);

    [Fact]
    public void Request_accepts_only_seven_previous_days_and_explicit_reason()
    {
        Assert.True(Request.IsValid(Today));
        Assert.True((Request with { BusinessDate = Today.AddDays(-7) }).IsValid(Today));
        Assert.False((Request with { BusinessDate = Today.AddDays(-8) }).IsValid(Today));
        Assert.False((Request with { BusinessDate = Today }).IsValid(Today));
        Assert.False((Request with { BusinessDate = Today.AddDays(1) }).IsValid(Today));
        Assert.False((Request with { Reason = "قصير" }).IsValid(Today));
        Assert.False((Request with { Reason = "استدراك\nغير صالح" }).IsValid(Today));
        Assert.False((Request with { Reason = " " + Request.Reason }).IsValid(Today));
        Assert.False((Request with { Reason = new string('a',401) }).IsValid(Today));
    }

    [Fact]
    public void Preview_requires_exact_date_reason_counts_fingerprint_and_confirmation()
    {
        var preview = Preview;
        Assert.True(preview.Matches(Request, Today));
        Assert.False((preview with { Request = Request with { BusinessDate = Today.AddDays(-2) } }).Matches(Request, Today));
        Assert.False((preview with { Request = Request with { Reason = "سبب آخر للتحضير الناقص" } }).Matches(Request, Today));
        Assert.False((preview with { PendingCount = 0 }).Matches(Request, Today));
        Assert.False((preview with { EligibleCount = 4 }).Matches(Request, Today));
        Assert.False((preview with { Token = "bad" }).Matches(Request, Today));
        Assert.False((preview with { Fingerprint = new string('z',64) }).Matches(Request, Today));
        Assert.False((preview with { RequiresExplicitConfirmation = false }).Matches(Request, Today));
    }

    [Fact]
    public void Receipt_accepts_only_canonical_date_and_count_with_no_overstatement()
    {
        var preview = Preview;
        var receipt = new NativeAdministrativeCatchUpReceipt(Request.BusinessDate,5,3,"تم الاستدراك",true);
        Assert.True(preview.MatchesReceipt(receipt));
        Assert.True(preview.MatchesReceipt(receipt with { AppliedCount = 0 }));
        Assert.False(preview.MatchesReceipt(receipt with { AppliedCount = 6 }));
        Assert.False(preview.MatchesReceipt(receipt with { AppliedCount = -1 }));
        Assert.False(preview.MatchesReceipt(receipt with { PendingBefore = 4 }));
        Assert.False(preview.MatchesReceipt(receipt with { BusinessDate = Today }));
        Assert.False(preview.MatchesReceipt(receipt with { IsCatchUpRun = false }));
        Assert.False(preview.MatchesReceipt(receipt with { Summary = "" }));
    }

    [Fact]
    public void Approved_token_remains_structurally_valid_for_exact_replay_after_window_closes()
    {
        var preview = Preview;
        Assert.False(preview.Matches(Request,Today.AddDays(8)));
        Assert.True(preview.IsWellFormed);
        Assert.True(preview.MatchesReceipt(new(Request.BusinessDate,5,5,"تم الاستدراك",true)));
        Assert.False((preview with { RequiresExplicitConfirmation = false }).IsWellFormed);
    }
}
