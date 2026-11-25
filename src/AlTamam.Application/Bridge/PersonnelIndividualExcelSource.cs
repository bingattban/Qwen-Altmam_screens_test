using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Persistence;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Collects the existing individual report's six independently paged histories.
/// No alternate SQL, file writes or database mutations. The caller supplies
/// a live authorization callback; every canonical read is bracketed by it.
/// Two complete reads detect observed drift, not transactional isolation.
/// </summary>
public sealed class PersonnelIndividualExcelSource(AlTamamDbContext database)
{
    private readonly PersonnelReportService _canonical = new(database);
    private const int PageSize = 100;
    private const int MaxRows = 20_000;
    private const int MaxSummaryRows = 5_000;
    private const long MaxSourceBytes = 64L * 1024 * 1024;
    private static readonly (string Rows, string Prefix)[] Streams =
    [
        ("attendance", "attendance"), ("identityHistory", "identity"),
        ("movements", "movement"), ("revisions", "revision"),
        ("audit", "audit"), ("siteHistory", "site")
    ];

    public Task<JsonObject> ReadCompleteAsync(JsonObject request,
        Func<CancellationToken, Task> authorize, CancellationToken ct) =>
        CollectAsync(request, _canonical.ReportAsync, authorize, ct);

    public static async Task<JsonObject> CollectAsync(JsonObject request,
        Func<JsonObject, CancellationToken, Task<BridgeResponse>> read,
        Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(authorize);
        var input = Normalize(request);
        var fingerprints = new List<string>();
        JsonObject? header = null;
        JsonObject? complete = null;
        int pages = 1;
        long bytes = 0;

        async Task<JsonObject> ReadPage(int page)
        {
            ct.ThrowIfCancellationRequested();
            var payload = (JsonObject)input.DeepClone();
            foreach (var (_, prefix) in Streams) payload[prefix + "Page"] = page;
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var answer = await read(payload, ct).ConfigureAwait(false);
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!answer.Success || answer.Data is not JsonObject data)
                throw new InvalidDataException("Canonical individual history unavailable.");
            return data;
        }

