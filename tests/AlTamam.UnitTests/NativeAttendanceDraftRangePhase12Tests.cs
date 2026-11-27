using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceDraftRangePhase12Tests
{
    private static readonly DateOnly Day=new(2026,11,27);
    private static NativeAttendanceDraft Draft(string status="STS-LEAVE")=>new(
        "DRF-"+new string('a',32),Day,"SITE-A",status,DateTimeOffset.UnixEpoch,["P-1","P-2"]);

    [Theory]
    [InlineData("STS-LEAVE",true)]
    [InlineData("STS-LEAVE",false)]
    [InlineData("STS-PERMISSION",true)]
    [InlineData("STS-PERMISSION",false)]
    public void Complete_range_preserves_exact_dates_and_explicit_mode(string status,bool initial)
    {
        var evidence=new NativeAttendanceDraftRangeEvidence("2026-11-27","2026-12-03",initial);
        var draft=Draft(status) with {RangeEvidence=evidence};
        Assert.True(draft.IsValid);
        Assert.True(draft.HasCompleteRangeEvidence);
        Assert.True(evidence.IsValid(status,Day));
        Assert.False(evidence.Matches(evidence with {IsInitial=!initial}));
        Assert.False(evidence.Matches(evidence with {EndDate="2026-12-04"}));
        Assert.True(evidence.Matches(evidence with {}));
    }

    [Theory]
    [InlineData("2026-11-26","2026-12-03")]
    [InlineData("2026-11-27","2026-11-26")]
    [InlineData("2026-11-27","2027-11-28")]
    [InlineData("2026-11-27","")]
    [InlineData(" 2026-11-27","2026-12-03")]
    [InlineData("2026-11-27","2026-02-30")]
    public void Invalid_or_noncanonical_range_is_rejected(string start,string end)
    {
        var evidence=new NativeAttendanceDraftRangeEvidence(start,end,true);
        Assert.False(evidence.IsValid("STS-LEAVE",Day));
        Assert.False((Draft() with {RangeEvidence=evidence}).IsValid);
    }

    [Fact]
    public void Legacy_incomplete_range_remains_visible_but_not_restorable()
    {
        var legacy=Draft();
        Assert.True(legacy.IsValid);
        Assert.False(legacy.HasCompleteRangeEvidence);
        Assert.True(NativeAttendanceDraft.ValidCatalogue([legacy]));
        Assert.False(legacy.SameSavedEvidence(legacy with {
            RangeEvidence=new("2026-11-27","2026-11-28",true)}));
    }

    [Fact]
    public void Range_evidence_cannot_be_attached_to_an_ordinary_status()
    {
        var evidence=new NativeAttendanceDraftRangeEvidence("2026-11-27","2026-11-27",false);
        Assert.False(evidence.IsValid("STS-PRESENT",Day));
        Assert.False((Draft("STS-PRESENT") with {RangeEvidence=evidence}).IsValid);
    }

    [Fact]
    public void Exact_saved_evidence_includes_range_and_mode()
    {
        var row=Draft() with {RangeEvidence=new("2026-11-27","2026-12-03",true)};
        Assert.True(row.SameSavedEvidence(row with {PersonIds=["P-2","P-1"]}));
        Assert.False(row.SameSavedEvidence(row with {RangeEvidence=row.RangeEvidence! with {IsInitial=false}}));
        Assert.False(row.SameSavedEvidence(row with {RangeEvidence=row.RangeEvidence! with {EndDate="2026-12-04"}}));
        Assert.False(row.SameSavedEvidence(row with {RangeEvidence=null}));
    }
}
