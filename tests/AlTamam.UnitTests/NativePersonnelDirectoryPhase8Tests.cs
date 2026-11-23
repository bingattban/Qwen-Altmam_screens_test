using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Test-first native WPF personnel administration: verified SQLCipher
/// source, manager RBAC, immutable PersonId, current custom fields and
/// identity history; no mutable form and no duplicate identity writer.
/// </summary>
public sealed class NativePersonnelDirectoryPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-1", "admin", UserRole.GeneralAdministration, "ROSTER-NONCE");

    [Fact]
    public async Task Missing_session_and_supervisor_cannot_read_admin_personnel()
    {
        var reader = new Reader();
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => null);
        await vm.RefreshAsync();
        Assert.Empty(reader.Requests);
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);

        NativeIdentity? live = Manager with { Role = UserRole.Supervisor };
        var other = new AdministrationPageViewModel();
        other.AttachReader(reader, () => live);
        await other.RefreshAsync();
        Assert.Empty(reader.Requests);
        Assert.False(other.IsDataConnected);
    }

    [Fact]
    public async Task Authenticated_personnel_show_original_number_rank_site_and_stable_identity()
    {
        var reader = new Reader();
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => Manager);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        Assert.Single(reader.Requests);
        Assert.Equal(100, reader.Requests[0].PageSize);
        Assert.Equal(1, reader.Requests[0].OrganizationalStatus);
        var person = Assert.Single(vm.Rows);
        Assert.Equal("P-1", person.PersonId);
        Assert.Equal("123", person.ServiceNumberA);
        Assert.Equal("رقيب", person.Rank);
        Assert.Equal("أحمد", person.Name);
        Assert.Equal("المركز", person.SiteName);
        Assert.Equal("على القوة", person.StatusName);
        Assert.Equal("ممتاز", Assert.Single(person.CustomCells));
        Assert.Equal("ملاحظة", Assert.Single(vm.CustomColumns).Caption);
    }

    [Fact]
    public async Task Site_status_search_and_pagination_use_exact_canonical_filters()
    {
        var reader = new Reader { Total = 150 };
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => Manager);
        await vm.RefreshAsync();
        await vm.NextPageAsync();
        Assert.Equal(2, reader.Requests[^1].Page);
        await vm.SelectSiteAsync("SITE-A");
        Assert.Equal("SITE-A", reader.Requests[^1].SiteId);
        Assert.Equal(1, reader.Requests[^1].Page);
        await vm.SelectStatusAsync(3);
        Assert.Equal(3, reader.Requests[^1].OrganizationalStatus);
        await vm.SearchAsync("أحمد");
        Assert.Equal("أحمد", reader.Requests[^1].SearchTerm);
        Assert.Equal(1, reader.Requests[^1].Page);
        await vm.SelectStatusAsync(0);
        Assert.Equal(0, reader.Requests[^1].OrganizationalStatus);
    }

    [Fact]
    public async Task Forged_old_row_and_nonce_swap_never_read_another_person_profile()
    {
        var reader = new Reader();
        NativeIdentity? live = Manager;
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Rows);
        await vm.OpenProfileAsync(row with { PersonId = "P-FORGED" });
        Assert.Equal(0, reader.Profiles);
        await vm.SelectSiteAsync("SITE-A");
        await vm.OpenProfileAsync(row);
        Assert.Equal(0, reader.Profiles);
        var current = Assert.Single(vm.Rows);
        live = Manager with { SessionNonce = "OTHER-NONCE" };
        await vm.OpenProfileAsync(current);
        Assert.Equal(0, reader.Profiles);
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Canonical_profile_history_is_read_only_and_closes_on_refresh()
    {
        var reader = new Reader();
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => Manager);
        await vm.RefreshAsync();
        await vm.OpenProfileAsync(Assert.Single(vm.Rows));
        Assert.True(vm.IsProfileOpen);
        Assert.Equal(1, reader.Profiles);
        Assert.NotNull(vm.SelectedProfile);
        Assert.Equal("P-1", vm.SelectedProfile.PersonId);
        Assert.Single(vm.SelectedProfile.IdentityHistory);
        Assert.Equal("أحمد القديم", vm.SelectedProfile.IdentityHistory[0].Name);
        await vm.RefreshAsync();
        Assert.False(vm.IsProfileOpen);
        Assert.Null(vm.SelectedProfile);
    }

    [Fact]
    public async Task Logout_discards_inflight_read_and_malformed_page_fails_closed()
    {
        var reader = new Reader();
        NativeIdentity? live = Manager;
        var vm = new AdministrationPageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        var pending = new TaskCompletionSource<NativePersonnelSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = pending.Task;
        var reading = vm.RefreshAsync();
        vm.Clear();
        live = null;
        pending.SetResult(reader.Snapshot(reader.Requests[^1]));
        await reading;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);

        reader.Pending = null;
        reader.ForgedEcho = true;
        live = Manager;
        await vm.RefreshAsync();
        Assert.False(vm.IsDataConnected);
        Assert.NotEmpty(vm.LoadError);
        Assert.Empty(vm.Rows);
    }

    private sealed class Reader : INativePersonnelDirectoryReader
    {
        public int Profiles;
        public long Total = 1;
        public bool ForgedEcho;
        public Task<NativePersonnelSnapshot>? Pending;
        public readonly List<NativePersonnelFilter> Requests = [];

        public Task<NativePersonnelCatalogue> ReadCatalogueAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult(new NativePersonnelCatalogue(
                [new NativeDashboardSite("SITE-A", "المركز")]));

        public Task<NativePersonnelSnapshot> ReadPageAsync(
            NativeIdentity actor, NativePersonnelFilter filter, CancellationToken ct = default)
        {
            Requests.Add(filter);
            return Pending ?? Task.FromResult(Snapshot(filter));
        }

        public NativePersonnelSnapshot Snapshot(NativePersonnelFilter filter) =>
            new(ForgedEcho ? "SITE-OTHER" : filter.SiteId, filter.SearchTerm,
                filter.OrganizationalStatus, filter.Page, Total,
                (long)filter.Page * 100 < Total,
                [new NativeDashboardColumn("rank", "الرتبة"),
                    new NativeDashboardColumn("note", "ملاحظة")],
                [new NativePersonnelRow("P-1", "123", "أحمد",
                    "المركز", 1, true, 1, ["رقيب", "ممتاز"])
                    { RowNumber = (filter.Page - 1) * 100 + 1 }]);

        public Task<NativePersonnelProfile> ReadProfileAsync(
            NativeIdentity actor, string personId, CancellationToken ct = default)
        {
            Profiles++;
            return Task.FromResult(new NativePersonnelProfile(
                personId, "أحمد", "123", 1, true, 1,
                "2026-01-01",
                [new NativePersonnelIdentityHistory(
                    "أحمد القديم", "123", "2026-01-01", "",
                    "تصحيح الاسم", "2026-01-02T00:00:00Z")]));
        }
    }
}
