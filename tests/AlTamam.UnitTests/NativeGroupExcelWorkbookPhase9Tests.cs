using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AlTamam.Application.Bridge;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Real OOXML/ZIP evidence: four Arabic worksheets, original source keys and
/// inline-only strings. Never accept formula injection disguised as a name.
/// </summary>
public sealed class NativeGroupExcelWorkbookPhase9Tests
{
    private static JsonObject Canonical(string name = "=SUM(1,2)") => new()
    {
        ["mode"] = "group",
        ["personCount"] = 1,
        ["dateFrom"] = "",
        ["dateTo"] = "",
        ["siteId"] = "",
        ["statusId"] = "",
        ["movementType"] = 0,
        ["attendanceTotal"] = 2L,
        ["movementTotal"] = 1L,
        ["persons"] = new JsonArray(new JsonObject
        {
            ["personId"] = "PER-KEY-01",
            ["name"] = name,
            ["serviceNumberA"] = "00001",
            ["firstRecordedDate"] = "2024-01-01",
            ["organizationalStatus"] = 1,
            ["active"] = true,
            ["attendanceTotal"] = 2L,
            ["movementTotal"] = 1L,
            ["statusTotals"] = new JsonArray(new JsonObject
            {
                ["statusId"] = "STS-01",
                ["statusName"] = "حاضر",
                ["count"] = 2L
            })
        }),
        ["statusTotals"] = new JsonArray(new JsonObject
        {
            ["statusId"] = "STS-01",
            ["statusName"] = "حاضر",
            ["count"] = 2L
        })
    };

    [Fact]
    public void Workbook_is_four_real_RTL_worksheets_and_no_cell_contains_a_formula()
    {
        using var buffer = new MemoryStream();
        PersonnelGroupExcelWorkbook.Write(buffer, Canonical(),
        [
            new JsonObject
            {
                ["date"]="2026-11-10", ["statusId"]="STS-01",
                ["statusName"]="حاضر", ["count"]=2L, ["personCount"]=1L
            }
        ]);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        string[] names = ["الأفراد", "الحالات", "الملخص اليومي", "حالات الأفراد"];
        using var workbook = zip.GetEntry("xl/workbook.xml")!.Open();
        var work = XDocument.Load(workbook);
        Assert.Equal(names, work.Descendants(ns + "sheet")
            .Select(x => x.Attribute("name")?.Value).ToArray());
        for (int i = 1; i <= 4; i++)
        {
            using var sheet = zip.GetEntry($"xl/worksheets/sheet{i}.xml")!.Open();
            var doc = XDocument.Load(sheet);
            Assert.Equal("1", doc.Descendants(ns + "sheetView")
                .Single().Attribute("rightToLeft")?.Value);
            Assert.Empty(doc.Descendants(ns + "f")); // No Excel formula cells.
            Assert.All(doc.Descendants(ns + "c"), cell =>
                Assert.Equal("inlineStr", cell.Attribute("t")?.Value));
        }
        using var personSheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var text = XDocument.Load(personSheet);
        Assert.Contains("=SUM(1,2)", text.Descendants(ns + "t")
            .Select(element => element.Value));
        Assert.Contains("PER-KEY-01", text.Descendants(ns + "t")
            .Select(element => element.Value));
    }

    [Fact]
    public void Unsafe_excel_xml_characters_are_rejected_before_any_zip_part_is_written()
    {
        using var buffer = new MemoryStream();
        var error = Assert.Throws<InvalidDataException>(() =>
            PersonnelGroupExcelWorkbook.Write(buffer, Canonical("سجل\u0001فاسد"), []));
        Assert.Contains("text", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, buffer.Length);
    }
}
