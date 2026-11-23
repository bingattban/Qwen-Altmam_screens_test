#!/usr/bin/env python3
"""Test-first, fail-closed Phase 8 site retirement contract on BOTH Windows CI.

This static negative-path check supplements mandatory xUnit SQLCipher,
WPF/WebView2 E2E, packaged Windows x86/x64 and independent SHA-256 gates.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativeSiteRetirementContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeSiteRetirementViewModel.cs",
    "parent": "src/AlTamam.NativePresentation/NativeSiteCatalogViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "service": "src/AlTamam.Application/Bridge/AdminConfigurationService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeSiteRetirementPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/SiteRetirementIntegrationTests.cs",
    "administration": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "site_xaml": "src/AlTamam.NativeDesktop/Views/NativeSiteCatalogView.xaml",
    "retire_xaml": "src/AlTamam.NativeDesktop/Views/NativeSiteRetirementView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeSiteRetirementView.xaml.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in PATHS.items()}


def evaluate(files):
    errors = []

    def need(key, needle):
        if needle not in files[key]:
            errors.append(f"{key}: missing retirement invariant: {needle}")

    for file in ("site_xaml", "retire_xaml"):
        try:
            ET.fromstring(files[file])
        except ET.ParseError as exc:
            errors.append(f"{file}: malformed WPF XAML: {exc}")

    for needle in (
        "INativeSiteRetirementBackend", "PreviewRetirementAsync(",
        "CommitRetirementAsync(", "NativeSiteRetirementPreview(",
        "NativeSiteRetirementReceipt(", "DestinationSiteId"
    ):
        need("contract", needle)

    for needle in (
        "public async Task<BridgeResponse> PreviewSiteRetirementAsync(",
        "public async Task<BridgeResponse> RemoveSiteAndReassignCurrentPersonsAsync(",
        "ExecuteInWriteTransactionAsync(",
        "MutationIdempotency.StartAsync(",
        "MutationIdempotency.StoreAsync(",
        '["activeCount"] = activeCount',
        '["destinationSiteId"] = "SITE-UNSPECIFIED"',
        '["eligible"] = reason.Length == 0',
        '["blockReason"] = reason',
        "PeriodClosures", "AttendanceRecords", "PersonnelMovements",
        "PersonAssignments", "SITE_RETIRE_PERSON_REASSIGNED",
        "SITE_RETIRE", "ValidToLocalDate=$day",
        'WHERE a.SiteId=$id AND a.ValidToLocalDate IS NULL',
        'WHERE a.SiteId=$site AND a.ValidToLocalDate IS NULL',
        "UPDATE Sites SET ActiveFlag=0,RowVersion=RowVersion+1",
        "string.CompareOrdinal"
    ):
        need("service", needle)

    for needle in (
        "INativeSiteRetirementBackend",
        "new AdminConfigurationService(database)",
        ".PreviewSiteRetirementAsync(new JsonObject",
        ".RemoveSiteAndReassignCurrentPersonsAsync(new JsonObject",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSitesPrincipalAsync(actor, ct)",
        '["siteId"] = preview.SiteId',
        '["expectedRevision"] = preview.Revision',
        '["expectedActiveCount"] = preview.ActiveCount',
        '["confirmed"] = true',
        '["operationId"] = operationId',
        'RequiredNumber(data, "activeCount")',
        'RequiredBoolean(data, "eligible")',
        'RequiredNumber(data, "reassignedCount") != preview.ActiveCount',
        'RequiredNumber(data, "rowVersion") != preview.Revision + 1',
        'RequiredBoolean(data, "active")',
        '"SITE-UNSPECIFIED"'
    ):
        need("adapter", needle)

    for needle in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_onPage?.Invoke(site) == true",
        "_liveActor?.Invoke() == actor",
        "PreviewRetirementAsync(",
        "ValidatePreview(result)",
        'Guid.NewGuid().ToString("N")',
        "_impactConfirmed",
        "CommitRetirementAsync(actor, preview,",
        "_attempted = true",
        "result.SiteId != preview.SiteId",
        "result.ReassignedCount != preview.ActiveCount",
        "result.DestinationSiteId != \"SITE-UNSPECIFIED\"",
        "_uncertainActor = actor",
        "public void ForceCancel()"
    ):
        need("vm", needle)

    for needle in (
        "Retirement = new NativeSiteRetirementViewModel()",
        "Retirement.AttachBackend(",
        "Retirement.CommittedAndClosed",
        "Retirement.Open(row)",
        "Retirement.ForceCancel()",
        "!Retirement.IsOpen",
        "RetireCommand"
    ):
        need("parent", needle)
    need("administration", "SiteCatalog.AttachRetirementBackend(retirementBackend)")
    for needle in (
        'Command="{Binding RetireCommand}"',
        'DataContext="{Binding Retirement}"',
        "NativeSiteRetirementView"
    ):
        need("site_xaml", needle)

    for needle in (
        'Text="{Binding SourceSiteId}"',
        'Text="{Binding ImpactNote}"',
        'Text="{Binding Preview.BlockReason}"',
        'IsChecked="{Binding ImpactConfirmed, Mode=TwoWay',
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        "غير محدد", "SITE-UNSPECIFIED"
    ):
        need("retire_xaml", needle)
    need("view", "InitializeComponent()")

    for needle in (
        "Only_authorized_current_active_non_system_site_can_retire",
        "Preview_shows_exact_site_version_count_date_and_fixed_destination",
        "Ineligible_or_stale_preview_cannot_enable_commit",
        "Explicit_impact_checkbox_required_before_one_atomic_commit",
        "Forged_person_count_revision_or_destination_invalidates_preview",
        "Logout_during_preview_discards_old_snapshot_and_forbids_write",
        "Lost_write_receipt_or_bad_receipt_disables_same_session_retry",
        "Retirement_never_targets_an_operator_selected_destination"
    ):
        need("tests", needle)
    if files["tests"].count("[Fact]") < 8:
        errors.append("tests: eight C# first tests required")
    for needle in (
        "PreviewRetirement_ExposesExactCurrentActiveCountWithoutAnyWrites",
        "PreviewRetirement_ProtectsSystemSiteAndClosedDayWithoutChangingHistory",
        "RetiresSiteOnce_PreservesHistory_AndNeverMergesReusedNumbers",
        "SameDayPreparedOrNewlyEnrolledOrClosedDay_RejectsInsteadOfRewritingPast"
    ):
        need("integration", needle)

    for key in ("core", "release"):
        lines = files[key].splitlines()
        cmd = "python build/test-native-phase8-site-retirement-contract.py"
        indices = [i for i, line in enumerate(lines) if line.strip() == cmd]
        if len(indices) != 1:
            errors.append(f"{key}: retirement gate must execute once")
        elif indices[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indices[0]+1]
        ):
            errors.append(f"{key}: retirement gate must fail immediately")

    for key in ("contract", "vm", "parent", "site_xaml", "retire_xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "UPDATE Sites SET", "DELETE FROM Sites",
            "UPDATE Persons SET", "INSERT INTO PersonAssignments",
            "new AdminConfigurationService(", "ExecuteInWriteTransactionAsync("
        ):
            if forbidden in files[key]:
                errors.append(f"{key}: forbidden direct native SQL writer: {forbidden}")
    return errors


if __name__ == "__main__":
    problems = evaluate(TEXT)
    if problems:
        raise SystemExit("PHASE8 SITE RETIREMENT FAIL:\n" + "\n".join(problems))
    for key, token in (
        ("service", "PreviewSiteRetirementAsync("),
        ("service", '["destinationSiteId"] = "SITE-UNSPECIFIED"'),
        ("adapter", '["expectedActiveCount"] = preview.ActiveCount'),
        ("adapter", '["confirmed"] = true'),
        ("vm", "_impactConfirmed"),
        ("vm", "_attempted = true"),
        ("parent", "!Retirement.IsOpen"),
        ("administration", "SiteCatalog.AttachRetirementBackend(retirementBackend)"),
        ("retire_xaml", 'IsChecked="{Binding ImpactConfirmed, Mode=TwoWay'),
        ("tests", "Lost_write_receipt_or_bad_receipt_disables_same_session_retry"),
        ("core", "python build/test-native-phase8-site-retirement-contract.py"),
        ("release", "python build/test-native-phase8-site-retirement-contract.py")
    ):
        altered = dict(TEXT)
        altered[key] = altered[key].replace(token, "DELETED_NEGATIVE_REGRESSION")
        if not evaluate(altered):
            raise SystemExit(f"Negative regression absent: {key}: {token}")
    print("PHASE8 SITE RETIREMENT PASS: original SQLCipher transaction, exact site/count, stable history and both Windows gates")
