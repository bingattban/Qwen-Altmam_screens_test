using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using AlTamam.Infrastructure.Excel.Reports;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Pure Office Open XML group-history workbook renderer. Original SQLCipher
/// PersonId evidence is supplied by PersonnelReportService; this class never
/// reads the database or interprets historic identity from mutable names.
/// All cells are genuine inline strings, never executable Excel formulas.
/// </summary>
public static class PersonnelGroupExcelWorkbook
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static JsonArray Array(JsonObject source, string field) =>
        source[field] as JsonArray ??
        throw new InvalidDataException("Missing group source table " + field);
    private static JsonObject Obj(JsonNode? node) =>
        node as JsonObject ??
        throw new InvalidDataException("Malformed group workbook row");
    private static string Text(JsonObject source, string field) =>
        source[field]?.GetValue<string>() ??
        throw new InvalidDataException("Missing group text: " + field);
    private static string Number(JsonObject source, string field) =>
        source[field]?.ToString() ??
        throw new InvalidDataException("Missing group number: " + field);
    private static string SafeText(string value)
    {
        if (value.Length > 32767 || value.Any(c => !XmlConvert.IsXmlChar(c)))
            throw new InvalidDataException("Historical text exceeds safe Excel text limit.");
        return value;
    }
    internal sealed record Sheet(string Name, string[] Headings,
        IReadOnlyList<string[]> Rows);

    public static void Write(Stream output, JsonObject original,
        IReadOnlyList<JsonObject> daily)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(daily);
        if (!output.CanWrite || daily.Count > 20000)
            throw new InvalidDataException("Cannot write a bounded group workbook.");

        var people = Array(original, "persons").Select(Obj).ToArray();
        if (people.Length is < 1 or > 100)
            throw new InvalidDataException("Group workbook person bounds exceeded.");
        var allStatuses = Array(original, "statusTotals").Select(Obj).ToArray();
        if (allStatuses.Length > 5000)
            throw new InvalidDataException("Group status table exceeds safety bounds.");

        var personRows = people.Select(p => new[]
        {
            Text(p,"personId"), Text(p,"name"),
            Text(p,"serviceNumberA"), Text(p,"firstRecordedDate"),
            Number(p,"organizationalStatus"), p["active"]?.ToString() ?? "",
            Number(p,"attendanceTotal"), Number(p,"movementTotal")
        }).ToArray();
        var totalsRows = allStatuses.Select(s => new[]
        {
            Text(s,"statusId"), Text(s,"statusName"), Number(s,"count")
        }).ToArray();
        var dailyRows = daily.Select(s => new[]
        {
            Text(s,"date"), Text(s,"statusId"), Text(s,"statusName"),
            Number(s,"count"), Number(s,"personCount")
        }).ToArray();
        var personStatusRows = new List<string[]>();
        foreach (var person in people)
        {
            string id = Text(person, "personId");
            string name = Text(person, "name");
            var totals = Array(person, "statusTotals");
            if (totals.Count > 5000)
                throw new InvalidDataException("Person status breakdown exceeded the safe limit.");
            foreach(var item in totals)
            {
                var row = Obj(item);
                personStatusRows.Add(
                [
                    id, name, Text(row,"statusId"),
                    Text(row,"statusName"), Number(row,"count")
                ]);
            }
        }
        string descriptor = "من: " + Text(original,"dateFrom") +
            " — إلى: " + Text(original,"dateTo") +
            " — الموقع: " + Text(original,"siteId") +
            " — الحالة: " + Text(original,"statusId") +
            " — نوع الحركة: " + Number(original,"movementType");
        var sheets = new Sheet[]
        {
            new("الأفراد", ["معرف الفرد الثابت", "الاسم الحالي", "الرقم (ع) الحالي",
                "أول يوم مسجل", "الحالة التنظيمية", "نشط", "أيام التحضير", "الحركات"],
                personRows),
            new("الحالات", ["معرف الحالة", "الحالة التاريخية", "أيام التحضير"], totalsRows),
            new("الملخص اليومي", ["التاريخ", "معرف الحالة",
                "الحالة التاريخية", "أيام التحضير", "عدد الأفراد"], dailyRows),
            new("حالات الأفراد", ["معرف الفرد الثابت", "الاسم الحالي",
                "معرف الحالة", "الحالة التاريخية", "أيام التحضير"], personStatusRows)
        };
        WriteSheets(output, sheets, "التقرير التاريخي الجماعي", descriptor);
    }

    // Shared OOXML packaging only: callers supply distinct canonical projections.
    internal static void WriteSheets(Stream output, IReadOnlyList<Sheet> sheets,
        string title, string descriptor)
    {
        SafeText(title);
        SafeText(descriptor);
        foreach (var sheet in sheets)
        {
            if (sheet.Rows.Count > 1048573) // 3 heading/metadata rows.
                throw new InvalidDataException("Excel worksheet row limit exceeded.");
            SafeText(sheet.Name);
            foreach (var heading in sheet.Headings) SafeText(heading);
            foreach (var row in sheet.Rows)
            {
                if (row.Length != sheet.Headings.Length)
                    throw new InvalidDataException("Column count mismatch.");
                foreach (string cell in row) SafeText(cell);
            }
        }

        using var zip = new ZipArchive(output, ZipArchiveMode.Create,
            leaveOpen:true, entryNameEncoding:Encoding.UTF8);
        Part(zip, "[Content_Types].xml", w =>
        {
            w.WriteStartElement("Types", PackageNs);
            w.WriteStartElement("Default", PackageNs);
            w.WriteAttributeString("Extension","rels");
            w.WriteAttributeString("ContentType","application/vnd.openxmlformats-package.relationships+xml");
            w.WriteEndElement();
            w.WriteStartElement("Default", PackageNs);
            w.WriteAttributeString("Extension","xml");
            w.WriteAttributeString("ContentType","application/xml");
            w.WriteEndElement();
            ContentType(w, "/xl/workbook.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            ContentType(w, "/xl/styles.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            for(int i=1;i<=sheets.Count;i++)
                ContentType(w, $"/xl/worksheets/sheet{i}.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            w.WriteEndElement();
        });
        Part(zip,"_rels/.rels",w => Relationships(w,
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
             "xl/workbook.xml")));
        Part(zip,"xl/_rels/workbook.xml.rels",w =>
        {
            var relations = Enumerable.Range(1,sheets.Count)
                .Select(i => ($"rId{i}",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    $"worksheets/sheet{i}.xml"))
                .Append(($"rId{sheets.Count+1}",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
                    "styles.xml")).ToArray();
            Relationships(w,relations);
        });
        Part(zip,"xl/styles.xml",ExcelReportLayout.Styles);
        Part(zip,"xl/workbook.xml",w =>
        {
            w.WriteStartElement("workbook",Ns);
            w.WriteAttributeString("xmlns","r",null,RelNs);
            w.WriteStartElement("sheets",Ns);
            for(int i=0;i<sheets.Count;i++)
            {
                w.WriteStartElement("sheet",Ns);
                w.WriteAttributeString("name",sheets[i].Name);
                w.WriteAttributeString("sheetId",(i+1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("r","id",RelNs,$"rId{i+1}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            ExcelReportLayout.PrintTitles(w,
                sheets.Select(s => (s.Name,3)).ToArray());
            w.WriteEndElement();
        });
        for(int i=0;i<sheets.Count;i++)
        {
            Sheet sheet=sheets[i];
            Part(zip,$"xl/worksheets/sheet{i+1}.xml",w =>
            {
                w.WriteStartElement("worksheet",Ns);
                ExcelReportLayout.SheetProperties(w);
                w.WriteStartElement("sheetViews",Ns);
                w.WriteStartElement("sheetView",Ns);
                w.WriteAttributeString("workbookViewId","0");
                w.WriteAttributeString("rightToLeft","1");
                w.WriteStartElement("pane",Ns);
                w.WriteAttributeString("ySplit","3");
                w.WriteAttributeString("topLeftCell","A4");
                w.WriteAttributeString("activePane","bottomLeft");
                w.WriteAttributeString("state","frozen");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
                ExcelReportLayout.Columns(w,sheet.Headings);
                w.WriteStartElement("sheetData",Ns);
                WriteRow(w,1,["التمام اليومي — "+title+" / "+sheet.Name],3);
                WriteRow(w,2,[descriptor],3);
                WriteRow(w,3,sheet.Headings,3);
                for(int row=0;row<sheet.Rows.Count;row++)
                    WriteRow(w,row+4,sheet.Rows[row],3);
                w.WriteEndElement();
                ExcelReportLayout.FinishSheet(w,sheet.Headings.Length,sheet.Rows.Count+3,3);
                w.WriteEndElement();
            });
        }
    }

    private static void WriteRow(XmlWriter w, int row, IReadOnlyList<string> fields, int header)
    {
        w.WriteStartElement("row",Ns);
        w.WriteAttributeString("r",row.ToString(CultureInfo.InvariantCulture));
        int col=0;
        foreach(var value in fields)
        {
            ++col;
            w.WriteStartElement("c",Ns);
            w.WriteAttributeString("r",
                ExcelReportLayout.Column(col)+row.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("s",ExcelReportLayout.Style(row,header));
            w.WriteAttributeString("t","inlineStr"); // Never write Excel formulas.
            w.WriteStartElement("is",Ns);
            w.WriteStartElement("t",Ns);
            w.WriteString(value);
            w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
        }
        w.WriteEndElement();
    }
    private static void Part(ZipArchive zip,string path,Action<XmlWriter> action)
    {
        var entry=zip.CreateEntry(path,CompressionLevel.Optimal);
        using var target=entry.Open();
        using var writer=XmlWriter.Create(target,new XmlWriterSettings
        {
            Encoding=new UTF8Encoding(false),CloseOutput=false
        });
        writer.WriteStartDocument();
        action(writer);
        writer.WriteEndDocument();
    }
    private static void ContentType(XmlWriter w,string part,string type)
    {
        w.WriteStartElement("Override",PackageNs);
        w.WriteAttributeString("PartName",part);
        w.WriteAttributeString("ContentType",type);
        w.WriteEndElement();
    }
    private static void Relationships(XmlWriter w,
        params (string Id,string Type,string Target)[] relations)
    {
        w.WriteStartElement("Relationships",RelationshipNs);
        foreach(var rel in relations)
        {
            w.WriteStartElement("Relationship",RelationshipNs);
            w.WriteAttributeString("Id",rel.Id);
            w.WriteAttributeString("Type",rel.Type);
            w.WriteAttributeString("Target",rel.Target);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
