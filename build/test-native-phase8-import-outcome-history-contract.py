#!/usr/bin/env python3
"""Test-first structural security contract: Phase 8 slice 15 import outcomes."""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
P = {
    "canonical": "src/AlTamam.Application/Bridge/ExcelImportOutcomeService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativeImportOutcomeHistoryContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeImportOutcomeHistoryViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativeImportOutcomeHistoryView.xaml",
    "viewcode": "src/AlTamam.NativeDesktop/Views/NativeImportOutcomeHistoryView.xaml.cs",
    "host": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeImportOutcomeHistoryPhase8Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {k: (ROOT / v).read_text(encoding="utf-8") for k, v in P.items()}
REQUIRED = {
    "canonical": ("CreatedByUserId=$actor", "Status='COMMITTED'", "LIMIT 30",
                  "ORDER BY RowNumber,IssueId LIMIT 501", "public async Task<BridgeResponse> QueryAsync"),
    "contracts": ("interface INativeImportOutcomeHistoryBackend", "ReadRecentAsync(",
                  "ReadBatchAsync(", "NativeImportBatchIssue", "NativeImportBatchDetail"),
    "vm": ("RoleAuthorizationPolicy.CanAccessUsersManagement", "ReferenceEquals(b, row)",
           "version != _generation || linked.IsCancellationRequested",
           "version != _detailGeneration || linked.IsCancellationRequested",
           "if (!CanRead) { Close(); return; }", "_pending?.Cancel();",
           "_detailPending?.Cancel();", "detail.Batch != row",
           "detail.Issues.Count != row.RejectedRows", "batches.Count > 30",
           "x.AcceptedRows + x.RejectedRows != x.TotalRows",
           "Distinct(StringComparer.Ordinal).Count()", "public void Close()"),
    "adapter": ("INativeImportOutcomeHistoryBackend",
                "new ExcelImportOutcomeService(database)", "QueryAsync(null, actor.UserId, ct)",
                '["importBatchId"] = batchId', "await VerifyPersonnelManagerAsync(actor, ct)",
                "batch.ImportBatchId != batchId", "rejected.Count != batch.RejectedRows",
                "row.Status != \"COMMITTED\"", "row.TotalRows is < 1 or > 500"),
    "admin": ("ImportHistory = new NativeImportOutcomeHistoryViewModel();",
              "OpenImportHistoryCommand", "ImportHistory.AttachBackend(",
              "ImportHistory.Close();", "!ImportHistory.IsOpen"),
    "shell": ("Administration.ImportHistory.Close();", "Administration.Clear();"),
    "view": ("ItemsSource=\"{Binding Batches}\"",
             "ItemsSource=\"{Binding SelectedDetail.Issues}\"",
             "Command=\"{Binding DetailsCommand}\"",
             "Command=\"{Binding CloseCommand}\"", "IsReadOnly=\"True\""),
    "viewcode": ("InitializeComponent()",),
    "host": ("NativeImportOutcomeHistoryView", "OpenImportHistoryCommand", "Binding ImportHistory"),
    "tests": ("Anonymous_and_supervisor_are_denied_without_any_sql_read",
              "Manager_reads_only_bounded_committed_batches_and_never_writes",
              "Selected_original_batch_exposes_all_rejected_evidence",
              "Forged_batch_or_mismatched_issue_count_fails_closed",
              "Overlong_or_duplicate_history_is_hidden_not_partially_rendered",
              "Logout_cancels_pending_history_and_discards_late_replies",
              "Refresh_and_close_revoke_old_detail_and_pending_result"),
    "core": ("python build/test-native-phase8-import-outcome-history-contract.py",),
    "release": ("python build/test-native-phase8-import-outcome-history-contract.py",),
}


def problems(files):
    result = []
    for k, tokens in REQUIRED.items():
        for token in tokens:
            if token not in files[k]:
                result.append(f"{k}: missing critical contract {token}")
    for k in ("view", "host"):
        try:
            ElementTree.fromstring(files[k])
        except ElementTree.ParseError as exc:
            result.append(f"{k}: invalid XAML: {exc}")
    if files["tests"].count("[Fact]") < 7:
        result.append("tests: missing test-first cases")
    for k in ("contracts", "vm", "view", "viewcode"):
        for unsafe in ("INSERT INTO", "UPDATE Persons SET", "DELETE FROM",
                       "new SqliteConnection(", "new ExcelImportOutcomeService(",
                       "File.WriteAllBytes(", "SaveFileDialog",
                       "ExcelImportOutcomeService(database)"):
            if unsafe in files[k]:
                result.append(f"{k}: unexpected SQL/export writer {unsafe}")
    for k in ("core", "release"):
        lines = files[k].splitlines()
        marker = "python build/test-native-phase8-import-outcome-history-contract.py"
        matches = [i for i, line in enumerate(lines) if line.strip() == marker]
        if len(matches) != 1 or matches[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[matches[0] + 1]
        ):
            result.append(f"{k}: guard must run exactly once and fail closed")
    return result


if __name__ == "__main__":
    errors = problems(FILES)
    if errors:
        raise SystemExit("IMPORT OUTCOME CONTRACT FAIL:\n" + "\n".join(errors))
    for key, token in (
        ("canonical", "CreatedByUserId=$actor"),
        ("vm", "detail.Batch != row"),
        ("vm", "_detailPending?.Cancel();"),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("view", 'ItemsSource="{Binding SelectedDetail.Issues}"'),
        ("tests", "Logout_cancels_pending_history_and_discards_late_replies"),
        ("core", "python build/test-native-phase8-import-outcome-history-contract.py"),
        ("release", "python build/test-native-phase8-import-outcome-history-contract.py"),
    ):
        damaged = dict(FILES)
        damaged[key] = damaged[key].replace(token, "DELETED_FOR_NEGATIVE_TEST")
        if not problems(damaged):
            raise SystemExit(f"Negative case not covered: {key}: {token}")
    print("PASS: original actor-owned committed import history, no writes, secure WPF lifecycle")
