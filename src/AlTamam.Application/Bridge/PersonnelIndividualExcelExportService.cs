using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Excel.Reports;
using AlTamam.Infrastructure.Persistence;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Backend-only individual history Excel publication. The trusted native caller
/// must enforce session identity, role, explicit single-use approval and audit
/// before exposing this service to operators. No new bridge action is registered.
/// </summary>
public sealed class PersonnelIndividualExcelExportService(AlTamamDbContext database)
{
    private readonly PersonnelIndividualExcelSource _source = new(database);
    private readonly OutputDirectorySettings _settings = new(database);

    private static readonly string[] Totals =
    [
        "attendanceTotal", "identityTotal", "movementTotal",
        "revisionTotal", "auditTotal", "siteTotal"
    ];

    public async Task<BridgeResponse> PreviewAsync(JsonObject request,
        Func<CancellationToken, Task> authorize, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        try
        {
            ct.ThrowIfCancellationRequested();
            await authorize(ct).ConfigureAwait(false);
            var complete = await _source.ReadCompleteAsync(request, authorize, ct)
                .ConfigureAwait(false);
            var folder = await ReadFolderAsync(ct).ConfigureAwait(false);
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var result = Metadata(complete);
            result["directory"] = folder;
            result["fingerprint"] = ApprovalFingerprint(complete, folder);
            result["format"] = "xlsx";
            result["sheetCount"] = 9;
            return BridgeHelpers.Ok(result);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException
            or OverflowException or InvalidOperationException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "تعذر اعتماد معاينة التقرير الفردي أو مجلد الحفظ المحلي.");
        }
    }

    public async Task<BridgeResponse> ExportAsync(JsonObject request,
        string expectedFingerprint, Func<CancellationToken, Task> authorize,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorize);
        if (expectedFingerprint is not { Length: 64 } ||
            expectedFingerprint.Any(c => !char.IsAsciiHexDigit(c)))
            return BridgeHelpers.Error(BridgeErrorCodes.ValidationError,
                "يجب اعتماد معاينة التقرير قبل التصدير.");

        string? temporary = null, destination = null;
        bool ownsTemporary = false, published = false, completed = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            await authorize(ct).ConfigureAwait(false);
            var complete = await _source.ReadCompleteAsync(request, authorize, ct)
                .ConfigureAwait(false);
            string folder = await ReadFolderAsync(ct).ConfigureAwait(false);
            string fingerprint = ApprovalFingerprint(complete, folder);
            if (!string.Equals(expectedFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Individual history or destination changed since preview.");
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // Only the confirmed operation may probe write access or create files.
            if (!OutputDirectorySettings.IsUsableDirectory(folder, database.DatabasePath))
                throw new InvalidDataException("Configured local export directory is unavailable.");
            string filename = "تقرير_فردي_" +
                DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                "_" + Guid.NewGuid().ToString("N") + ".xlsx";
            destination = Path.Combine(folder, filename);
            temporary = destination + ".part";
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                PersonnelIndividualExcelWorkbook.Write(stream, complete);
            }

            // Recheck the configured destination and live authorization before
            // publishing. A setting change requires a new preview.
            string currentFolder = await ReadFolderAsync(ct).ConfigureAwait(false);
            if (!string.Equals(folder, currentFolder, StringComparison.Ordinal))
                throw new InvalidDataException("Export directory changed before publication.");
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ReportWorkbookCommit.VerifyFlushAndMove(temporary, destination, ct);
            published = true;
            ownsTemporary = false;
            await authorize(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var receipt = Metadata(complete);
            receipt["filePath"] = destination;
            receipt["format"] = "xlsx";
            receipt["sheetCount"] = 9;
            receipt["fingerprint"] = fingerprint;
            completed = true;
            return BridgeHelpers.Ok(receipt);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException
            or OverflowException or ArgumentException or InvalidOperationException
            or System.Security.SecurityException)
        {
            return BridgeHelpers.Error(BridgeErrorCodes.ExcelExportFailed,
                "تعذر تصدير التقرير الفردي؛ تحقق من البيانات والمجلد وأعد المعاينة.");
        }
        finally
        {
            // Never delete a pre-existing export or a file we did not create.
            if (ownsTemporary && temporary is not null) DeleteOwned(temporary);
            if (published && !completed && destination is not null) DeleteOwned(destination);
        }
    }

    private async Task<string> ReadFolderAsync(CancellationToken ct)
    {
        string configured = await _settings.GetAsync(ct).ConfigureAwait(false);
        if (!OutputDirectorySettings.IsSafeDirectory(configured, database.DatabasePath,
            out string folder))
            throw new InvalidDataException("Unsafe or missing configured export directory.");
        return folder;
    }

    private static string ApprovalFingerprint(JsonObject complete, string folder)
    {
        var scope = new JsonObject
        {
            ["source"] = PersonnelIndividualExcelSource.Fingerprint(complete),
            ["directory"] = folder
        };
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(scope.ToJsonString()))).ToLowerInvariant();
    }

    private static JsonObject Metadata(JsonObject complete)
    {
        var person = complete["person"] as JsonObject ??
            throw new InvalidDataException("Missing individual export identity.");
        var result = new JsonObject();
        foreach (string key in new[] { "personId", "name", "serviceNumberA" })
            result[key] = person[key]?.DeepClone() ??
                throw new InvalidDataException("Missing individual export identity field.");
        foreach (string key in new[] { "dateFrom", "dateTo", "siteId", "statusId", "movementType" }
            .Concat(Totals))
            result[key] = complete[key]?.DeepClone() ??
                throw new InvalidDataException("Missing individual export scope.");
        return result;
    }

    private static void DeleteOwned(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
