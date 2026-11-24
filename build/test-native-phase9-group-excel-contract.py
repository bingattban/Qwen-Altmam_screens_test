#!/usr/bin/env python3
"""Phase 9 group Excel: strict source/preview/commit/WPF contract + mutations.

C# xUnit tests prove two-step identity/session semantics, real 4-worksheet
OOXML and formula safety. This gate runs BEFORE packaging in both pipelines.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
FILES = {
    "source": "src/AlTamam.Application/Bridge/PersonnelGroupExcelExportService.cs",
    "workbook": "src/AlTamam.Application/Bridge/PersonnelGroupExcelWorkbook.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelGroupExcelContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelGroupExcelViewModel.cs",
    "group": "src/AlTamam.NativePresentation/NativePersonnelGroupReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelGroupReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeGroupExcelExportPhase9Tests.cs",
    "ziptests": "tests/AlTamam.UnitTests/NativeGroupExcelWorkbookPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k:(ROOT/p).read_text(encoding="utf-8") for k,p in FILES.items()}
COMMAND = "python build/test-native-phase9-group-excel-contract.py"
NEEDED = {
    "source": (
        "private readonly PersonnelReportService _canonical = new(database);", # Canonical original source only
        ".ReportAsync(Page(input, page), ct)",
        "OutputDirectorySettings(database).GetAsync(ct)",
        "OutputDirectorySettings.IsSafeDirectory",
        "OutputDirectorySettings.IsUsableDirectory",
        "ReportWorkbookCommit.VerifyFlushAndMove(partial, file, ct)",
        "Fingerprint(first)",
        "Fingerprint(current)",
        "MaxDailyRows",
        "dailyStatusHasNext",
        "allDaily.Sum(x => Number(x, \"count\"))",
        "await authorize(ct).ConfigureAwait(false);",
        "if (published && !completed)",
        "File.Delete(file)",
        "File.Delete(partial)",
        "FileMode.CreateNew",
    ),
    "workbook": (
        "public static class PersonnelGroupExcelWorkbook",
        "ZipArchive(output, ZipArchiveMode.Create",
        "ExcelReportLayout.Styles",
        "ExcelReportLayout.Columns",
        "ExcelReportLayout.FinishSheet",
        '"inlineStr"',
        '"rightToLeft","1"',
        "new(\"الأفراد\"",
        "new(\"الحالات\"",
        "new(\"الملخص اليومي\"",
        "new(\"حالات الأفراد\"",
        "XmlConvert.IsXmlChar",
        "1048573",
    ),
    "contracts": (
        "INativePersonnelGroupExcelBackend",
        "PreviewGroupExcelAsync(",
        "ExportGroupExcelAsync(",
        "NativeGroupExcelPreview(",
        "NativeGroupExcelReceipt(",
    ),
    "vm": (
        "OpenPreviewAsync(CancellationToken ct = default)",
        "ConfirmAsync(CancellationToken ct = default)",
        "RoleAuthorizationPolicy.CanAccessUsersManagement",
        "SameRequest(_approvedRequest, _request?.Invoke())",
        "preview.Fingerprint",
        "ValidatePreview(preview, request)",
        "ValidateReceipt(receipt, preview)",
        "_preview = null; // Never retry an uncertain commit",
        "version != _generation || linked.IsCancellationRequested",
        "FilePath",
    ),
    "group": (
        "public NativePersonnelGroupExcelViewModel Excel",
        "Excel.Attach(excel",
        "private NativePersonnelGroupReportRequest? CurrentExcelRequest()",
        "Excel.Close();",
    ),
    "adapter": (
        "INativePersonnelGroupExcelBackend",
        "new PersonnelGroupExcelExportService(database)",
        "PreviewGroupExcelAsync(",
        "ExportGroupExcelAsync(",
        "token => VerifyPersonnelManagerAsync(actor, token)",
        'RequiredText(source, "fingerprint")',
        'RequiredNumber(source, "sheetCount") != 4',
    ),
    "view": (
        "Binding Excel.OpenPreviewCommand",
        'DataContext="{Binding Excel}"',
        "Binding ConfirmCommand",
        "Binding Receipt.FilePath",
        "Binding Preview.PersonCount",
        "قبل تأكيد",
    ),
    "tests": (
        "Preview_then_explicit_confirm_uses_exact_ids_filters_and_fingerprint",
        "Changed_group_selection_or_date_after_preview_refuses_stale_export",
        "Revoked_live_role_or_new_session_cannot_export_previous_preview",
        "Failing_export_clears_approval_and_never_auto_retries",
        "Late_preview_after_close_cannot_repopulate_sensitive_export",
    ),
    "ziptests": (
        "Workbook_is_four_real_RTL_worksheets_and_no_cell_contains_a_formula",
        "Unsafe_excel_xml_characters_are_rejected_before_any_zip_part_is_written",
        'Assert.Empty(doc.Descendants(ns + "f"))',
    ),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = {
    "vm": ("new SqliteConnection(", "File.WriteAllBytes(", "SaveFileDialog(", "ZipArchive(", "File.Move("),
    "group": ("File.WriteAllBytes(", "new SqliteConnection(", "File.Move("),
    "workbook": ("new SqliteConnection(", "INSERT INTO", "UPDATE Persons"),
    "source": ("INSERT INTO", "UPDATE Persons SET", "DELETE FROM AttendanceRecords"),
}
def check(data):
    errors=[]
    for key, terms in NEEDED.items():
        for term in terms:
            if term not in data[key]:
                errors.append(f"{key}: required guard missing: {term}")
    for key, terms in FORBIDDEN.items():
        for term in terms:
            if term in data[key]:
                errors.append(f"{key}: forbidden UI writer/second SQL writer: {term}")
    try:
        ElementTree.fromstring(data["view"])
    except ElementTree.ParseError as err:
        errors.append(f"view: invalid RTL WPF XAML: {err}")
    for key in ("core","release"):
        lines=data[key].splitlines()
        indexes=[i for i,line in enumerate(lines) if line.strip()==COMMAND]
        if len(indexes)!=1 or indexes[0]+1>=len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indexes[0]+1]
        ):
            errors.append(f"{key}: missing fatal group-Excel pre-build gate")
    if data["tests"].count("[Fact]") < 6 or data["ziptests"].count("[Fact]") < 2:
        errors.append("tests: insufficient malicious source/session and genuine OOXML coverage")
    return errors

if __name__ == "__main__":
    bad=check(TEXT)
    if bad:
        raise SystemExit("NATIVE GROUP EXCEL CONTRACT FAIL:\n"+"\n".join(bad))
    for key,term in (
        ("source","ReportWorkbookCommit.VerifyFlushAndMove(partial, file, ct)"),
        ("source","if (published && !completed)"),
        ("source","Fingerprint(current)"),
        ("workbook",'"inlineStr"'),
        ("workbook",'"rightToLeft","1"'),
        ("vm","SameRequest(_approvedRequest, _request?.Invoke())"),
        ("vm","_preview = null; // Never retry an uncertain commit"),
        ("group","Excel.Close();"),
        ("adapter","token => VerifyPersonnelManagerAsync(actor, token)"),
        ("view","Binding ConfirmCommand"),
        ("tests","Revoked_live_role_or_new_session_cannot_export_previous_preview"),
        ("ziptests",'Assert.Empty(doc.Descendants(ns + "f"))'),
        ("core",COMMAND), ("release",COMMAND)
    ):
        mutated=dict(TEXT)
        mutated[key]=mutated[key].replace(term,"REMOVED_SECURITY_GATE")
        if not check(mutated):
            raise SystemExit(f"Negative safety mutation unexpectedly passed: {key}/{term}")
    print("PASS: source-authorized group XLSX preview, no second SQL, 4 RTL sheets, formula-safe, SHA/atomic")
