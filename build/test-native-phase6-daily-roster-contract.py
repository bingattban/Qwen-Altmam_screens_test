#!/usr/bin/env python3
"""Phase 6 first slice: live daily roster and fail-closed native WPF contract.

This static negative-path check runs in BOTH Core and Release alongside real
xUnit, SQLCipher Windows integration, WebView2 E2E and four package gates.
It does not certify native WPF production or the unmigrated daily write route.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "vm": "src/AlTamam.NativePresentation/DailyAttendancePageViewModel.cs",
    "drafts": "src/AlTamam.NativePresentation/NativeAttendanceDraftsViewModel.cs",
    "draft_intent": "src/AlTamam.NativePresentation/NativeAttendanceDraftIntent.cs",
    "draft_save_tests": "tests/AlTamam.UnitTests/NativeAttendanceDraftSavePhase12Tests.cs",
    "draft_restore_tests": "tests/AlTamam.UnitTests/NativeAttendanceDraftRestorePhase12Tests.cs",
    "review_xaml": "src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml",
    "draft_tests": "tests/AlTamam.UnitTests/NativeAttendanceDraftPhase12Tests.cs",
    "navigation_tests": "tests/AlTamam.UnitTests/NativeWpfNavigationTests.cs",
    "tests": "tests/AlTamam.UnitTests/NativeDailyAttendancePhase6Tests.cs",
    "review_tests": "tests/AlTamam.UnitTests/NativeDailyReviewedStatusPhase6Tests.cs",
    "bulk_tests": "tests/AlTamam.UnitTests/NativeDailyBulkReviewPhase6Tests.cs",
    "review": "src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "pages": "src/AlTamam.NativePresentation/NativePageViewModels.cs",
    "templates": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeDailyAttendanceView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeDailyAttendanceView.xaml.cs",
    "reader": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/DailyMonthlyReadService.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {key: (ROOT / name).read_text(encoding="utf-8")
        for key, name in FILES.items()}


def check(content):
    issues = []

    def needs(key, text):
        if text not in content[key]:
            issues.append(f"Missing {key} safety invariant: {text}")

    for key in ("templates", "xaml"):
        try:
            ET.fromstring(content[key])
        except ET.ParseError as error:
            issues.append(f"Invalid native XAML {key}: {error}")
    for token in ("INativeDashboardReader", "DateOnly.FromDateTime(DateTime.Now)",
                  "new NativeDashboardFilter(", "100);",
                  "ClearSensitive(", "_activeRead?.Cancel()",
                  "request != _version", "_currentPrincipal?.Invoke() != principal",
                  "_boundPrincipal != principal", "Validate(snapshot, filter)",
                  "snapshot.BusinessDate != filter.BusinessDate",
                  "snapshot.SiteId != filter.SiteId",
                  "snapshot.HasNext !=", "SelectSiteAsync(", "SelectStatusAsync(",
                  "SearchAsync(", "NextPageAsync(", "PreviousPageAsync(",
                  "snapshot.CustomColumns[i].Caption.Trim() is \"الرتبة\" or \"رتبة\"",
                  "CustomCells.Where((_, i) => i != rankIndex)"):
        needs("vm", token)
    for token in ("CanReviewPerson(", "ReferenceEquals(item, row)",
                  "Review.OpenPersonsAsync(", "Review.ForceCancel();",
                  "person.PersonId, person.RowNumber", "_loadedDate = snapshot.BusinessDate"):
        needs("vm", token)
    for token in ("OpenPersonsAsync(", "CommitAsync(",
                  "ReadChoicesAsync(", "PreviewAsync("):
        needs("review", token)
    for token in ("Live_daily_row_reuses_server_review_and_writes_only_after_confirmation",
                  "Forged_or_expired_daily_selection_cannot_open_review",
                  "Selection_from_previous_page_or_filter_is_not_authorized"):
        needs("review_tests", token)
    if content["review_tests"].count("[Fact]") < 3:
        issues.append("Missing daily-review positive and negative path xUnit coverage.")
    for token in ("SetSelectedRows(", "CanReviewSelected()", "OpenSelectedReviewAsync(",
                  "_selected.All(row => CanReviewPerson(row))",
                  "selection.Any(row => row is null", "ResetSelection(\"\");",
                  "ReviewSelectedCommand", "SelectedCount =>"):
        needs("vm", token)
    for token in ("CanChangeRosterScope => CanRead && !Review.IsOpen",
                  "if (!CanChangeRosterScope) return;",
                  "Review.IsCompleted && !Review.HasUnverifiedCommit",
                  "RefreshAfterVerifiedCommitAsync()"):
        needs("vm", token)
    for token in ("Daily_review_freezes_roster_scope_until_cancelled",
                  "Verified_commit_refreshes_once_and_clears_selection_but_keeps_receipt",
                  "Unverified_commit_freezes_scope_and_preserves_original_intent",
                  "Two_selected_persons_are_previewed_together_and_never_committed_implicitly",
                  "Duplicate_forged_and_expired_group_members_fail_closed",
                  "Selection_is_frozen_during_approved_preview_and_cleared_on_session_logout"):
        needs("bulk_tests", token)
    for token in ("OpenInitialPersonsAsync(", "row.IsUnprepared", "!row.IsTransferDay",
                  "selection.Select(row => row.IsUnprepared).Distinct().Count() > 1"):
        needs("vm", token)
    for token in ("Unprepared_people_use_initial_preview_and_submit_only_after_confirmation",
                  "Mixed_initial_and_revision_selection_requires_separate_operations",
                  "Initial_preview_with_wrong_mode_or_previous_status_is_not_approved"):
        needs("bulk_tests", token)
    needs("reader", 'IsUnprepared = !transferDay && item["statusId"] is null')
    if content["bulk_tests"].count("[Fact]") < 9:
        issues.append("Missing bulk-selection xUnit positive/negative coverage.")
    for token in ("DailyAttendance = new DailyAttendancePageViewModel()",
                  "DailyAttendance.AttachReader(reader, () => _active)",
                  "_ = DailyAttendance.RefreshAsync()",
                  "DailyAttendance.Clear();"):
        needs("shell", token)
    if "public sealed class DailyAttendancePageViewModel()" in content["pages"]:
        issues.append("The disconnected daily route placeholder remains.")
    needs("templates", "<views:NativeDailyAttendanceView")
    for token in ('Header="الرقم"', 'Header="الرقم (ع)"',
                  'Header="الرتبة"', 'Header="الاسم"',
                  'Header="الموقع"', 'Header="الحالة"',
                  'IsReadOnly="True"', "SearchCommand",
                  "SelectedSiteId", "SelectedStatusId", "DailyGrid",
                  "NativeAttendanceReviewView", "ReviewPersonCommand",
                  "ReviewSelectedCommand", 'SelectionMode="Extended"',
                  'SelectionChanged="OnDailySelectionChanged"',
                  "SelectedLabel", "SelectionError",
                  'IsEnabled="{Binding CanChangeRosterScope}"',
                  'CommandParameter="{Binding SelectedItem, ElementName=DailyGrid}"'):
        needs("xaml", token)
    for token in ("IsDataConnected", "CustomColumns",
                  "DailyGrid.Columns.Add(", "OnSiteChanged(",
                  "OnStatusChanged(", "IsKeyboardFocusWithin",
                  "OnDailySelectionChanged(", "DailyGrid.SelectedItems",
                  "DailyGrid.SelectAll();", "DailyGrid.UnselectAll();"):
        needs("view", token)
    for token in ("ResolveSessionAsync(identity.SessionNonce",
                  "DailyRosterAsync(rosterQuery, ct)",
                  "new DailyMonthlyReadService(database)",
                  "personId", "customColumns"):
        needs("reader", token)
    needs("canonical", "RosterCustomFieldProjection.AttachAsync")
    if content["tests"].count("[Fact]") < 6:
        issues.append("Missing Phase 6 xUnit regression coverage.")
    for token in ("No_live_session_means_no_query_and_no_personnel",
                  "Native_daily_screen_uses_existing_typed_roster_with_actual_rank",
                  "Logout_or_replaced_nonce_revokes_all_daily_personnel"):
        needs("tests", token)
    for token in ("NativeAttendanceDraft.ValidCatalogue(rows)", "Guid.TryParseExact(DraftId[4..]",
                  "PersonIds.Distinct(StringComparer.Ordinal).Count()==PersonIds.Count",
                  "_liveActor?.Invoke()==actor", "version!=_generation || linked.IsCancellationRequested",
                  "if(!CanRead){Clear();return;}", "Array.AsReadOnly(row.PersonIds.ToArray())"):
        needs("drafts", token)
    for token in ("services.ListDraftsAsync(principal.UserId,lifetime.Token)",
                  "Draft session changed.", "NativeAttendanceDraft.ValidCatalogue(rows)"):
        needs("reader", token)
    source=content["reader"]
    start=source.find("public async Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(")
    end=source.find("public async Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(",start)
    if not (0<=start<end):
        issues.append("Missing native owner draft read entry")
    else:
        method=source[start:end]
        order=[method.find(t) for t in (
            "using var nativeOperation = operations.Enter();",
            "services.ResolveSessionAsync(actor.SessionNonce,ct)",
            "principal.UserId!=actor.UserId",
            "CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)",
            "services.ListDraftsAsync(principal.UserId,lifetime.Token)",
            "VerifyContinuationIdentityAsync(actor,lifetime.Token)",
            "NativeAttendanceDraft.ValidCatalogue(rows)")]
        if min(order)<0 or order!=sorted(order) or any(t in method for t in ("SaveDraftAsync(","DeleteDraftAsync(","CommitAsync(","ExecuteInWriteTransactionAsync(")):
            issues.append("Draft read must use exact live owner, linked cancellation and no write")
    for token in ("Drafts.Attach(draftReader, principal)", "Drafts.Clear();", "!Drafts.IsOpen",
                  "if (Drafts.IsOpen || Review.IsOpen", "OpenDraftsCommand"):
        needs("vm",token)
    needs("shell","(screen == NativeScreen.DailyAttendance || !DailyAttendance.Drafts.IsOpen) &&")
    for token in ("Binding OpenDraftsCommand", "Binding Drafts", "Binding PersonCount"):
        needs("xaml",token)
    for token in ("Catalogue_validates_identity_bounds_and_unique_selection",
                  "All_standard_roles_read_only_their_own_draft_catalogue",
                  "Closed_or_replaced_session_cannot_receive_late_draft_rows",
                  "Malformed_catalogue_is_hidden_and_refresh_recovers"):
        needs("draft_tests",token)
    needs("navigation_tests","Draft_catalogue_freezes_daily_scope_and_navigation_and_logout_scrubs_rows")
    for token in ("BusinessDate==today", "!NativeAttendanceDateRange.RequiresRange(TargetStatusId)",
                  "receipt.DraftId==DraftId", "receipt.PersonIds.Order"):
        needs("draft_intent",token)
    for token in ("_draftAttempted=true", "if(_savingDraft) return;",
                  "writer.SaveAttendanceDraftAsync(actor,intent,linked.Token)",
                  "!intent.Matches(receipt)", "if(version!=_generation)return;",
                  "_savedDraftId=receipt.DraftId", "if(!HasValidActor(actor)){ForceCancel();return;}"):
        # Preserve source formatting for the prior cancellation guard.
        needs("review", token.replace("if(_savingDraft) return;", "if (_savingDraft) return;"))
    needs("vm","Review.AttachDraftWriter(writer)")
    needs("shell","(screen == NativeScreen.DailyAttendance || !DailyAttendance.Review.IsSavingDraft) &&")
    needs("review_xaml","Binding SaveDraftCommand")
    for token in ("Draft_intent_requires_today_unique_ids_and_no_unstored_range",
                  "Save_is_explicit_verified_once_and_never_previews_or_commits_attendance",
                  "Missing_or_forged_receipt_never_claims_success_or_retries",
                  "Logout_discards_late_save_receipt_and_blocks_close_during_write"):
        needs("draft_save_tests",token)
    save_start=source.find("public async Task<NativeAttendanceDraft> SaveAttendanceDraftAsync(")
    if not (0<=save_start<start):
        issues.append("Missing canonical native draft save entry")
    else:
        save=source[save_start:start]
        order=[save.find(t) for t in ("using var nativeOperation = operations.Enter();",
            "intent.IsValid(DateOnly.FromDateTime(DateTime.Now))",
            "services.ResolveSessionAsync(actor.SessionNonce,ct)",
            "principal.UserId!=actor.UserId",
            "CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)",
            "var before=RequiredArray(await services.ListDraftsAsync",
            "services.SaveDraftAsync(principal.UserId,new JsonObject",
            "var after=RequiredArray(await services.ListDraftsAsync",
            "if(!intent.Matches(result) || updated>saved)")]
        if min(order)<0 or order!=sorted(order) or any(t in save for t in ("DeleteDraftAsync(","CommitAsync(","ExecuteInWriteTransactionAsync(")):
            issues.append("Draft save must use exact owner, one lease, fresh ID and canonical read-back without attendance write")
    for token in ("_rows.Any(r=>ReferenceEquals(r,row))",
                  "row.BusinessDate==DateOnly.FromDateTime(DateTime.Now)",
                  "!NativeAttendanceDateRange.RequiresRange(row.TargetStatusId)",
                  "a.UpdatedAtUtc==b.UpdatedAtUtc", "!SameEvidence(row,current)",
                  "bool restored=await restore(actor,row,initial,linked.Token)",
                  "if(restored){Clear();return;}", "_open && !_restoring",
                  "RestoreInitialCommand", "RestoreRevisionCommand"):
        needs("drafts",token)
    needs("vm","Drafts.AttachRestore(RestoreDraftReviewAsync)")
    vm=content["vm"]
    restore_start=vm.find("private async Task<bool> RestoreDraftReviewAsync(")
    restore_end=vm.find("private bool CanReviewPerson(",restore_start)
    if not (0<=restore_start<restore_end):
        issues.append("Missing native draft restore review entry")
    else:
        restore=vm[restore_start:restore_end]
        order=[restore.find(t) for t in (
            "_currentPrincipal?.Invoke()!=actor",
            "draft.BusinessDate!=DateOnly.FromDateTime(DateTime.Now)",
            "if(initial)",
            "await Review.OpenInitialPersonsAsync(draft.BusinessDate,draft.SiteId,draft.PersonIds,ct)",
            "await Review.OpenPersonsAsync(draft.BusinessDate,draft.SiteId,draft.PersonIds,ct)",
            "Review.StatusChoices.Any(choice=>choice.StatusId==draft.TargetStatusId)",
            "Review.ChooseStatus(draft.TargetStatusId)",
            "await Review.PreviewAsync(ct)",
            "Drafts.IsRestoring && Review.IsOpen && Review.HasPreview",
            "Review.IsInitial==initial && Review.SelectedStatusId==draft.TargetStatusId")]
        if min(order)<0 or order!=sorted(order) or any(t in restore for t in ("ConfirmAsync(","CommitAsync(","SaveDraftAsync(","DeleteDraftAsync(","ConfirmCommand.Execute")):
            issues.append("Restore requires explicit mode, fresh canonical preview, live actor and no automatic attendance/draft write")
    for token in ("Binding RestoreInitialCommand","Binding RestoreRevisionCommand","ElementName=AttendanceDraftGrid"):
        needs("xaml",token)
    for token in ("Restore_uses_explicit_mode_and_fresh_preview_never_commits",
                  "Forged_or_changed_draft_cannot_open_review",
                  "Old_or_incomplete_range_draft_cannot_be_restored",
                  "Wrong_mode_or_inactive_status_is_rejected_without_automatic_fallback",
                  "Logout_cancels_restore_and_discards_delayed_owner_read"):
        needs("draft_restore_tests",token)
    for name in ("core", "release"):
        needs(name, "python build/test-native-phase6-daily-roster-contract.py")
    for forbidden in ("new AttendanceCommitService(", "RunPendingAsync(",
                      "ExecuteInWriteTransactionAsync(", "new BridgePrincipal("):
        if forbidden in content["vm"] or forbidden in content["view"]:
            issues.append(f"No write or forged authority in daily WPF presentation: {forbidden}")
    return issues


if __name__ == "__main__":
    errors = check(TEXT)
    if errors:
        raise SystemExit("NATIVE PHASE 6 DAILY CONTRACT FAILED:\n" + "\n".join(errors))
    for key, text in (
        ("drafts", "!SameEvidence(row,current)"),
        ("drafts", "a.UpdatedAtUtc==b.UpdatedAtUtc"),
        ("drafts", "_rows.Any(r=>ReferenceEquals(r,row))"),
        ("vm", "await Review.PreviewAsync(ct)"),
        ("vm", "Drafts.IsRestoring && Review.IsOpen && Review.HasPreview"),
        ("xaml", "Binding RestoreInitialCommand"),
        ("xaml", "Binding RestoreRevisionCommand"),
        ("draft_intent", "!NativeAttendanceDateRange.RequiresRange(TargetStatusId)"),
        ("review", "_draftAttempted=true"),
        ("review", "!intent.Matches(receipt)"),
        ("reader", "services.SaveDraftAsync(principal.UserId,new JsonObject"),
        ("reader", "if(!intent.Matches(result) || updated>saved)"),
        ("shell", "(screen == NativeScreen.DailyAttendance || !DailyAttendance.Review.IsSavingDraft) &&"),
        ("review_xaml", "Binding SaveDraftCommand"),
        ("drafts", "Guid.TryParseExact(DraftId[4..]"),
        ("drafts", "if(!CanRead){Clear();return;}"),
        ("reader", "services.ListDraftsAsync(principal.UserId,lifetime.Token)"),
        ("vm", "Drafts.Clear();"),
        ("shell", "(screen == NativeScreen.DailyAttendance || !DailyAttendance.Drafts.IsOpen) &&"),
        ("xaml", "Binding OpenDraftsCommand"),
        ("vm", "request != _version"),
        ("vm", "_boundPrincipal != principal"),
        ("vm", "snapshot.HasNext !="),
        ("shell", "DailyAttendance.Clear();"),
        ("shell", "DailyAttendance.AttachReader(reader, () => _active)"),
        ("xaml", 'Header="الرتبة"'),
        ("xaml", "NativeAttendanceReviewView"),
        ("vm", "ReferenceEquals(item, row)"),
        ("review", "OpenPersonsAsync("),
        ("review_tests", "Forged_or_expired_daily_selection_cannot_open_review"),
        ("bulk_tests", "Duplicate_forged_and_expired_group_members_fail_closed"),
        ("vm", "SetSelectedRows("),
        ("vm", "OpenInitialPersonsAsync("),
        ("reader", 'IsUnprepared = !transferDay && item["statusId"] is null'),
        ("bulk_tests", "Mixed_initial_and_revision_selection_requires_separate_operations"),
        ("vm", "CanChangeRosterScope => CanRead && !Review.IsOpen"),
        ("vm", "Review.IsCompleted && !Review.HasUnverifiedCommit"),
        ("xaml", 'IsEnabled="{Binding CanChangeRosterScope}"'),
        ("bulk_tests", "Unverified_commit_freezes_scope_and_preserves_original_intent"),
        ("vm", "_selected.All(row => CanReviewPerson(row))"),
        ("xaml", 'SelectionMode="Extended"'),
        ("view", "OnDailySelectionChanged("),
        ("reader", "DailyRosterAsync(rosterQuery, ct)"),
        ("core", "python build/test-native-phase6-daily-roster-contract.py"),
        ("release", "python build/test-native-phase6-daily-roster-contract.py"),
    ):
        if text not in TEXT[key]:
            raise SystemExit(f"Stale negative-test target in {key}: {text}")
        altered = TEXT.copy()
        altered[key] = altered[key].replace(text, "REMOVED_BY_NEGATIVE_TEST")
        if not check(altered):
            raise SystemExit(f"Unsafe change incorrectly passed: {key}: {text}")
    print("NATIVE PHASE 6 DAILY PASS: SQLCipher, rank, nonce, single+bulk reviewed status, Core/Release")
