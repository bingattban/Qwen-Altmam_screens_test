using System.Globalization;
using System.Text.Json.Nodes;
using Sheet = AlTamam.Application.Bridge.PersonnelGroupExcelWorkbook.Sheet;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Pure XLSX projection of a complete canonical individual report. No database
/// access or UI authorization lives here. The caller must assemble and verify
/// all source pages and the approved immutable PersonId before writing.
/// </summary>
public static class PersonnelIndividualExcelWorkbook
{
    private const int MaxHistoryRows = 20_000;
    private const int MaxSummaryRows = 5_000;

    public static void Write(Stream output, JsonObject complete)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(complete);
        if (!output.CanWrite) throw new InvalidDataException("Invalid workbook output.");
        var person = Object(complete["person"]);
        var personId = Cell(person, "personId");
        if (personId.Length is < 3 or > 128 || personId.Any(char.IsControl))
            throw new InvalidDataException("Invalid immutable person identity.");

        var attendance = History(complete, "attendance", "attendanceTotal");
        var identities = History(complete, "identityHistory", "identityTotal");
        var movements = History(complete, "movements", "movementTotal");
        var revisions = History(complete, "revisions", "revisionTotal");
        var audit = History(complete, "audit", "auditTotal");
        var sites = History(complete, "siteHistory", "siteTotal");
        var statuses = Rows(complete, "statusTotals", MaxSummaryRows);
        var months = Rows(complete, "monthlyTotals", MaxSummaryRows);
        if (statuses.Sum(r => Count(r, "count", MaxHistoryRows)) != attendance.Length ||
            months.Sum(r => Count(r, "count", MaxHistoryRows)) != attendance.Length)
            throw new InvalidDataException("Individual attendance summary mismatch.");

        string descriptor = "معرف الفرد: " + personId +
            " — من: " + Cell(complete, "dateFrom") +
            " — إلى: " + Cell(complete, "dateTo") +
            " — الموقع: " + Cell(complete, "siteId") +
            " — الحالة: " + Cell(complete, "statusId") +
            " — نوع الحركة: " + Count(complete, "movementType", 4);
        var customProjection = PersonnelGroupExcelWorkbook.CurrentCustomProjection(complete, [person]);
        var identityRow = Project(person, ["personId","name","serviceNumberA","firstRecordedDate",
            "#organizationalStatus","!active"]).Concat(
            new[] {attendance.Length, identities.Length, movements.Length,
                revisions.Length, audit.Length, sites.Length}
            .Select(n => n.ToString(CultureInfo.InvariantCulture)))
            .Concat(customProjection.Values[0]).ToArray();

        Sheet[] sheets =
        [
            new("الفرد", ["معرف الفرد الثابت","الاسم الحالي","الرقم (ع) الحالي","أول تسجيل",
                "الحالة التنظيمية","نشط","أيام التحضير","سجلات الهوية","الحركات","التعديلات",
                "العمليات","سجلات المواقع", ..customProjection.Headings], [identityRow]),
            Table("الحالات", ["معرف الحالة","الحالة التاريخية","عدد الأيام","أول تاريخ","آخر تاريخ"],
                statuses, ["statusId","statusName","#count","firstDate","lastDate"]),
            Table("الملخص الشهري", ["الشهر","معرف الحالة","الحالة التاريخية","عدد الأيام"],
                months, ["period","statusId","statusName","#count"]),
            Table("التحضير", ["التاريخ","الحالة","الرمز","الموقع التاريخي","المصدر","المراجعة",
                "وقت الحفظ UTC","الاسم التاريخي","الرقم (ع) التاريخي"],
                attendance, ["date","statusName","symbol","site","source","#revision",
                    "committedAtUtc","?name","?serviceNumberA"]),
            Table("الهوية", ["الاسم التاريخي","الرقم (ع) التاريخي","من","إلى",
                "معرف المنفذ","السبب","وقت التسجيل UTC"],
                identities, ["name","serviceNumberA","from","?to","actorUserId","reason","recordedAtUtc"]),
            Table("الحركات", ["النوع","المصدر","الوجهة","السبب","التاريخ","وقت الحفظ UTC",
                "معرف المنفذ","الاسم التاريخي","الرقم (ع) التاريخي","المنفذ"],
                movements, ["type","source","destination","?reason","date","committedAtUtc",
                    "actorUserId","name","serviceNumberA","executor"]),
            Table("التعديلات", ["التاريخ","قبل","بعد","السبب","وقت التنفيذ UTC","طريقة التفويض","المنفذ"],
                revisions, ["date","before","after","?reason","executedAtUtc","?authorizationMethod","executor"]),
            Table("العمليات", ["العملية","اسم المستخدم","الاسم الكامل","المنفذ",
                "التاريخ الفعلي","وقت التنفيذ UTC","الوصف","السبب"],
                audit, ["operation","username","fullName","executor","?effectiveDate",
                    "executedAtUtc","description","?reason"]),
            Table("المواقع", ["معرف الموقع","من","إلى","الموقع التاريخي","وقت الإنشاء UTC"],
                sites, ["siteId","from","?to","siteName","createdAtUtc"])
        ];
        // Raw audit JSON remains excluded. Current custom values belong only
        // to the identity sheet and must never replace saved historical facts.
        PersonnelGroupExcelWorkbook.WriteSheets(output, sheets,
            "التقرير التاريخي الفردي", descriptor);
    }

    private static JsonObject Object(JsonNode? value) => value as JsonObject ??
        throw new InvalidDataException("Invalid individual history object.");

    private static JsonObject[] Rows(JsonObject source, string key, int maximum)
    {
        if (source[key] is not JsonArray array || array.Count > maximum)
            throw new InvalidDataException("Missing or oversized individual history: " + key);
        return array.Select(Object).ToArray();
    }

    private static JsonObject[] History(JsonObject source, string key, string total)
    {
        var rows = Rows(source, key, MaxHistoryRows);
        if (rows.Length != Count(source, total, MaxHistoryRows))
            throw new InvalidDataException("Incomplete individual history: " + key);
        return rows;
    }

    private static long Count(JsonObject source, string key, long maximum)
    {
        if (source[key] is not JsonValue value ||
            !long.TryParse(value.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ||
            n < 0 || n > maximum)
            throw new InvalidDataException("Invalid numeric history value: " + key);
        return n;
    }

    private static string Cell(JsonObject source, string field)
    {
        string key = field[0] is '?' or '#' or '!' ? field[1..] : field;
        if (field[0] == '#')
            return Count(source, key, long.MaxValue).ToString(CultureInfo.InvariantCulture);
        if (!source.TryGetPropertyValue(key, out var node))
            throw new InvalidDataException("Missing history field: " + key);
        if (node is null && field[0] == '?') return "";
        if (node is JsonValue value)
        {
            if (field[0] == '!' && value.TryGetValue<bool>(out var flag))
                return flag ? "true" : "false";
            if (field[0] != '!' && value.TryGetValue<string>(out var text) && text is not null)
                return text;
        }
        throw new InvalidDataException("Invalid history field: " + key);
    }

    private static string[] Project(JsonObject row, string[] fields) =>
        fields.Select(field => Cell(row, field)).ToArray();

    private static Sheet Table(string title, string[] headings, JsonObject[] rows, string[] fields) =>
        new(title, headings, rows.Select(row => Project(row, fields)).ToArray());
}
