namespace AlTamam.Domain.Policies;

/// <summary>One filename policy for canonical monthly writer and native review.</summary>
public static class MonthlyExcelOutputNamePolicy
{
    public static bool TryNormalize(string? input,out string name)
    {
        name="";
        if(input is null)return true;
        string proposed=input.Trim();
        if(proposed.Length is <5 or >124 ||
           !proposed.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase))return false;
        string stem=proposed[..^5];
        if(stem.Length is <1 or >119 || stem!=stem.Trim() ||
           !stem.All(c=>char.IsLetterOrDigit(c)||c is ' ' or '-' or '_' or '(' or ')'))
            return false;
        // Windows device basenames remain reserved even with an .xlsx extension.
        // Validate uniformly on Windows and non-Windows test runners, before any
        // .part file or output directory is created. COM1..COM9 and LPT1..LPT9
        // are all reserved; the historical shorter list missed COM4 and LPT9.
        if(stem.Equals("CON",StringComparison.OrdinalIgnoreCase) ||
           stem.Equals("PRN",StringComparison.OrdinalIgnoreCase) ||
           stem.Equals("AUX",StringComparison.OrdinalIgnoreCase) ||
           stem.Equals("NUL",StringComparison.OrdinalIgnoreCase) ||
           (stem.Length==4 && (stem.StartsWith("COM",StringComparison.OrdinalIgnoreCase) ||
                               stem.StartsWith("LPT",StringComparison.OrdinalIgnoreCase)) &&
            stem[3] is >= '1' and <= '9'))return false;
        name=stem+".xlsx";
        return true;
    }
}
