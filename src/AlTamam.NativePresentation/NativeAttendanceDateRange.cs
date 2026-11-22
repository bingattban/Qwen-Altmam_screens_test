using System.Globalization;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native input boundary for bounded same-day attendance revisions.
/// Mirrors the canonical AttendanceDateRangePolicy contract; it does not
/// authorize writes or replace the server-side SQLCipher validation.
/// </summary>
public sealed record NativeAttendanceDateRange(string StartDate, string EndDate)
{
    public static bool RequiresRange(string? statusId) =>
        statusId is "STS-LEAVE" or "STS-PERMISSION";

    public static bool TryCreate(string? statusId, DateOnly businessDate,
        string? startDate, string? endDate,
        out NativeAttendanceDateRange? range, out string error)
    {
        range = null;
        error = "";
        string start = startDate?.Trim() ?? "";
        string end = endDate?.Trim() ?? "";
        if (!RequiresRange(statusId))
        {
            if (start.Length != 0 || end.Length != 0)
            {
                error = "لا تقبل الحالة المختارة مدة زمنية.";
                return false;
            }
            return true;
        }

        if (!DateOnly.TryParseExact(start, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(end, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            error = "حدد تاريخ البداية والنهاية بصيغة صحيحة.";
            return false;
        }
        if (to < from)
        {
            error = "تاريخ نهاية المدة يسبق تاريخ بدايتها.";
            return false;
        }
        if (from != businessDate)
        {
            error = "يجب أن تبدأ مدة الإجازة أو الإذن من تاريخ التمام الحالي.";
            return false;
        }
        if (to.DayNumber - from.DayNumber > 365)
        {
            error = "لا يمكن تحديد مدة تتجاوز 366 يوما.";
            return false;
        }
        range = new(start, end);
        return true;
    }
}
