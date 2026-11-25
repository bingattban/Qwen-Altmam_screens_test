using System.Globalization;
using System.IO;

namespace AlTamam.NativePresentation;

public sealed record NativeHistoricalPersonCandidate(string PersonId,string Name,string ServiceNumberA,
    string SiteName,int OrganizationalStatus)
{
    public bool IsValid => NativeHistoricalRangeRequest.IsIdentifier(PersonId) &&
        !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(ServiceNumberA) &&
        !string.IsNullOrWhiteSpace(SiteName) && OrganizationalStatus is >= 1 and <= 4;

    public static bool IsValidTerm(string? term) => term is { Length: >= 1 and <= 100 } &&
        !string.IsNullOrWhiteSpace(term) && term.Trim() == term && !term.Any(char.IsControl);
}

public sealed record NativeHistoricalPeriodRequest(string PersonId,DateOnly From,DateOnly To)
{
    public int SpanDays => To.DayNumber - From.DayNumber + 1;
    public bool IsValid(DateOnly today) => NativeHistoricalRangeRequest.IsIdentifier(PersonId) &&
        SpanDays is >= 1 and <= 62 && To <= today;
}

public sealed record NativeHistoricalPeriodDay(DateOnly BusinessDate,bool HasSavedRecord,
    string StatusName,string SiteName,long Revision);

/// <summary>Read-only evidence of saved records; absence never implies correction eligibility.</summary>
public sealed record NativeHistoricalPeriodEvidence(NativeHistoricalPeriodRequest Request,string PersonName,
    string ServiceNumberA,IReadOnlyList<NativeHistoricalPeriodDay> Days)
{
    private const string Missing = "لا يوجد سجل تحضير";
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    public bool Matches(NativeHistoricalPeriodRequest expected,DateOnly today)
    {
        if (Request != expected || !expected.IsValid(today) || string.IsNullOrWhiteSpace(PersonName) ||
            string.IsNullOrWhiteSpace(ServiceNumberA) || Days is null || Days.Count != expected.SpanDays)
            return false;
        for (int i=0;i<Days.Count;i++)
        {
            var day=Days[i];
            if (day is null || day.BusinessDate != expected.From.AddDays(i)) return false;
            if (day.HasSavedRecord)
            {
                if (day.Revision <= 0 || string.IsNullOrWhiteSpace(day.StatusName) ||
                    string.IsNullOrWhiteSpace(day.SiteName)) return false;
            }
            else if (day.Revision != 0 || day.StatusName != Missing || day.SiteName != "") return false;
        }
        return true;
    }

    // Compare only actual proposed changes; omitted days are not evidence of
    // eligibility, and a missing record may still be canonically preparable.
    public bool MatchesPreviewState(NativeHistoricalRangePreview preview,DateOnly today)
    {
        var request=preview.Request;
        if (!Matches(Request,today) || request.PersonId!=Request.PersonId ||
            request.From!=Request.From || request.To!=Request.To ||
            !preview.Matches(request,today)) return false;
        foreach (var proposed in preview.Days)
        {
            var saved=Days[proposed.BusinessDate.DayNumber-Request.From.DayNumber];
            if (saved.HasSavedRecord!=proposed.HasExistingRecord ||
                saved.Revision!=proposed.PreviousRevision) return false;
        }
        return true;
    }

    public static NativeHistoricalPeriodEvidence FromReport(NativeHistoricalPeriodRequest request,
        NativePersonnelReportSnapshot report,DateOnly today)
    {
        if (!request.IsValid(today) || report.PersonId != request.PersonId ||
            report.DateFrom != Iso(request.From) || report.DateTo != Iso(request.To) ||
            report.MovementType != 0 || report.SiteId != "" || report.StatusId != "" ||
            report.AttendancePage != 1 || report.PageSize != 100 || report.AttendanceHasNext ||
            report.Attendance is null || report.AttendanceTotal != report.Attendance.Count ||
            report.Attendance.Count > request.SpanDays)
            throw new InvalidDataException("Incomplete or mismatched canonical historical period.");
        var saved=new Dictionary<DateOnly,NativeHistoricalPeriodDay>();
        foreach (var row in report.Attendance)
        {
            if (!DateOnly.TryParseExact(row.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) ||
                date < request.From || date > request.To ||
                !saved.TryAdd(date,new(date,true,row.StatusName,row.SiteName,row.Revision)))
                throw new InvalidDataException("Invalid or duplicate canonical attendance date.");
        }
        var days=Enumerable.Range(0,request.SpanDays).Select(offset =>
        {
            var date=request.From.AddDays(offset);
            return saved.TryGetValue(date,out var day) ? day : new(date,false,Missing,"",0);
        }).ToArray();
        var result=new NativeHistoricalPeriodEvidence(request,report.Name,report.ServiceNumberA,Array.AsReadOnly(days));
        if (!result.Matches(request,today)) throw new InvalidDataException("Invalid canonical period evidence.");
        return result;
    }
}

public interface INativeHistoricalPeriodReader
{
    Task<IReadOnlyList<NativeHistoricalPersonCandidate>> SearchHistoricalPeopleAsync(NativeIdentity actor,
        string term,CancellationToken ct=default);
    Task<NativeHistoricalPeriodEvidence> ReadHistoricalPeriodAsync(NativeIdentity actor,
        NativeHistoricalPeriodRequest request,CancellationToken ct=default);
}
