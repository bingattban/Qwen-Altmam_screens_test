using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Excel.Reports;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Original immutable PersonId history is the ONLY source of this group XLSX.
/// Reuses the existing offline output-directory policy and atomic OpenXML commit;
/// no new reporting SQL, no writes to SQLCipher, no browser-selected path.
/// </summary>
public sealed class PersonnelGroupExcelExportService(AlTamamDbContext database)
{
    private const int MaxDailyRows = 20000;
    private readonly PersonnelReportService _canonical = new(database);

    private static JsonObject Page(JsonObject input, int page)
    {
        var payload = (JsonObject)input.DeepClone();
        payload["summaryPage"] = page;
        return payload;
    }

    private static string Text(JsonObject data, string key) =>
        data[key]?.GetValue<string>() ??
        throw new InvalidDataException("Missing canonical history field " + key);

    private static long Number(JsonObject data, string key)
    {
        if (data[key] is not JsonValue value)
            throw new InvalidDataException("Missing canonical numeric field " + key);
        if (value.TryGetValue<long>(out long wide)) return wide;
        if (value.TryGetValue<int>(out int integer)) return integer;
        throw new InvalidDataException("Malformed canonical numeric field " + key);
    }

    private static JsonArray Array(JsonObject data, string key) =>
        data[key] as JsonArray ??
        throw new InvalidDataException("Missing canonical history array " + key);

    private static JsonObject Obj(JsonNode? node) =>
        node as JsonObject ??
        throw new InvalidDataException("Corrupted historical report item.");

    private static bool Bool(JsonObject data, string key) =>
        data[key]?.GetValue<bool>() ??
        throw new InvalidDataException("Missing canonical paging flag " + key);

    private static bool ValidRequest(JsonObject? input)
    {
        if (input?["personIds"] is not JsonArray ids ||
            ids.Count is < 1 or > 100)
            return false;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in ids)
        {
            string? id;
            try { id = node?.GetValue<string>(); } catch { return false; }
            if (id is not { Length: >= 3 and <= 128 } ||
                id.Any(char.IsControl) || !unique.Add(id)) return false;
        }
        try
        {
            string from = input["dateFrom"]?.GetValue<string>() ?? "";
            string to = input["dateTo"]?.GetValue<string>() ?? "";
            string site = input["siteId"]?.GetValue<string>() ?? "";
            string status = input["statusId"]?.GetValue<string>() ?? "";
            int movement = input["movementType"]?.GetValue<int>() ?? 0;
            static bool IsDate(string date) => date.Length == 0 ||
                DateOnly.TryParseExact(date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            return IsDate(from) && IsDate(to) &&
                   (from.Length == 0 || to.Length == 0 ||
                    string.CompareOrdinal(from, to) <= 0) &&
                   site.Length <= 128 && status.Length <= 128 &&
                   !site.Any(char.IsControl) && !status.Any(char.IsControl) &&
                   movement is >= 0 and <= 4;
        }
        catch { return false; }
    }

    private static string[] Ids(JsonObject request) =>
        Array(request, "personIds").Select(node =>
            node?.GetValue<string>() ??
            throw new InvalidDataException("Missing group person ID")).ToArray();

