using System.IO;

namespace AlTamam.NativePresentation;

/// <summary>Checks the canonical writer's complete receipt, not inferred files
/// or a directory scan. Site counts may overlap historically.</summary>
public static class NativeMonthlyExcelReceiptEvidence
{
    public static IReadOnlyList<NativeMonthlyExcelFile> ValidateAndFreeze(
        NativeMonthlyExcelReceipt receipt,NativeMonthlyExcelFilter filter)
    {
        if(receipt.Period!=filter.Period || receipt.SiteId!=filter.SiteId ||
           receipt.SearchTerm!=filter.SearchTerm || receipt.PersonCount is <1 or >100000 ||
           receipt.FileCount is <1 or >10001 || receipt.Files is not { } files ||
           files.Count!=receipt.FileCount || (filter.SiteId.Length>0 && files.Count!=1))
            throw new InvalidDataException("Incomplete monthly Excel file receipt.");
        var comparer=OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
        try
        {
            string primary=CanonicalPath(receipt.PrimaryFilePath);
            string folder=filter.OutputDirectory is { } chosen
                ?Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen))
                :Path.GetDirectoryName(primary)!;
            if(filter.OutputFileName is { } name && !comparer.Equals(primary,Path.Combine(folder,name)))
                throw new InvalidDataException("Primary workbook does not match reviewed destination.");
            var paths=new HashSet<string>(comparer);
            var sites=new HashSet<string>(StringComparer.Ordinal);
            var frozen=new List<NativeMonthlyExcelFile>(files.Count);
            for(int i=0;i<files.Count;i++)
            {
                var row=files[i];
                if(row is null || row.SiteId is null || row.SiteId.Length>128 || row.SiteId.Any(char.IsControl) ||
                   (i>0 && string.IsNullOrWhiteSpace(row.SiteId)) ||
                   string.IsNullOrWhiteSpace(row.SiteName) || row.SiteName.Length>512 ||
                   row.SiteName.Any(char.IsControl) || row.PersonCount is <1 or >100000 ||
                   !sites.Add(row.SiteId))
                    throw new InvalidDataException("Invalid or duplicate monthly site receipt.");
                string file=CanonicalPath(row.FilePath);
                if(!comparer.Equals(Path.GetDirectoryName(file),folder) || !paths.Add(file) ||
                   (i==0 && (row.SiteId!=filter.SiteId || !comparer.Equals(file,primary) ||
                             row.PersonCount!=receipt.PersonCount)))
                    throw new InvalidDataException("Invalid or duplicate monthly workbook path.");
                frozen.Add(row);
            }
            return Array.AsReadOnly(frozen.ToArray());
        }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or IOException && ex is not InvalidDataException)
        {
            throw new InvalidDataException("Invalid monthly workbook receipt path.",ex);
        }
    }

    private static string CanonicalPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.StartsWith(@"\\",StringComparison.Ordinal) ||
           path.StartsWith("//",StringComparison.Ordinal) || !Path.IsPathFullyQualified(path) ||
           !path.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid local XLSX receipt path.");
        var full=Path.GetFullPath(path);
        if(!string.Equals(full,path,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
            throw new InvalidDataException("Noncanonical workbook receipt path.");
        return full;
    }
}
