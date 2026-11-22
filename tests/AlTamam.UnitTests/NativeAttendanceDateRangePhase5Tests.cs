using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Phase 5 RED tests: bounded same-day leave/permission input,
/// before any UI or SQLCipher write is enabled.</summary>
public sealed class NativeAttendanceDateRangePhase5Tests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    [Theory]
    [InlineData("STS-LEAVE")]
    [InlineData("STS-PERMISSION")]
    public void Timed_status_requires_both_valid_dates(string status)
    {
        Assert.False(NativeAttendanceDateRange.TryCreate(status, Today, "", "", out _, out _));
        Assert.False(NativeAttendanceDateRange.TryCreate(status, Today, "2026-11-22", "", out _, out _));
        Assert.False(NativeAttendanceDateRange.TryCreate(status, Today, "2026-11-22", "2026-11-21", out _, out _));
        Assert.False(NativeAttendanceDateRange.TryCreate(status, Today, "2026-11-23", "2026-11-24", out _, out _));
        Assert.False(NativeAttendanceDateRange.TryCreate(status, Today, "2026-11-22", "2027-11-23", out _, out _));
    }

    [Theory]
    [InlineData("STS-LEAVE")]
    [InlineData("STS-PERMISSION")]
    public void Timed_status_accepts_bounded_inclusive_today_range(string status)
    {
        Assert.True(NativeAttendanceDateRange.TryCreate(status, Today, "2026-11-22", "2026-11-25", out var range, out _));
        Assert.NotNull(range);
        Assert.Equal("2026-11-22", range.StartDate);
        Assert.Equal("2026-11-25", range.EndDate);
    }

    [Fact]
    public void Ordinary_status_rejects_unrequested_range()
    {
        Assert.False(NativeAttendanceDateRange.TryCreate("STS-ABSENT", Today,
            "2026-11-22", "2026-11-22", out _, out _));
        Assert.True(NativeAttendanceDateRange.TryCreate("STS-ABSENT", Today,
            "", "", out var range, out _));
        Assert.Null(range);
    }

    [Theory]
    [InlineData("2026-02-30", "2026-03-01")]
    [InlineData("٢٠٢٦-١١-٢٢", "2026-11-23")]
    [InlineData("2026-11-22T00:00:00", "2026-11-23")]
    [InlineData("2026-11-22", "2026/11/23")]
    public void Rejects_malformed_or_localized_date_strings(string from, string to)
    {
        Assert.False(NativeAttendanceDateRange.TryCreate("STS-LEAVE", Today,
            from, to, out _, out _));
    }

    [Fact]
    public void Max_range_is_366_inclusive_days()
    {
        Assert.True(NativeAttendanceDateRange.TryCreate("STS-LEAVE", Today,
            "2026-11-22", "2027-11-22", out _, out _));
        Assert.False(NativeAttendanceDateRange.TryCreate("STS-LEAVE", Today,
            "2026-11-22", "2027-11-24", out _, out _));
    }
}
