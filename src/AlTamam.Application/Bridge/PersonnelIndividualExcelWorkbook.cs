using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using AlTamam.Infrastructure.Excel.Reports;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Pure Office Open XML renderer over six ORIGINAL immutable PersonId streams,
/// plus full saved historical status and monthly aggregates. No database access,
/// calculated retroactive identity, SQL, user-supplied filenames or formulas.
/// </summary>
public static class PersonnelIndividualExcelWorkbook
{
    private const string Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageNs="http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipNs="http://schemas.openxmlformats.org/package/2006/relationships";

    private static JsonObject Obj(JsonNode? node) =>
        node as JsonObject ?? throw new InvalidDataException("Missing saved individual history source.");
    private static JsonArray Arr(JsonObject source,string name) =>
        source[name] as JsonArray ?? throw new InvalidDataException("Missing saved individual history "+name);
    private static string Text(JsonObject source,string name,string absent="غير موثق تاريخيا")
    {
        if(source[name] is null)return absent;
        if(source[name] is JsonValue value)
            return value.ToString();
        throw new InvalidDataException("Unsupported person-history cell "+name);
    }
    private static string Safe(string value)
    {
        if(value.Length>32767 || value.Any(ch=>!XmlConvert.IsXmlChar(ch)))
            throw new InvalidDataException("Excel history cell is not safe XML text.");
        return value;
    }
    private sealed record Sheet(string Name,string[] Header,IReadOnlyList<string[]> Rows);

    public static void Write(Stream output,JsonObject original,
        IReadOnlyDictionary<string,List<JsonObject>> history)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(history);
        if(!output.CanWrite)throw new InvalidDataException("The Excel output is not writable.");
        string[] streamNames=
            ["attendance","movements","identityHistory","siteHistory","revisions","audit"];
        foreach(string stream in streamNames)
            if(!history.TryGetValue(stream,out var rows) || rows.Count>20000)
                throw new InvalidDataException("Individual history source incomplete or unbounded.");

