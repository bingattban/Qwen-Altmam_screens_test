using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 3 tests-first: canonical auto-attendance deadline is display-only;
/// auto-prepared register is isolated read-only SQLCipher data, not a filter
/// secretly applied to the main roster. The old scheduler remains authoritative.
/// </summary>
public sealed class NativeAutoPreparedRegisterPhase3Tests
{
    private static readonly NativeIdentity Founder =
        new("USR-01", "owner", UserRole.GeneralAdministration, "PHASE3-LIVE-NONCE");

    [Fact]
    public async Task Before_auth_no_policy_fetch_no_register_and_no_fake_clock()
    {
        var fake = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => null);
        await page.LoadAutoPolicyAsync();
        await page.OpenAutoRegisterAsync();
        page.TickClock(new DateTime(2026, 11, 22, 7, 59, 59));
        Assert.Equal(0, fake.PolicyReads);
        Assert.Empty(fake.RegisterCalls);
        Assert.False(page.AutoRegister.IsOpen);
        Assert.Equal("", page.CountdownLabel);
    }

    [Fact]
    public async Task Native_countdown_uses_configured_SQLCipher_HH_mm_and_never_runs_scheduler()
    {
        var fake = new Reader { DueTime = "09:15" };
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.LoadAutoPolicyAsync();
        page.TickClock(new DateTime(2026, 11, 22, 9, 14, 59));
        Assert.Contains("09:15", page.CountdownLabel);
        Assert.Contains("00:00:01", page.CountdownLabel);
        page.TickClock(new DateTime(2026, 11, 22, 9, 15, 0));
        Assert.Contains("حان موعد", page.CountdownLabel);
        Assert.Equal(1, fake.PolicyReads);
        Assert.Empty(fake.RegisterCalls);
        Assert.Equal(0, fake.SchedulerWrites);
    }

    [Fact]
    public async Task Auto_counter_opens_separate_date_site_scoped_register_not_main_filter()
    {
        var fake = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.OpenAutoRegisterAsync();
        Assert.True(page.AutoRegister.IsOpen);
        Assert.Single(fake.RegisterCalls);
        Assert.Equal("__AUTO_PREPARED__", fake.RegisterCalls[0].StatusId);
        Assert.Equal(100, fake.RegisterCalls[0].PageSize);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), fake.RegisterCalls[0].BusinessDate);
        Assert.Equal("", page.SelectedStatusId);
        Assert.Single(page.AutoRegister.Rows);
        Assert.Equal("تحضير تلقائي 08:00", page.AutoRegister.Rows[0].AutoSource);
        Assert.Equal("FIELD-A", page.AutoRegister.CustomColumns[0].ColumnId);
    }

    [Fact]
    public async Task Register_search_and_paging_are_server_scoped_without_unbounded_rows()
    {
        var fake = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.OpenAutoRegisterAsync();
        await page.AutoRegister.NextPageAsync();
        Assert.Equal(2, fake.RegisterCalls[^1].Page);
        await page.AutoRegister.SearchAsync("أحمد 123");
        Assert.Equal(1, fake.RegisterCalls[^1].Page);
        Assert.Equal("أحمد 123", fake.RegisterCalls[^1].SearchTerm);
        Assert.Equal(100, fake.RegisterCalls[^1].PageSize);
        await page.AutoRegister.SearchAsync("");
        Assert.Equal("", fake.RegisterCalls[^1].SearchTerm);
    }

    [Fact]
    public async Task Closing_modal_invalidates_inflight_SQLCipher_response()
    {
        var fake = new Reader();
        var waiting = new TaskCompletionSource<NativeAutoRegisterPage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.Delayed = waiting.Task;
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        var loading = page.OpenAutoRegisterAsync();
        Assert.True(page.AutoRegister.IsOpen);
        page.AutoRegister.Close();
        waiting.SetResult(Reader.Snapshot(fake.RegisterCalls[0]));
        await loading;
        Assert.False(page.AutoRegister.IsOpen);
        Assert.Empty(page.AutoRegister.Rows);
        Assert.Empty(page.AutoRegister.CustomColumns);
    }

    [Fact]
    public async Task Logout_clears_modal_and_cancels_register_pending_tasks()
    {
        var fake = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.OpenAutoRegisterAsync();
        Assert.Single(page.AutoRegister.Rows);
        page.Clear();
        Assert.False(page.AutoRegister.IsOpen);
        Assert.Empty(page.AutoRegister.Rows);
        Assert.Equal("", page.CountdownLabel);
    }

    [Fact]
    public async Task Invalid_search_refuses_to_read_without_exposing_old_rows()
    {
        var fake = new Reader();
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.OpenAutoRegisterAsync();
        await page.AutoRegister.SearchAsync(new string('x', 101));
        Assert.Single(fake.RegisterCalls);
        Assert.Empty(page.AutoRegister.Rows);
        Assert.NotEmpty(page.AutoRegister.LoadError);
    }

    [Fact]
    public async Task Modified_principal_is_rejected_even_when_old_read_finishes()
    {
        var fake = new Reader();
        NativeIdentity? current = Founder;
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => current);
        await page.OpenAutoRegisterAsync();
        Assert.Single(page.AutoRegister.Rows);
        current = Founder with { SessionNonce = "REPLACED" };
        await page.AutoRegister.RefreshAsync();
        Assert.Empty(page.AutoRegister.Rows);
        Assert.False(page.AutoRegister.IsOpen);
    }

    [Fact]
    public async Task Malformed_policy_does_not_fake_a_valid_countdown()
    {
        var fake = new Reader { DueTime = "25:61" };
        var page = new DashboardPageViewModel();
        page.AttachReader(fake, () => Founder);
        await page.LoadAutoPolicyAsync();
        page.TickClock(new DateTime(2026, 11, 22, 7, 0, 0));
        Assert.Equal("", page.CountdownLabel);
        Assert.NotEmpty(page.AutoPolicyError);
    }

    private sealed class Reader : INativeDashboardReader, INativeDashboardContinuationReader
    {
        public readonly List<NativeAutoRegisterFilter> RegisterCalls = [];
        public int PolicyReads { get; private set; }
        public int SchedulerWrites => 0; // Contract has no such API.
        public string DueTime { get; set; } = "08:00";
        public Task<NativeAutoRegisterPage>? Delayed;

        public Task<NativeDashboardSnapshot> ReadAsync(NativeIdentity principal,
            NativeDashboardFilter filter, CancellationToken ct = default) =>
            Task.FromResult(new NativeDashboardSnapshot(
                filter.BusinessDate, filter.SiteId, 1, 1, 0, [], [], [], [], 0, filter.Page, false));

        public Task<NativeAutoAttendancePolicy> ReadPolicyAsync(
            NativeIdentity principal, CancellationToken ct = default)
        {
            PolicyReads++;
            return Task.FromResult(new NativeAutoAttendancePolicy(DueTime, true));
        }

        public async Task<NativeAutoRegisterPage> ReadAutoRegisterAsync(
            NativeIdentity principal, NativeAutoRegisterFilter filter, CancellationToken ct = default)
        {
            RegisterCalls.Add(filter);
            if (Delayed is { } delayed) return await delayed;
            return Snapshot(filter);
        }

        public static NativeAutoRegisterPage Snapshot(NativeAutoRegisterFilter filter) =>
            new(filter.BusinessDate, filter.SiteId, 125, filter.Page, filter.Page < 2,
                [new NativeDashboardColumn("FIELD-A", "حقل إضافي")],
                [new NativeAutoRegisterRow("P-1", "123", "أحمد", "المركز",
                    filter.BusinessDate.ToString("yyyy-MM-dd"), "تحضير تلقائي 08:00",
                    "2026-11-22T05:00:00Z", "موجود", ["A"])]);
    }
}