        for (int pass = 0; pass < 2; pass++)
        {
            for (int page = 1; page <= pages; page++)
            {
                var data = await ReadPage(page).ConfigureAwait(false);
                var currentHeader = VerifyHeader(data, input);
                if (header is null)
                {
                    header = currentHeader;
                    complete = (JsonObject)header.DeepClone();
                    foreach (var (rows, prefix) in Streams)
                    {
                        long total = Number(header, prefix + "Total");
                        pages = Math.Max(pages, (int)((total + PageSize - 1) / PageSize));
                        complete[rows] = new JsonArray();
                    }
                }
                else if (!JsonNode.DeepEquals(header, currentHeader))
                    throw new InvalidDataException("Individual report header changed between reads.");

                var evidence = new JsonObject();
                foreach (var (rows, prefix) in Streams)
                {
                    long total = Number(header, prefix + "Total");
                    var source = Array(data, rows, PageSize);
                    long offset = (long)(page - 1) * PageSize;
                    long expected = Math.Clamp(total - offset, 0L, PageSize);
                    if (Number(data, prefix + "Page") != page ||
                        Flag(data, prefix + "HasNext") != (offset + PageSize < total) ||
                        source.Count != expected)
                        throw new InvalidDataException("Individual history page mismatch: " + rows);

                    var visible = new JsonArray();
                    foreach (var item in source)
                    {
                        var row = (JsonObject)Object(item).DeepClone();
                        if (rows == "audit")
                        {
                            // Do not retain raw old/new payloads in export data.
                            row.Remove("oldValueJson");
                            row.Remove("newValueJson");
                        }
                        visible.Add(row);
                        if (pass == 0) complete![rows]!.AsArray().Add(row.DeepClone());
                    }
                    evidence[rows] = visible;
                }
                string serialized = evidence.ToJsonString();
                string digest = Hash(serialized);
                if (pass == 0)
                {
                    bytes = checked(bytes + Encoding.UTF8.GetByteCount(serialized));
                    if (bytes > MaxSourceBytes)
                        throw new InvalidDataException("Individual history exceeds safe export size.");
                    fingerprints.Add(digest);
                }
                else if (fingerprints[page - 1] != digest)
                    throw new InvalidDataException("Individual history changed during complete verification.");
            }
        }
        ct.ThrowIfCancellationRequested();
        return complete ?? throw new InvalidDataException("No canonical individual history.");
    }

    /// <summary>Hash the full collected visible history, not just its totals.</summary>
    public static string Fingerprint(JsonObject complete)
    {
        ArgumentNullException.ThrowIfNull(complete);
        return Hash(complete.ToJsonString());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonObject Normalize(JsonObject request)
    {
        var id = Text(request, "personId");
        var from = Text(request, "dateFrom");
        var to = Text(request, "dateTo");
        var site = Text(request, "siteId");
        var status = Text(request, "statusId");
        long movement = Number(request, "movementType");
        static bool ValidDate(string value) => value.Length == 0 ||
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
        static bool ValidKey(string value) => value.Length <= 128 &&
            value == value.Trim() && !value.Any(char.IsControl);
        if (request.ContainsKey("personIds") || id.Length < 3 || !ValidKey(id) ||
            !ValidKey(site) || !ValidKey(status) || !ValidDate(from) || !ValidDate(to) ||
            (from.Length != 0 && to.Length != 0 && string.CompareOrdinal(from, to) > 0) ||
            movement is < 0 or > 4)
            throw new InvalidDataException("Invalid individual export request.");
        return new JsonObject
        {
            ["personId"] = id, ["dateFrom"] = from, ["dateTo"] = to,
            ["siteId"] = site, ["statusId"] = status, ["movementType"] = (int)movement
        };
    }

    private static JsonObject VerifyHeader(JsonObject source, JsonObject input)
    {
        var person = Object(source["person"]);
        if (Text(person, "personId") != Text(input, "personId") ||
            Number(source, "pageSize") != PageSize ||
            Number(source, "movementType") != Number(input, "movementType"))
            throw new InvalidDataException("Wrong canonical individual identity or page size.");
        var result = new JsonObject { ["person"] = person.DeepClone() };
        foreach (string field in new[] { "dateFrom", "dateTo", "siteId", "statusId" })
        {
            string text = Text(source, field);
            if (text != Text(input, field))
                throw new InvalidDataException("Canonical individual filter mismatch.");
            result[field] = text;
        }
        result["movementType"] = Number(source, "movementType");
        foreach (var (_, prefix) in Streams)
        {
            long total = Number(source, prefix + "Total");
            if (total < 0 || total > MaxRows)
                throw new InvalidDataException("Individual history exceeds safe row bound.");
            result[prefix + "Total"] = total;
        }
        foreach (string field in new[] { "statusTotals", "monthlyTotals" })
        {
            var array = Array(source, field, MaxSummaryRows);
            long sum = 0;
            foreach (var item in array)
            {
                long count = Number(Object(item), "count");
                if (count <= 0 || count > MaxRows)
                    throw new InvalidDataException("Invalid individual summary count.");
                sum += count;
            }
            if (sum != Number(result, "attendanceTotal"))
                throw new InvalidDataException("Individual history summary disagrees with total.");
            result[field] = array.DeepClone();
        }
        return result;
    }

    private static JsonObject Object(JsonNode? node) => node as JsonObject ??
        throw new InvalidDataException("Invalid canonical individual history object.");

    private static JsonArray Array(JsonObject data, string key, int max) =>
        data[key] is JsonArray array && array.Count <= max ? array :
            throw new InvalidDataException("Invalid canonical individual history array: " + key);

    private static string Text(JsonObject data, string key) =>
        data[key] is JsonValue value && value.TryGetValue<string>(out var text) && text is not null ?
            text : throw new InvalidDataException("Missing canonical text: " + key);

    private static long Number(JsonObject data, string key) =>
        data[key] is JsonValue value &&
        long.TryParse(value.ToJsonString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n : throw new InvalidDataException("Missing canonical integer: " + key);

    private static bool Flag(JsonObject data, string key) =>
        data[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag :
            throw new InvalidDataException("Missing canonical page flag: " + key);
}