    private static void Verify(JsonObject source, JsonObject request, int page)
    {
        string[] ids = Ids(request);
        JsonArray people = Array(source, "persons");
        JsonArray status = Array(source, "statusTotals");
        JsonArray days = Array(source, "dailyStatusTotals");
        if (Text(source, "mode") != "group" ||
            Number(source, "personCount") != ids.Length ||
            Number(source, "dailyStatusPage") != page ||
            Text(source, "dateFrom") != (request["dateFrom"]?.GetValue<string>() ?? "") ||
            Text(source, "dateTo") != (request["dateTo"]?.GetValue<string>() ?? "") ||
            Text(source, "siteId") != (request["siteId"]?.GetValue<string>() ?? "") ||
            Text(source, "statusId") != (request["statusId"]?.GetValue<string>() ?? "") ||
            Number(source, "movementType") != (request["movementType"]?.GetValue<int>() ?? 0) ||
            people.Count != ids.Length || status.Count > 5000 ||
            days.Count > 100 || (Bool(source, "dailyStatusHasNext") && days.Count != 100) ||
            Number(source, "attendanceTotal") < 0 ||
            Number(source, "movementTotal") < 0)
            throw new InvalidDataException("Canonical group report pagination or source echo mismatch.");

        long totalAttendance = 0, totalMovements = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            var person = Obj(people[i]);
            if (Text(person, "personId") != ids[i])
                throw new InvalidDataException("Group report person identity/order mismatch.");
            long attended = Number(person, "attendanceTotal");
            long movements = Number(person, "movementTotal");
            var totals = Array(person, "statusTotals");
            if (attended < 0 || movements < 0 || totals.Count > 5000 ||
                totals.Sum(t => Number(Obj(t), "count")) != attended)
                throw new InvalidDataException("Per-person source totals disagree.");
            totalAttendance = checked(totalAttendance + attended);
            totalMovements = checked(totalMovements + movements);
        }
        if (Number(source, "attendanceTotal") != totalAttendance ||
            Number(source, "movementTotal") != totalMovements ||
            status.Sum(t => Number(Obj(t), "count")) != totalAttendance)
            throw new InvalidDataException("Whole group source totals disagree.");
        foreach (var node in days)
        {
            var row = Obj(node);
            long count = Number(row, "count"), distinct = Number(row, "personCount");
            if (count < 0 || distinct < 0 || distinct > ids.Length ||
                distinct > count || Text(row, "date").Length != 10)
                throw new InvalidDataException("Invalid canonical daily aggregation.");
        }
    }

    // Excludes page-limited daily rows. Includes all selected identities,
    // filters, per-person/status totals and both aggregate counters.
    public static string Fingerprint(JsonObject report)
    {
        var canonical = new JsonObject
        {
            ["mode"] = report["mode"]?.DeepClone(),
            ["personCount"] = report["personCount"]?.DeepClone(),
            ["dateFrom"] = report["dateFrom"]?.DeepClone(),
            ["dateTo"] = report["dateTo"]?.DeepClone(),
            ["siteId"] = report["siteId"]?.DeepClone(),
            ["statusId"] = report["statusId"]?.DeepClone(),
            ["movementType"] = report["movementType"]?.DeepClone(),
            ["persons"] = report["persons"]?.DeepClone(),
            ["statusTotals"] = report["statusTotals"]?.DeepClone(),
            ["attendanceTotal"] = report["attendanceTotal"]?.DeepClone(),
            ["movementTotal"] = report["movementTotal"]?.DeepClone()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            canonical.ToJsonString()))).ToLowerInvariant();
    }

    private async Task<BridgeResponse> ReadAsync(
        JsonObject input, int page, CancellationToken ct) =>
        await _canonical.ReportAsync(Page(input, page), ct).ConfigureAwait(false);

    public async Task<BridgeResponse> PreviewAsync(JsonObject? input, CancellationToken ct)
    {
        if (!ValidRequest(input))
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "تحديد المجموعة أو مرشحات التاريخ غير صالح.");
        var response = await ReadAsync(input!, 1, ct).ConfigureAwait(false);
        if (!response.Success) return response;
        try
        {
            JsonObject data = Obj(response.Data);
            Verify(data, input!, 1);
            return BridgeHelpers.Ok(new JsonObject
            {
                ["personIds"] = data["persons"] is JsonArray people ?
                    new JsonArray(people.Select(row =>
                        (JsonNode?)JsonValue.Create(Text(Obj(row), "personId"))).ToArray())
                    : throw new InvalidDataException("Missing group persons"),
                ["personCount"] = Number(data, "personCount"),
                ["attendanceTotal"] = Number(data, "attendanceTotal"),
                ["movementTotal"] = Number(data, "movementTotal"),
                ["dateFrom"] = Text(data, "dateFrom"),
                ["dateTo"] = Text(data, "dateTo"),
                ["siteId"] = Text(data, "siteId"),
                ["statusId"] = Text(data, "statusId"),
                ["movementType"] = Number(data, "movementType"),
                ["fingerprint"] = Fingerprint(data),
                ["suggestedFileName"] = "تقرير_جماعي_" +
                    DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                    ".xlsx",
                ["format"] = "xlsx"
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "بيانات التقرير الجماعي غير متطابقة؛ لا يمكن اعتماد التصدير.");
        }
    }

    /// <summary>
    /// Authorize on every canonical page and both sides of final file commit.
    /// A cancellation or changed original source prevents publishing a partial
    /// spreadsheet. All generated filenames are server-generated and unique.
    /// </summary>
    public async Task<BridgeResponse> ExportAsync(
        JsonObject? input, string expectedFingerprint,
        Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        if (!ValidRequest(input) ||
            expectedFingerprint is not { Length: 64 } ||
            expectedFingerprint.Any(ch => !char.IsAsciiHexDigit(ch)))
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "معاينة تقرير Excel غير صالحة أو منتهية.");

        await authorize(ct).ConfigureAwait(false);
        var firstResponse = await ReadAsync(input!, 1, ct).ConfigureAwait(false);
        await authorize(ct).ConfigureAwait(false);
        if (!firstResponse.Success) return firstResponse;

        JsonObject first;
        var allDaily = new List<JsonObject>();
        try
        {
            first = Obj(firstResponse.Data);
            string normalizedExpected = expectedFingerprint.ToLowerInvariant();
            Verify(first, input!, 1);
            if (!string.Equals(Fingerprint(first), normalizedExpected,
                StringComparison.Ordinal))
                throw new InvalidDataException("Historical report changed after operator approval.");

            int page = 1;
            JsonObject current = first;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                Verify(current, input!, page);
                if (Fingerprint(current) != normalizedExpected)
                    throw new InvalidDataException("Historical data changed during Excel export.");
                foreach (var item in Array(current, "dailyStatusTotals"))
                {
                    if (allDaily.Count >= MaxDailyRows)
                        throw new InvalidDataException("Export exceeds bounded history; no silent truncation.");
                    allDaily.Add(Obj(item).DeepClone().AsObject());
                }
                if (!Bool(current, "dailyStatusHasNext")) break;
                if (page >= MaxDailyRows / 100)
                    throw new InvalidDataException("Unbounded daily group export.");
                await authorize(ct).ConfigureAwait(false);
                var answer = await ReadAsync(input!, ++page, ct).ConfigureAwait(false);
                await authorize(ct).ConfigureAwait(false);
                if (!answer.Success)
                    throw new InvalidDataException("A historical daily page was not available.");
                current = Obj(answer.Data);
            }
            if (allDaily.Sum(x => Number(x, "count")) !=
                Number(first, "attendanceTotal"))
                throw new InvalidDataException("Daily history does not reconcile to original group totals.");

            // Catch source drift after the entire paged read, not just before it.
            await authorize(ct).ConfigureAwait(false);
            var verifyResponse = await ReadAsync(input!, 1, ct).ConfigureAwait(false);
            await authorize(ct).ConfigureAwait(false);
            if (!verifyResponse.Success ||
                Fingerprint(Obj(verifyResponse.Data)) != normalizedExpected ||
                Array(Obj(verifyResponse.Data), "dailyStatusTotals").ToJsonString() !=
                    Array(first, "dailyStatusTotals").ToJsonString())
                throw new InvalidDataException("Historical source changed during export.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "تغيرت بيانات التقرير أو تجاوزت حد Excel الآمن؛ لم يتم نشر ملف ناقص.");
        }

        string chosen;
        try { chosen = await new OutputDirectorySettings(database).GetAsync(ct).ConfigureAwait(false); }
        catch (InvalidOperationException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "لم يتم تحديد مجلد التقارير المعتمد.");
        }
        if (!OutputDirectorySettings.IsSafeDirectory(chosen, database.DatabasePath, out string folder) ||
            !OutputDirectorySettings.IsUsableDirectory(chosen, database.DatabasePath))
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "مجلد التقارير المحلي غير متاح أو غير قابل للكتابة.");

        string name = "تقرير_جماعي_" +
            DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
            "_" + Guid.NewGuid().ToString("N")[..10] + ".xlsx";
        string file = Path.Combine(folder, name);
        string partial = file + ".part";
        bool published = false;
        bool completed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            using(var stream = new FileStream(partial, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                PersonnelGroupExcelWorkbook.Write(stream, first, allDaily);
            }
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ReportWorkbookCommit.VerifyFlushAndMove(partial, file, ct);
            published = true;
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            completed = true;
            return BridgeHelpers.Ok(new JsonObject
            {
                ["filePath"] = file,
                ["format"] = "xlsx",
                ["fingerprint"] = expectedFingerprint.ToLowerInvariant(),
                ["personCount"] = Number(first, "personCount"),
                ["attendanceTotal"] = Number(first, "attendanceTotal"),
                ["movementTotal"] = Number(first, "movementTotal"),
                ["sheetCount"] = 4,
                ["dailyRows"] = allDaily.Count
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
            or System.Security.SecurityException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "لم يكتمل ملف Excel أو لم يجتز تحقق السلامة؛ لم يُنشر ملف ناقص.");
        }
        finally
        {
            // Revocation or cancellation *after* the atomic rename still
            // deletes the newly created export rather than disclosing data.
            if (published && !completed)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
