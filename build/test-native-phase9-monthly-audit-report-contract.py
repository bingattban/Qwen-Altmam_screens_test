#!/usr/bin/env python3
"""Native screen_025 slice 3: original monthly totals and immutable audit guard.

Extends tests-first C# behavior regression; both Windows workflows run this
before packaging. It does not assert customer readiness or route parity.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "original": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "audit_contracts": "src/AlTamam.NativePresentation/NativeAuditReadContracts.cs",
    "audit_detail": "src/AlTamam.NativePresentation/NativeAuditDetailContracts.cs",
    "account_contract": "src/AlTamam.NativePresentation/NativeAccountReadContracts.cs",
    "account_vm": "src/AlTamam.NativePresentation/NativeAccountsPageViewModel.cs",
    "account_tests": "tests/AlTamam.UnitTests/NativeAccountListPhase11Tests.cs",
    "audit_history_contract": "src/AlTamam.NativePresentation/NativeAuditHistoryContracts.cs",
    "audit_history_vm": "src/AlTamam.NativePresentation/NativeAuditHistoryViewModel.cs",
    "audit_history_tests": "tests/AlTamam.UnitTests/NativeAuditHistoryPhase11Tests.cs",
    "audit_edit_contract": "src/AlTamam.NativePresentation/NativeAuditEditContracts.cs",
    "audit_edit_review": "src/AlTamam.NativePresentation/NativeAuditEditReview.cs",
    "audit_edit_tests": "tests/AlTamam.UnitTests/NativeAuditEditPhase11Tests.cs",
    "audit_detail_tests": "tests/AlTamam.UnitTests/NativeAuditDetailsPhase11Tests.cs",
    "audit_tests": "tests/AlTamam.UnitTests/NativeAuditReadPhase11Tests.cs",
    "audit_list": "src/AlTamam.NativePresentation/NativeAuditPageViewModel.cs",
    "audit_list_tests": "tests/AlTamam.UnitTests/NativeAuditListPhase11Tests.cs",
    "audit_shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "audit_host": "src/AlTamam.NativeDesktop/MainWindow.xaml",
    "audit_template": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "audit_nav_tests": "tests/AlTamam.UnitTests/NativeWpfNavigationTests.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "viewmodel": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeMonthlyHistoryAndAuditPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
SOURCE = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}
COMMAND = "python build/test-native-phase9-monthly-audit-report-contract.py"
REQUIRED = {
    "original": ("FROM AttendanceRecords WHERE PersonId=$id",
                 "GROUP BY SUBSTR(AttendanceLocalDate,1,7),StatusId,StatusNameSnapshot",
                 "FROM AuditEvents", "TargetEntityId=$id",
                 '["monthlyTotals"]=monthly', '["audit"]=events',
                 '["auditPage"]=auditPage', '["auditTotal"]=audits',
                 '["auditHasNext"]=' ),
    "contracts": ("NativePersonnelReportMonthSummary(",
                  "NativePersonnelReportAuditRecord(",
                  "int AuditPage = 1", "MonthlyTotals { get; init; }",
                  "AuditTotal { get; init; }",
                  "AuditHasNext { get; init; }"),
    "viewmodel": ("NextAuditAsync(", "PreviousAuditAsync(",
                  "AuditPageLabel", "report.AuditPage != requested.AuditPage",
                  "report.AuditHasNext !=",
                  "ValidPageCount(report.Audit.Count, report.AuditTotal, requested.AuditPage)",
                  "report.MonthlyTotals.Sum(month => month.Count) != report.AttendanceTotal",
                  "month.Period + \"-01\"", "month.Count <= 0",
                  "item.UsernameSnapshot", "version != _generation",
                  "_pending?.Cancel();"),
    "account_contract": ("CanAccessUsersManagement(role)", "CanCreateAccountWithRole(actorRole,role)",
                         "actor.Role!=UserRole.OrdinaryAdministration || Role==UserRole.Supervisor",
                         "actor.UserId!=UserId", "!ids.Add(row.UserId)", "row.Index!=offset+i+1"),
    "account_vm": ("generation!=_generation", "AuthorizedActor!=actor", "_pending?.Cancel()",
                   "result.Matches(request,actor)", "Array.AsReadOnly(result.Rows.ToArray())"),
    "account_tests": ("Account_page_rejects_scope_leaks_forged_permissions_and_duplicate_identity",
                      "Account_creation_roles_exclude_founder_and_follow_canonical_policy",
                      "Account_search_and_paging_require_explicit_requests",
                      "Account_page_denies_supervisor_and_fails_closed_on_forged_response",
                      "Account_list_cannot_repaint_after_clear_or_session_change"),
    "audit_history_contract": ("CanReviewAuditOverlayHistory(role)", "After.Version==Before.Version+1",
                               "After.Description==Before.Description", "!keys.Add((row.AuditEventId,row.After.Version))",
                               "row.AuditEventId!=expected.AuditEventId"),
    "audit_history_vm": ("generation!=_generation", "AuthorizedActor!=actor", "_pending?.Cancel()",
                         "result.Matches(request)", "Array.AsReadOnly(result.Rows.ToArray())"),
    "audit_history_tests": ("History_preserves_versions_and_rejects_duplicate_or_forged_states",
                            "History_reads_selected_scope_and_pages_then_clears",
                            "History_denies_supervisor_and_discards_wrong_scope",
                            "History_cannot_repaint_after_close_or_session_change"),
    "audit_edit_contract": ("Hide ? Description==Row.Description", "request.Hide,request.Description", "CanEditOrDeleteVisibleAuditLog(role)", "detail.ChainValid",
                            "OperationAuditEventId!=AuditEventId", "RawEvidenceUntouched",
                            "Before==new NativeAuditEditState", "After==new NativeAuditEditState"),
    "audit_edit_review": ("IsHide || _description==value", "_backend.HideAuditEntryAsync(_actor!,request,ct)",
                          "ReferenceEquals(_currentRow?.Invoke(),row)", "ReferenceEquals(_currentDetail?.Invoke(),detail)",
                          "_uncertain=true", "result.Matches(request)", "NativeAuditEditStaleException",
                          "Prepared=request", "if(!CanClose) return false"),
    "audit_edit_tests": ("Hide_intent_requires_reason_visible_verified_row_and_unchanged_description",
                         "Hide_review_uses_distinct_canonical_action_and_exact_retry",
                         "Page_hide_cannot_overlap_edit_and_refreshes_only_after_verified_receipt",
                         "Edit_request_and_receipt_enforce_identity_role_revision_and_raw_evidence",
                         "Edit_review_requires_explicit_preview_and_retries_identical_operation",
                         "Edit_review_rejects_forged_receipt_and_definitive_stale_version",
                         "Edit_review_scrubs_pending_result_on_session_teardown",
                         "Page_edit_freezes_filters_and_reloads_after_verified_receipt"),
    "audit_detail": ("INativeAuditDetailReader", "EventHash==row.EventHash",
                     "OverlayVersion==row.OverlayVersion", "IsTombstoned==row.IsTombstoned",
                     "CanReviewAuditOverlayHistory(role)", 'PriorEventHash=="GENESIS"', "ChainValid"),
    "audit_detail_tests": ("Detail_matches_only_exact_immutable_evidence_and_overlay_revision",
                           "Detail_requires_reference_bound_row_and_preserves_list",
                           "Detail_discards_late_response_on_filter_edit_or_session_change",
                           "Detail_clear_during_read_scrubs_all_evidence"),
    "audit_list": ("History.Close()", "History.Attach(historyReader,liveActor)", "History.OpenAsync(row.AuditEventId,ct)",
                   "public bool CanOpenHide=>CanOpenEdit", "()=>Detail,hide:true", "Edit.ForceClear()", "if(Edit.IsOpen) return;", "Edit.Receipt is not null || Edit.NeedsReload",
                   "ReferenceEquals(current,row)", "generation!=_detailGeneration",
                   "result.Matches(row,actor.Role)", "_detailPending?.Cancel()", "ResetDetail();",
                   "_pending?.Cancel()", "generation!=_generation", "AuthorizedActor!=actor",
                   "result.Matches(filter)", "Array.AsReadOnly(result.Rows.ToArray())",
                   "CanReviewAuditOverlayHistory(actor.Role)", "_draft=next with { Page=1 }"),
    "audit_list_tests": ("Audit_list_reads_exact_filters_and_pages_only_on_explicit_request",
                         "Audit_list_denies_hidden_scope_to_supervisor_and_rejects_bad_echo",
                         "Audit_list_discards_pending_results_after_clear_or_identity_change",
                         "Audit_list_cannot_repaint_old_results_after_filter_edit"),
    "audit_shell": ("Accounts.AttachReader(accountReader, () => _active)", "Accounts.Clear();",
                    "screen != NativeScreen.Accounts || Accounts.CanRead", "!Audit.Edit.IsOpen", "Audit.AttachReader(auditReader, () => _active)", "Audit.Clear();",
                    "screen == NativeScreen.AuditLog", "screen != NativeScreen.AuditLog || Audit.CanRead"),
    "audit_host": ("present:NativeScreen.AuditLog", "present:NativeScreen.Accounts", "Binding CanManageAccounts"),
    "audit_template": ("vm:NativeAccountsPageViewModel", "Binding CreationRoleLabel", "Binding ModifyLabel",
                       "Binding OpenHistoryCommand", "Binding OpenEntryHistoryCommand", "Binding History.Rows",
                       "Binding Before.Description", "Binding After.Description", "Binding History.CloseCommand",
                       "Binding OpenHideCommand", "Binding Edit.ShowDescriptionEditor", "Binding Edit.AfterLabel", "Binding Edit.PreviewCommand", "Binding Edit.ConfirmCommand", "Binding CloseEditCommand",
                       "Binding DataContext.OpenDetailCommand", "Binding Detail.ChainStatus",
                       "Binding Detail.RawDescription", "Binding Detail.EventHash",
                       "Binding Detail.OldValueDisplay", "Binding Detail.NewValueDisplay",
                       "Binding CloseDetailCommand", "vm:NativeAuditPageViewModel", "Binding IncludeTombstoned",
                       "Binding CanIncludeTombstoned", "Binding SearchCommand",
                       "Binding NextCommand", 'IsReadOnly="True"'),
    "audit_nav_tests": ("Audit_navigation_requires_live_reader_and_clears_on_leave_and_logout",
                        "Accounts_navigation_respects_roles_and_scrubs_on_leave_and_logout"),
    "audit_contracts": ("INativeAuditReader", "CanReviewAuditOverlayHistory(role)",
                        "Rows.Count!=Math.Clamp", "row.Sequence!=offset+i+1",
                        "!expected.IncludeTombstoned && row.IsTombstoned", "!ids.Add(row.AuditEventId)"),
    "audit_tests": ("Hidden_audit_entries_require_administration",
                    "Audit_filter_preserves_bounds_and_rejects_invalid_dates",
                    "Audit_snapshot_rejects_duplicate_rows_wrong_sequence_and_hidden_leaks",
                    "Audit_snapshot_checks_actual_page_size_and_canonical_offset"),
    "adapter": ("new PersonnelReportService(database)",
                "new AuditOverlayService(database).QueryAuditLogAsync",
                "new UserAccountManagementService(database).ListAsync",
                '["pageSize"]=NativeAccountRequest.PageSize', "!page.Matches(request,actor)",
                "new AuditOverlayService(database).QueryAuditPresentationChangesAsync",
                '["pageSize"]=NativeAuditHistoryRequest.PageSize',
                "!request.IsAuthorized(principal.Role)", "!page.Matches(request)",
                "new AuditOverlayService(database).GetAuditEntryDetailsAsync",
                "new AuditOverlayService(database).UpdateAuditPresentationAsync",
                "new AuditOverlayService(database).DeleteAuditPresentationAsync",
                "request.Hide!=hide", 'if (!hide) payload["correctedDescription"]=request.Description;',
                '["expectedOverlayVersion"]=request.Row.OverlayVersion',
                '["operationId"]=request.OperationId',
                "response.AuditEventId!=receipt.OperationAuditEventId", "!receipt.Matches(request)",
                'conflict["reason"]?.GetValue<string>()=="OverlayVersionStale"',
                'RequiredNumber(overlay,"overlayVersion")!=++version',
                "hidden && !RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role)",
                'RequiredBoolean(data,"chainValid")', "!detail.Matches(row,actor.Role)",
                "filter.IsAuthorized(principal.Role)", '["includeTombstoned"]=filter.IncludeTombstoned',
                'RequiredNumber(data,"pageSize")!=NativeAuditFilter.PageSize',
                'RequiredNumber(data,"count")!=items.Count',
                'RequiredText(row,"rawDescription")', "!snapshot.Matches(filter)",
                '["auditPage"] = pages.AuditPage',
                'RequiredNumber(data, "auditPage") != pages.AuditPage',
                'RequiredArray(data, "monthlyTotals")',
                'RequiredArray(data, "audit")',
                'RequiredNumber(data, "auditTotal")',
                'RequiredBoolean(data, "auditHasNext")',
                "await VerifyPersonnelManagerAsync(actor, ct)"),
    "view": ('FlowDirection="RightToLeft"', "Binding Report.MonthlyTotals",
             "Binding Report.Audit", "Binding NextAuditCommand",
             "Binding PreviousAuditCommand", 'IsReadOnly="True"',
             "Binding UsernameSnapshot", "Binding FullNameSnapshot"),
    "tests": ("Monthly_totals_preserve_source_periods_and_status_names",
              "Audit_has_own_100_row_pagination_without_mutating_other_pages",
              "Historical_executor_never_substitutes_current_actor_or_person",
              "Forged_monthly_or_audit_evidence_is_never_displayed",
              "Stale_row_or_unauthorized_actor_cannot_open_audit",
              "Logout_discards_late_audit_page_results"),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = ("new SqliteConnection(", "INSERT INTO", "DELETE FROM",
             "File.WriteAllBytes(", "SaveFileDialog(", "UPDATE Persons SET")


def validate(files):
    errors = []
    for key, tokens in REQUIRED.items():
        for token in tokens:
            if token not in files[key]:
                errors.append(f"{key}: missing original report invariant: {token}")
    if "new AuditOverlayService(database,services.Time)" in files["adapter"]:
        errors.append("adapter: internal BridgeSharedServices.Time is not accessible from NativeDesktop")
    for key in ("contracts", "viewmodel", "view", "audit_contracts", "audit_list", "audit_template", "audit_detail", "audit_edit_contract", "audit_edit_review", "audit_history_contract", "audit_history_vm", "account_contract", "account_vm"):
        for token in FORBIDDEN:
            if token in files[key]:
                errors.append(f"{key}: native report may not write: {token}")
    try:
        ElementTree.fromstring(files["view"])
        ElementTree.fromstring(files["audit_template"])
    except ElementTree.ParseError as exc:
        errors.append(f"view: malformed XML: {exc}")
    if files["tests"].count("[Fact]") < 5 or "[Theory]" not in files["tests"]:
        errors.append("tests: test-first monthly/audit cases absent")
    for key in ("core", "release"):
        lines = files[key].splitlines()
        indexes = [i for i, line in enumerate(lines) if line.strip() == COMMAND]
        if len(indexes) != 1 or indexes[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indexes[0] + 1]
        ):
            errors.append(f"{key}: guard must run exactly once with nonzero-exit failure")
    return errors


if __name__ == "__main__":
    errors = validate(SOURCE)
    if errors:
        raise SystemExit("NATIVE MONTHLY/AUDIT REPORT FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("original", "GROUP BY SUBSTR(AttendanceLocalDate,1,7),StatusId,StatusNameSnapshot"),
        ("original", "FROM AuditEvents"),
        ("audit_contracts", "CanReviewAuditOverlayHistory(role)"),
        ("audit_list", "generation!=_generation"),
        ("audit_list", "generation!=_detailGeneration"),
        ("audit_detail", "OverlayVersion==row.OverlayVersion"),
        ("account_contract", "actor.Role!=UserRole.OrdinaryAdministration || Role==UserRole.Supervisor"),
        ("account_vm", "AuthorizedActor!=actor"),
        ("account_tests", "Account_list_cannot_repaint_after_clear_or_session_change"),
        ("adapter", "new UserAccountManagementService(database).ListAsync"),
        ("audit_shell", "Accounts.AttachReader(accountReader, () => _active)"),
        ("audit_template", "vm:NativeAccountsPageViewModel"),
        ("audit_history_contract", "!keys.Add((row.AuditEventId,row.After.Version))"),
        ("audit_history_vm", "AuthorizedActor!=actor"),
        ("audit_history_tests", "History_cannot_repaint_after_close_or_session_change"),
        ("adapter", "new AuditOverlayService(database).QueryAuditPresentationChangesAsync"),
        ("audit_template", "Binding History.Rows"),
        ("audit_edit_contract", "OperationAuditEventId!=AuditEventId"),
        ("audit_edit_review", "_uncertain=true"),
        ("audit_edit_review", "_backend.HideAuditEntryAsync(_actor!,request,ct)"),
        ("audit_edit_contract", "Hide ? Description==Row.Description"),
        ("adapter", "new AuditOverlayService(database).DeleteAuditPresentationAsync"),
        ("audit_template", "Binding OpenHideCommand"),
        ("audit_edit_tests", "Hide_review_uses_distinct_canonical_action_and_exact_retry"),
        ("audit_shell", "!Audit.Edit.IsOpen"),
        ("audit_edit_tests", "Edit_review_requires_explicit_preview_and_retries_identical_operation"),
        ("adapter", "new AuditOverlayService(database).UpdateAuditPresentationAsync"),
        ("audit_template", "Binding Edit.ConfirmCommand"),
        ("adapter", 'RequiredBoolean(data,"chainValid")'),
        ("audit_template", "Binding Detail.ChainStatus"),
        ("audit_detail_tests", "Detail_clear_during_read_scrubs_all_evidence"),
        ("audit_list", "AuthorizedActor!=actor"),
        ("audit_shell", "Audit.AttachReader(auditReader, () => _active)"),
        ("audit_template", "vm:NativeAuditPageViewModel"),
        ("audit_list_tests", "Audit_list_cannot_repaint_old_results_after_filter_edit"),
        ("audit_contracts", "!ids.Add(row.AuditEventId)"),
        ("audit_tests", "Hidden_audit_entries_require_administration"),
        ("adapter", "new AuditOverlayService(database).QueryAuditLogAsync"),
        ("contracts", "NativePersonnelReportAuditRecord("),
        ("viewmodel", "report.AuditPage != requested.AuditPage"),
        ("viewmodel", "report.MonthlyTotals.Sum(month => month.Count) != report.AttendanceTotal"),
        ("adapter", '["auditPage"] = pages.AuditPage'),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("view", "Binding Report.MonthlyTotals"),
        ("view", "Binding Report.Audit"),
        ("tests", "Logout_discards_late_audit_page_results"),
        ("core", COMMAND),
        ("release", COMMAND),
    ):
        replaced = dict(SOURCE)
        replaced[key] = replaced[key].replace(needle, "FORGED_BY_NEGATIVE_PATH_TEST")
        if not validate(replaced):
            raise SystemExit(f"Missing negative-path enforcement: {key} {needle}")
    print("PASS: original immutable monthly aggregates and paged actor audit evidence")
