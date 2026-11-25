using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeHistoricalRangePhase10Tests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly NativeHistoricalRangeRequest Request = new(
        "P-1", Today.AddDays(-2), Today, "STS-ABSENT", "تصحيح معتمد للسجل التاريخي");

    private static NativeHistoricalRangeDay Day(DateOnly date, long revision = 0) =>
        new(date, revision > 0, revision, "A-1", "SITE-1", "المركز",
            revision > 0 ? "STS-PRESENT" : "__UNPREPARED__",
            revision > 0 ? "موجود" : "غير محضر", "غياب", "غ", "#FF0000");

    private static NativeHistoricalRangePreview Preview(params NativeHistoricalRangeDay[] days) =>
        new(Request, new string('a', 64), "أحمد", "123", days, true);

    [Fact]
    public void Request_bounds_match_canonical_inclusive_62_day_policy()
    {
        Assert.True(Request.IsValid(Today));
        Assert.True((Request with { From = Today.AddDays(-61) }).IsValid(Today));
        Assert.False((Request with { From = Today.AddDays(-62) }).IsValid(Today));
        Assert.False((Request with { To = Today.AddDays(1) }).IsValid(Today));
        Assert.False((Request with { From = Today.AddDays(1) }).IsValid(Today));
        Assert.False((Request with { Reason = "قصير" }).IsValid(Today));
    }

    [Fact]
    public void Preview_preserves_skipped_days_and_accepts_new_or_existing_records()
    {
        var preview = Preview(Day(Request.From), Day(Request.To, 7));
        Assert.True(preview.Matches(Request, Today));
        Assert.Equal(1, preview.SkippedDays);
        Assert.True(preview.MatchesReceipt(new NativeHistoricalRangeReceipt("P-1", Request.From, 8)));
        Assert.False(preview.MatchesReceipt(new NativeHistoricalRangeReceipt("P-1", Request.From, 1)));
        var inserted = Preview(Day(Request.From));
        Assert.True(inserted.MatchesReceipt(new NativeHistoricalRangeReceipt("P-1", Request.From, 1)));
    }

    [Fact]
    public void Preview_rejects_duplicate_out_of_range_or_forged_days()
    {
        var day = Day(Request.From);
        Assert.False(Preview(day, day).Matches(Request, Today));
        Assert.False(Preview(Day(Request.To), day).Matches(Request, Today));
        Assert.False(Preview(Day(Request.From.AddDays(-1))).Matches(Request, Today));
        Assert.False(Preview().Matches(Request, Today));
        Assert.False(Preview(day with { HasExistingRecord = true }).Matches(Request, Today));
        Assert.False(Preview(day with { PreviousRevision = long.MaxValue }).Matches(Request, Today));
        Assert.False(Preview(day with { BeforeStatusId = Request.StatusId }).Matches(Request, Today));
        Assert.False((Preview(day) with { Request = Request with { PersonId = "OTHER" } }).Matches(Request, Today));
        Assert.False((Preview(day) with { RequiresExplicitConfirmation = false }).Matches(Request, Today));
        Assert.False((Preview(day) with { Token = "wrong" }).Matches(Request, Today));
    }

    [Fact]
    public void Range_receipt_must_match_person_start_and_final_approved_revision()
    {
        var preview = Preview(Day(Request.From, 5), Day(Request.To));
        Assert.True(preview.MatchesReceipt(new("P-1", Request.From, 1)));
        Assert.False(preview.MatchesReceipt(new("OTHER", Request.From, 1)));
        Assert.False(preview.MatchesReceipt(new("P-1", Request.To, 1)));
        Assert.False(preview.MatchesReceipt(new("P-1", Request.From, 6)));
    }
}
