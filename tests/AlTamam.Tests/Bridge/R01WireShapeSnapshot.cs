using System.Text.Json;
using AlTamam.Contracts.Bridge;
using Xunit;

namespace AlTamam.Tests.Bridge;

// Contract snapshots retain field names/types, never credentials, nonces,
// generated IDs, file paths or person values. Business assertions remain in
// the native lifecycle tests; shape equality alone is not business acceptance.
internal static class R01WireShapeSnapshot
{
    internal static string Shape(BridgeResponse response)
    {
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return Describe(wire.RootElement);
    }

    internal static void Verify(string action, BridgeResponse response)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        const string relative = "tests/AlTamam.Tests/Bridge/Snapshots/R01NativeConfigurationResponseShapes.json";
        while (root is not null && !File.Exists(Path.Combine(root.FullName, relative))) root = root.Parent;
        Assert.NotNull(root);
        using var snapshots = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, relative)));
        Assert.Contains(Shape(response), snapshots.RootElement.GetProperty(action)
            .EnumerateArray().Select(item => item.GetString()!));
    }

    private static string Describe(JsonElement value, string? field = null)
    {
        // A source-scoped filter selector is a bounded list of immutable IDs
        // and historical labels. Its cardinality (including empty arrays)
        // varies with the selected person, but its exact item contract must
        // remain strict across every individual-report legacy alias.
        if ((field == "filterSites" || field == "filterStatuses") &&
            value.ValueKind == JsonValueKind.Array)
        {
            Assert.InRange(value.GetArrayLength(), 0, 500);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in value.EnumerateArray())
            {
                Assert.Equal(JsonValueKind.Object, choice.ValueKind);
                Assert.Equal(2, choice.EnumerateObject().Count());
                var idField = choice.GetProperty("id");
                var titleField = choice.GetProperty("title");
                Assert.Equal(JsonValueKind.String, idField.ValueKind);
                Assert.Equal(JsonValueKind.String, titleField.ValueKind);
                string id = idField.GetString()!;
                string title = titleField.GetString()!;
                Assert.InRange(id.Length, 1, 128);
                Assert.InRange(title.Length, 1, 240);
                Assert.True(!id.Any(char.IsControl), "Historical filter ID has control characters");
                Assert.True(!title.Any(char.IsControl), "Historical filter title has control characters");
                Assert.True(ids.Add(id), "Duplicate historical site/status filter ID");
            }
            return "array<canonicalFilterOption>";
        }
        // Monthly days is an ISO-date-keyed map, not a set of fixed DTO fields.
        // Validate the key contract without pinning snapshots to a calendar day.
        if (field == "days" && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in value.EnumerateObject())
                Assert.True(DateOnly.TryParseExact(item.Name, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _), "Invalid monthly date key: " + item.Name);
            return "dateMap<" + string.Join("|", value.EnumerateObject()
                .Select(item => Describe(item.Value)).Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)) + ">";
        }
        return value.ValueKind switch
        {
            JsonValueKind.Object => "object{" + string.Join(",", value.EnumerateObject()
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => item.Name + ":" + Describe(item.Value, item.Name))) + "}",
            JsonValueKind.Array => "array<" + string.Join("|", value.EnumerateArray()
                .Select(item => Describe(item)).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)) + ">",
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
            JsonValueKind.Null => "null",
            _ => throw new InvalidOperationException("Unsupported JSON wire value")
        };
    }
}