        var person=Obj(original["person"]);
        string id=Text(person,"personId"),name=Text(person,"name");
        var sheets=new Sheet[]
        {
            new("التحضير",["التاريخ","الحالة التاريخية","رمز الحالة","الموقع التاريخي",
                "المصدر","تعديل السجل","وقت الحفظ","الاسم التاريخي","الرقم التاريخي"],
                history["attendance"].Select(p=>new[]
                {
                    Text(p,"date"),Text(p,"statusName"),Text(p,"symbol"),
                    Text(p,"site"),Text(p,"source"),Text(p,"revision"),
                    Text(p,"committedAtUtc"),Text(p,"name"),
                    Text(p,"serviceNumberA")
                }).ToArray()),
            new("الحركات",["نوع الحركة","الموقع المصدر","الوجهة","السبب","تاريخ الحركة",
                "وقت الحفظ","اسم الفرد التاريخي","الرقم التاريخي","المنفذ"],
                history["movements"].Select(p=>new[]
                {
                    Text(p,"type"),Text(p,"source"),Text(p,"destination"),
                    Text(p,"reason"),Text(p,"date"),Text(p,"committedAtUtc"),
                    Text(p,"name"),Text(p,"serviceNumberA"),Text(p,"executor")
                }).ToArray()),
            new("سجل الهوية",["الاسم التاريخي","الرقم التاريخي",
                "من تاريخ","إلى تاريخ","معرف المستخدم المنفذ","السبب","وقت التسجيل"],
                history["identityHistory"].Select(p=>new[]
                {
                    Text(p,"name"),Text(p,"serviceNumberA"),Text(p,"from"),
                    Text(p,"to"),Text(p,"actorUserId"),Text(p,"reason"),
                    Text(p,"recordedAtUtc")
                }).ToArray()),
            new("انتساب المواقع",["معرف الموقع","اسم الموقع التاريخي",
                "من تاريخ","إلى تاريخ","وقت الحفظ"],
                history["siteHistory"].Select(p=>new[]
                {
                    Text(p,"siteId"),Text(p,"siteName"),Text(p,"from"),
                    Text(p,"to"),Text(p,"createdAtUtc")
                }).ToArray()),
            new("تصحيحات التحضير",["التاريخ","الحالة قبل","الحالة بعد",
                "السبب","وقت التصحيح","طريقة التفويض","المنفذ"],
                history["revisions"].Select(p=>new[]
                {
                    Text(p,"date"),Text(p,"before"),Text(p,"after"),
                    Text(p,"reason"),Text(p,"executedAtUtc"),
                    Text(p,"authorizationMethod"),Text(p,"executor")
                }).ToArray()),
            new("سجل العمليات",["العملية","اسم المستخدم","اسم المنفذ","التاريخ الفعلي",
                "وقت التنفيذ","الوصف","السبب","القيمة القديمة الأصلية",
                "القيمة الجديدة الأصلية"],
                history["audit"].Select(p=>new[]
                {
                    Text(p,"operation"),Text(p,"username"),Text(p,"fullName"),
                    Text(p,"effectiveDate"),Text(p,"executedAtUtc"),
                    Text(p,"description"),Text(p,"reason"),
                    Text(p,"oldValueJson"),Text(p,"newValueJson")
                }).ToArray()),
            new("إجماليات الحالات",["معرف الحالة","الحالة التاريخية","عدد الأيام",
                "أول يوم","آخر يوم"],
                Arr(original,"statusTotals").Select(node=>
                {
                    var p=Obj(node);
                    return new[]{Text(p,"statusId"),Text(p,"statusName"),
                        Text(p,"count"),Text(p,"firstDate"),Text(p,"lastDate")};
                }).ToArray()),
            new("الإجماليات الشهرية",["الشهر","معرف الحالة","الحالة التاريخية","عدد الأيام"],
                Arr(original,"monthlyTotals").Select(node=>
                {
                    var p=Obj(node);
                    return new[]{Text(p,"period"),Text(p,"statusId"),
                        Text(p,"statusName"),Text(p,"count")};
                }).ToArray())
        };
        string descriptor="معرف الفرد: "+id+" | الاسم الحالي: "+name+
            " | الرقم (ع): "+Text(person,"serviceNumberA")+
            " | من: "+Text(original,"dateFrom","")+
            " | إلى: "+Text(original,"dateTo","")+
            " | الموقع: "+Text(original,"siteId","")+
            " | الحالة: "+Text(original,"statusId","")+
            " | نوع الحركة: "+Text(original,"movementType","0");
        Safe(descriptor);
        foreach(var sheet in sheets)
        {
            Safe(sheet.Name);
            foreach(var header in sheet.Header)Safe(header);
            if(sheet.Rows.Count>1048573)
                throw new InvalidDataException("Individual Excel rows exceed worksheet bounds.");
            foreach(var row in sheet.Rows)
            {
                if(row.Length!=sheet.Header.Length)
                    throw new InvalidDataException("Individual Excel schema columns mismatch.");
                foreach(var cell in row)Safe(cell);
            }
        }
        using var zip=new ZipArchive(output,ZipArchiveMode.Create,
            leaveOpen:true,entryNameEncoding:Encoding.UTF8);
        Part(zip,"[Content_Types].xml",w=>
        {
            w.WriteStartElement("Types",PackageNs);
            foreach(var (ext,type) in new[]{
                ("rels","application/vnd.openxmlformats-package.relationships+xml"),
                ("xml","application/xml")})
            {
                w.WriteStartElement("Default",PackageNs);
                w.WriteAttributeString("Extension",ext);
                w.WriteAttributeString("ContentType",type);
                w.WriteEndElement();
            }
            ContentType(w,"/xl/workbook.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            ContentType(w,"/xl/styles.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            for(int i=1;i<=sheets.Length;i++)
                ContentType(w,$"/xl/worksheets/sheet{i}.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            w.WriteEndElement();
        });
        Part(zip,"_rels/.rels",w=>Relationships(w,
            ("rId1","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                "xl/workbook.xml")));
        Part(zip,"xl/_rels/workbook.xml.rels",w=>
        {
            var relations=Enumerable.Range(1,sheets.Length).Select(i=>
                ($"rId{i}",
                 "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                 $"worksheets/sheet{i}.xml"))
                .Append(($"rId{sheets.Length+1}",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
                    "styles.xml")).ToArray();
            Relationships(w,relations);
        });
        Part(zip,"xl/styles.xml",ExcelReportLayout.Styles);
        Part(zip,"xl/workbook.xml",w=>
        {
            w.WriteStartElement("workbook",Ns);
            w.WriteAttributeString("xmlns","r",null,RelNs);
            w.WriteStartElement("sheets",Ns);
            for(int i=0;i<sheets.Length;i++)
            {
                w.WriteStartElement("sheet",Ns);
                w.WriteAttributeString("name",sheets[i].Name);
                w.WriteAttributeString("sheetId",(i+1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("r","id",RelNs,$"rId{i+1}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            ExcelReportLayout.PrintTitles(w,
                sheets.Select(x=>(x.Name,3)).ToArray());
            w.WriteEndElement();
        });
        for(int i=0;i<sheets.Length;i++)
        {
            var sheet=sheets[i];
            Part(zip,$"xl/worksheets/sheet{i+1}.xml",w=>
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
                w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
                ExcelReportLayout.Columns(w,sheet.Header);
                w.WriteStartElement("sheetData",Ns);
                Row(w,1,["التمام اليومي — التقرير الفردي التاريخي / "+sheet.Name],3);
                Row(w,2,[descriptor],3);
                Row(w,3,sheet.Header,3);
                for(int j=0;j<sheet.Rows.Count;j++)Row(w,j+4,sheet.Rows[j],3);
                w.WriteEndElement();
                ExcelReportLayout.FinishSheet(w,sheet.Header.Length,sheet.Rows.Count+3,3);
                w.WriteEndElement();
            });
        }
    }
    private static void Row(XmlWriter w,int row,IReadOnlyList<string> values,int header)
    {
        w.WriteStartElement("row",Ns);
        w.WriteAttributeString("r",row.ToString(CultureInfo.InvariantCulture));
        int col=0;
        foreach(string text in values)
        {
            col++;
            w.WriteStartElement("c",Ns);
            w.WriteAttributeString("r",ExcelReportLayout.Column(col)+
                row.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("s",ExcelReportLayout.Style(row,header));
            w.WriteAttributeString("t","inlineStr"); // Formula injection denied.
            w.WriteStartElement("is",Ns);
            w.WriteStartElement("t",Ns);
            w.WriteString(text);
            w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
        }
        w.WriteEndElement();
    }
    private static void Part(ZipArchive zip,string name,Action<XmlWriter> render)
    {
        var entry=zip.CreateEntry(name,CompressionLevel.Optimal);
        using var stream=entry.Open();
        using var writer=XmlWriter.Create(stream,new XmlWriterSettings
        {
            Encoding=new UTF8Encoding(false),CloseOutput=false
        });
        writer.WriteStartDocument();
        render(writer);
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
        params (string Id,string Type,string Target)[] relationships)
    {
        w.WriteStartElement("Relationships",RelationshipNs);
        foreach(var (id,type,target) in relationships)
        {
            w.WriteStartElement("Relationship",RelationshipNs);
            w.WriteAttributeString("Id",id);
            w.WriteAttributeString("Type",type);
            w.WriteAttributeString("Target",target);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }
}
