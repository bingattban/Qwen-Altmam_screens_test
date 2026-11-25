using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AlTamam.Application.Bridge;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeIndividualExcelWorkbookPhase9Tests
{
    private static JsonObject Complete() => JsonNode.Parse("""
        {
          "person":{"personId":"PER-001","name":"=SUM(1,2)","serviceNumberA":"00007","firstRecordedDate":"2024-01-01","organizationalStatus":1,"active":true},
          "dateFrom":"2026-01-01","dateTo":"2026-12-31","siteId":"","statusId":"","movementType":0,
          "attendanceTotal":1,"identityTotal":1,"movementTotal":1,"revisionTotal":1,"auditTotal":1,"siteTotal":1,
          "statusTotals":[{"statusId":"STS-1","statusName":"حاضر","count":1,"firstDate":"2026-02-01","lastDate":"2026-02-01"}],
          "monthlyTotals":[{"period":"2026-02","statusId":"STS-1","statusName":"حاضر","count":1}],
          "attendance":[{"date":"2026-02-01","statusName":"حاضر","symbol":"ح","site":"موقع سابق","source":"manual","revision":2,"committedAtUtc":"2026-02-01T08:00:00Z","name":null,"serviceNumberA":null}],
          "identityHistory":[{"name":"اسم سابق","serviceNumberA":"00006","from":"2024-01-01","to":null,"actorUserId":"USR-1","reason":"تصحيح","recordedAtUtc":"2024-01-01T08:00:00Z"}],
          "movements":[{"type":"نقل داخلي","source":"أ","destination":"ب","reason":null,"date":"2026-02-01","committedAtUtc":"2026-02-01T08:00:00Z","actorUserId":"USR-1","name":"اسم سابق","serviceNumberA":"00006","executor":"مسؤول"}],
          "revisions":[{"date":"2026-02-01","before":"غائب","after":"حاضر","reason":"تصحيح","executedAtUtc":"2026-02-01T09:00:00Z","authorizationMethod":null,"executor":"مسؤول"}],
          "audit":[{"operation":"تعديل","username":"admin","fullName":"المسؤول","executor":"admin - المسؤول","effectiveDate":null,"executedAtUtc":"2026-02-01T09:00:00Z","description":"وصف","reason":null}],
          "siteHistory":[{"siteId":"SITE-1","from":"2024-01-01","to":null,"siteName":"موقع سابق","createdAtUtc":"2024-01-01T08:00:00Z"}]
        }
        """)!.AsObject();

    [Fact]
    public void Complete_individual_history_has_nine_RTL_sheets_and_literal_original_identity()
    {
        using var buffer = new MemoryStream();
        PersonnelIndividualExcelWorkbook.Write(buffer, Complete());
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument Read(string path)
        {
            using var stream = zip.GetEntry(path)!.Open();
            return XDocument.Load(stream);
        }
        var names = Read("xl/workbook.xml").Descendants(ns + "sheet")
            .Select(e => e.Attribute("name")!.Value).ToArray();
        Assert.Equal(new[] {"الفرد","الحالات","الملخص الشهري","التحضير","الهوية","الحركات","التعديلات","العمليات","المواقع"}, names);
        for (int i = 1; i <= names.Length; i++)
        {
            var sheet = Read($"xl/worksheets/sheet{i}.xml");
            Assert.Equal("1", sheet.Descendants(ns + "sheetView").Single().Attribute("rightToLeft")!.Value);
            Assert.Empty(sheet.Descendants(ns + "f"));
            Assert.All(sheet.Descendants(ns + "c"), c => Assert.Equal("inlineStr", c.Attribute("t")!.Value));
            Assert.Equal(4, sheet.Descendants(ns + "row").Count());
        }
        var person = Read("xl/worksheets/sheet1.xml");
        Assert.Contains("=SUM(1,2)", person.Descendants(ns + "t").Select(x => x.Value));
        Assert.Contains("00007", person.Descendants(ns + "t").Select(x => x.Value));
        var daily = Read("xl/worksheets/sheet4.xml");
        Assert.DoesNotContain("00007", daily.Descendants(ns + "t").Select(x => x.Value));
        Assert.DoesNotContain("=SUM(1,2)", daily.Descendants(ns + "t").Select(x => x.Value));
        Assert.Contains("موقع سابق", daily.Descendants(ns + "t").Select(x => x.Value));
    }

    [Theory]
    [InlineData("attendanceTotal")]
    [InlineData("identityTotal")]
    [InlineData("movementTotal")]
    [InlineData("revisionTotal")]
    [InlineData("auditTotal")]
    [InlineData("siteTotal")]
    public void Incomplete_history_is_rejected_before_any_zip_is_written(string total)
    {
        var report = Complete();
        report[total] = 2;
        using var buffer = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => PersonnelIndividualExcelWorkbook.Write(buffer, report));
        Assert.Equal(0L, buffer.Length);
    }

    [Fact]
    public void Missing_table_or_inconsistent_monthly_totals_is_rejected()
    {
        var report = Complete();
        report.Remove("siteHistory");
        using var buffer = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => PersonnelIndividualExcelWorkbook.Write(buffer, report));
        report = Complete();
        report["monthlyTotals"]![0]!["count"] = 2;
        Assert.Throws<InvalidDataException>(() => PersonnelIndividualExcelWorkbook.Write(buffer, report));
        Assert.Equal(0L, buffer.Length);
    }

    [Fact]
    public void Invalid_XML_text_is_rejected_before_writing()
    {
        var report = Complete();
        report["person"]!["name"] = "bad\u0001";
        using var buffer = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => PersonnelIndividualExcelWorkbook.Write(buffer, report));
        Assert.Equal(0L, buffer.Length);
    }
}
