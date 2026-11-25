using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeHistoricalPeriodEvidencePhase10Tests
{
    private static readonly DateOnly Today = new(2026,11,26);
    private static readonly NativeHistoricalPeriodRequest Request = new("P-1",Today.AddDays(-3),Today.AddDays(-1));
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);

    private static NativePersonnelReportSnapshot Report => new("P-1","أحمد","123","2026-01-01",false,
        2,0,0,100,1,false,1,false,1,false,[],
        [new(Iso(Request.To),"غياب","المركز","manual","اسم سابق","100",2),
         new(Iso(Request.From),"موجود","المركز","manual","اسم سابق","100",1)],[],[])
        { DateFrom=Iso(Request.From),DateTo=Iso(Request.To) };

    [Fact]
    public void Historical_person_search_rejects_empty_control_and_overlong_queries()
    {
        Assert.True(NativeHistoricalPersonCandidate.IsValidTerm("أحمد"));
        Assert.True(NativeHistoricalPersonCandidate.IsValidTerm("123"));
        Assert.False(NativeHistoricalPersonCandidate.IsValidTerm(""));
        Assert.False(NativeHistoricalPersonCandidate.IsValidTerm(" أحمد"));
        Assert.False(NativeHistoricalPersonCandidate.IsValidTerm("أحمد\n123"));
        Assert.False(NativeHistoricalPersonCandidate.IsValidTerm(new string('a',101)));
        var candidate=new NativeHistoricalPersonCandidate("P-1","أحمد","123","المركز",3);
        Assert.True(candidate.IsValid);
        Assert.False((candidate with { PersonId="" }).IsValid);
        Assert.False((candidate with { OrganizationalStatus=99 }).IsValid);
    }

    [Fact]
    public void Historical_period_requires_inclusive_bounded_past_or_today()
    {
        Assert.True(Request.IsValid(Today));
        Assert.True((Request with { From=Today.AddDays(-61),To=Today }).IsValid(Today));
        Assert.False((Request with { From=Today.AddDays(-62),To=Today }).IsValid(Today));
        Assert.False((Request with { To=Today.AddDays(1) }).IsValid(Today));
        Assert.False((Request with { From=Today,To=Today.AddDays(-1) }).IsValid(Today));
    }

    [Fact]
    public void Historical_period_reads_saved_days_and_never_infers_missing_day_eligibility()
    {
        var evidence=NativeHistoricalPeriodEvidence.FromReport(Request,Report,Today);
        Assert.True(evidence.Matches(Request,Today));
        Assert.Equal(3,evidence.Days.Count);
        Assert.Equal("موجود",evidence.Days[0].StatusName);
        Assert.True(evidence.Days[0].HasSavedRecord);
        Assert.False(evidence.Days[1].HasSavedRecord);
        Assert.Equal("لا يوجد سجل تحضير",evidence.Days[1].StatusName);
        Assert.Equal(0,evidence.Days[1].Revision);
        Assert.Equal("غياب",evidence.Days[2].StatusName);
        Assert.Equal(2,evidence.Days[2].Revision);
    }

    [Fact]
    public void Historical_period_rejects_wrong_identity_scope_partial_and_duplicate_report()
    {
        var report=Report;
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { PersonId="OTHER" },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { DateFrom=Iso(Today) },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { AttendanceHasNext=true },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { AttendanceTotal=3 },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { Attendance=[report.Attendance[0],report.Attendance[0]] },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { StatusId="STS-1" },Today));
        Assert.Throws<System.IO.InvalidDataException>(()=>NativeHistoricalPeriodEvidence.FromReport(Request,report with { Attendance=[report.Attendance[0] with { Date=Iso(Today) },report.Attendance[1]] },Today));
    }
}
