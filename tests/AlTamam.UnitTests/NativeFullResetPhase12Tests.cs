using System.Text.Json.Nodes;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeFullResetPhase12Tests
{
    private static readonly NativeIdentity Founder=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static JsonObject Data()=>new()
    {
        ["previewToken"]=new string('a',64),["expectedRevision"]=12L,
        ["people"]=10L,["attendance"]=100L,["backupEntries"]=1L,
        ["users"]=2L,["movements"]=3L,["assignments"]=10L,["auditEvents"]=4L,
        ["backupFileNames"]=new JsonArray(JsonValue.Create("AlTamam-example-BKP-1.dbbackup")),
        ["backupFileNamesTruncated"]=false,["countdownSeconds"]=5,
        ["requiredConfirmation"]="تصفير جميع البيانات"
    };

    [Fact]
    public void Review_requires_founder_and_complete_canonical_evidence()
    {
        var review=Assert.IsType<NativeFullResetPreview>(NativeFullResetPreview.Read(Founder,Data()));
        Assert.Equal(10L,review.People);
        Assert.Equal(100L,review.Attendance);
        Assert.Single(review.BackupFileNames);
        Assert.True(review.Matches(Founder));
        Assert.False(review.Matches(Founder with { SessionNonce="NONCE-B" }));
        Assert.Null(NativeFullResetPreview.Read(Founder with { Role=UserRole.OrdinaryAdministration },Data()));
        Assert.Null(NativeFullResetPreview.Read(Founder with { Role=UserRole.Supervisor },Data()));
    }

    [Theory]
    [InlineData("previewToken")]
    [InlineData("expectedRevision")]
    [InlineData("backupEntries")]
    [InlineData("backupFileNamesTruncated")]
    [InlineData("requiredConfirmation")]
    public void Missing_required_evidence_is_denied(string field)
    {
        var data=Data(); data.Remove(field);
        Assert.Null(NativeFullResetPreview.Read(Founder,data));
    }

    [Fact]
    public void Rejects_forged_revision_countdown_counts_and_path()
    {
        var data=Data(); data["expectedRevision"]=9007199254740992L;
        Assert.Null(NativeFullResetPreview.Read(Founder,data));
        data=Data(); data["countdownSeconds"]=0;
        Assert.Null(NativeFullResetPreview.Read(Founder,data));
        data=Data(); data["attendance"]=-1L;
        Assert.Null(NativeFullResetPreview.Read(Founder,data));
        data=Data(); data["backupFileNames"]=new JsonArray(JsonValue.Create("../outside.dbbackup"));
        Assert.Null(NativeFullResetPreview.Read(Founder,data));
    }

    [Fact]
    public void Confirmation_requires_exact_phrase_both_consents_five_seconds_and_same_session()
    {
        var review=Assert.IsType<NativeFullResetPreview>(NativeFullResetPreview.Read(Founder,Data()));
        var consent=new NativeFullResetConsent("تصفير جميع البيانات",true,true);
        Assert.True(review.CanCommit(Founder,consent,TimeSpan.FromSeconds(5)));
        Assert.False(review.CanCommit(Founder,consent,TimeSpan.FromMilliseconds(4999)));
        Assert.False(review.CanCommit(Founder,consent with { DeleteLiveData=false },TimeSpan.FromSeconds(6)));
        Assert.False(review.CanCommit(Founder,consent with { DeleteOwnedBackups=false },TimeSpan.FromSeconds(6)));
        Assert.False(review.CanCommit(Founder,consent with { Phrase="تصفير جميع البيانات " },TimeSpan.FromSeconds(6)));
        Assert.False(review.CanCommit(null,consent,TimeSpan.FromSeconds(6)));
        Assert.False(review.CanCommit(Founder with { SessionNonce="NONCE-B" },consent,TimeSpan.FromSeconds(6)));
    }
}
