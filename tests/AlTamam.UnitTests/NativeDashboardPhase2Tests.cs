using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 2 tests first: no DOM, no mock rows in production, no untrusted
/// session navigation; expected figures/labels reflect the old Dashboard.
/// The fake reader is used ONLY to exercise MVVM safety on Linux and Windows.
/// Production reader is verified independently with actual SQLCipher in CI.
/// </summary>
public sealed class NativeDashboardPhase2Tests
{
    private static readonly NativeIdentity Founder =
        new("USR-1", "owner", UserRole.GeneralAdministration, "LIVE_NONCE");

    [Fact]
    public async Task Unauthenticated_dashboard_never_fetches_or_shows_personnel()
    {
        var reader = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => null);
        await page.RefreshAsync();
        Assert.Empty(reader.Calls);
        Assert.False(page.IsDataConnected);
        Assert.Empty(page.Rows);
        Assert.Empty(page.Counters);
    }

    [Fact]
    public async Task Real_readonly_summary_preserves_existing_dashboard_counter_order_and_no_unprepared_card()
    {
        var reader = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => Founder);
        await page.RefreshAsync();
        Assert.True(page.IsDataConnected);
        Assert.Equal(24, page.TotalForce);
        Assert.Equal(new[] { "إجمالي القوة", "الموجود", "غائب", "المحضّرون تلقائياً", "نُقلوا اليوم" },
            page.Counters.Select(x => x.Label).ToArray());
        Assert.Equal(new long[] { 24, 15, 6, 4, 2 },
            page.Counters.Select(x => x.Count).ToArray());
        Assert.DoesNotContain(page.Counters, x => x.Label == "غير المحضرين");
        Assert.Equal("رقم خاص", page.CustomColumns.Single().Caption);
        Assert.Equal("A", page.Rows.Single().CustomCells[0]);
        Assert.Single(reader.Calls);
        Assert.Equal(100, reader.Calls.Single().PageSize);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), reader.Calls.Single().BusinessDate);
    }

    [Fact]
    public async Task Filters_search_and_pagination_preserve_backend_parameters_and_reset_page()
    {
        var reader = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => Founder);
        await page.RefreshAsync();
        await page.NextPageAsync();
        Assert.Equal(2, reader.Calls[^1].Page);
        await page.SelectSiteAsync("SITE-1");
        Assert.Equal(1, reader.Calls[^1].Page);
        Assert.Equal("SITE-1", reader.Calls[^1].SiteId);
        // Ordinary saved statuses filter the main roster; automatic
        // attendance uses a separate read-only modal (Phase 3 contract).
        await page.SelectStatusAsync("ABSENT");
        Assert.Equal("ABSENT", reader.Calls[^1].StatusId);
        await page.SearchAsync("أحمد 123");
        Assert.Equal("أحمد 123", reader.Calls[^1].SearchTerm);
        await page.SearchAsync("");
        Assert.Equal("", reader.Calls[^1].SearchTerm);
        Assert.Equal(1, reader.Calls[^1].Page);
    }

    [Fact]
    public async Task Data_from_previous_session_never_reappears_after_logout()
    {
        var reader = new Reader();
        var pending = new TaskCompletionSource<NativeDashboardSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Delayed = pending.Task;
        NativeIdentity? live = Founder;
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => live);
        var inFlight = page.RefreshAsync();
        page.Clear();
        live = null;
        pending.SetResult(Reader.ValidSnapshot(reader.Calls[0]));
        await inFlight;
        Assert.False(page.IsDataConnected);
        Assert.Empty(page.Rows);
        Assert.Empty(page.Counters);
        Assert.Equal(0, page.TotalForce);
    }

    [Fact]
    public async Task Replaced_or_expired_nonce_refuses_to_display_previous_personnel()
    {
        var reader = new Reader();
        var newPrincipal = Founder with { SessionNonce = "REPLACED" };
        NativeIdentity? live = Founder;
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => live);
        await page.RefreshAsync();
        Assert.True(page.IsDataConnected);
        live = newPrincipal;
        await page.RefreshAsync();
        Assert.False(page.IsDataConnected);
        Assert.Empty(page.Rows);
    }

    [Fact]
    public async Task Service_failure_and_wrong_filter_echo_fail_closed_without_old_counters()
    {
        var reader = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => Founder);
        await page.RefreshAsync();
        Assert.True(page.IsDataConnected);
        reader.Throws = true;
        await page.RefreshAsync();
        Assert.False(page.IsDataConnected);
        Assert.Empty(page.Rows);
        Assert.NotEmpty(page.LoadError);
        reader.Throws = false;
        reader.ReturnWrongDate = true;
        await page.RefreshAsync();
        Assert.False(page.IsDataConnected);
        Assert.Empty(page.Counters);
    }

    [Fact]
    public async Task Search_over_100_characters_is_rejected_before_any_backend_read()
    {
        var reader = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(reader, () => Founder);
        await page.SearchAsync(new string('x', 101));
        Assert.Empty(reader.Calls);
        Assert.False(page.IsDataConnected);
        Assert.NotEmpty(page.LoadError);
    }

    private sealed class Reader : INativeDashboardReader
    {
        public readonly List<NativeDashboardFilter> Calls = [];
        public Task<NativeDashboardSnapshot>? Delayed;
        public bool Throws;
        public bool ReturnWrongDate;

        public async Task<NativeDashboardSnapshot> ReadAsync(
            NativeIdentity principal, NativeDashboardFilter filter, CancellationToken ct = default)
        {
            Calls.Add(filter);
            if (Throws) throw new IOException("SQLCipher read unavailable");
            if (Delayed is { } delayed) return await delayed; // deliberately ignores ct to check generation gate
            var result = ValidSnapshot(filter);
            return ReturnWrongDate ? result with { BusinessDate = result.BusinessDate.AddDays(-1) } : result;
        }

        public static NativeDashboardSnapshot ValidSnapshot(NativeDashboardFilter request) =>
            new(request.BusinessDate, request.SiteId, 24, 4, 2,
                [new("PRESENT", "موجود", 15, "#15803D"), new("ABSENT", "غائب", 6, "#BE123C")],
                [new("SITE-1", "المركز")],
                [new("P-1", "123", "أحمد", "المركز", "موجود", "#15803D", ["A"])],
                [new("FIELD-1", "رقم خاص")], 110, request.Page, request.Page * request.PageSize < 110);
    }
}
