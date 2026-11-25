using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeHistoricalSettingsPhase10Tests
{
    private static readonly NativeIdentity Admin = new("USR-1","admin",UserRole.GeneralAdministration,"HISTORY-NONCE");
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Now).AddDays(-2);
    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);

    private static async Task Load(SettingsPageViewModel page)
    {
        page.OpenHistoricalCommand.Execute(null);
        Assert.True(page.Historical.IsOpen);
        page.Historical.SearchText="123";
        await page.Historical.SearchAsync();
        page.Historical.SelectedPerson=Assert.Single(page.Historical.Candidates);
        page.Historical.FromText=Iso(Day);
        page.Historical.ToText=Iso(Day.AddDays(2));
        await page.Historical.ReadAsync();
        Assert.NotNull(page.Historical.Evidence);
    }

    [Fact]
    public async Task Settings_historical_range_freezes_evidence_and_refreshes_after_verified_receipt()
    {
        var fake=new Fake();
        NativeIdentity? actor=Admin with { Role=UserRole.Supervisor };
        var page=new SettingsPageViewModel();
        page.AttachHistorical(fake,fake,fake,()=>actor);
        Assert.False(page.CanOpenHistorical);
        actor=Admin;
        page.NotifySessionChanged();
        await Load(page);
        Assert.True(page.IsReviewOpen);
        Assert.False(page.CanOpenCatchUp);
        Assert.False(page.CanOpenPolicy);
        await page.OpenHistoricalRangeAsync();
        Assert.True(page.HistoricalRange.IsOpen);
        Assert.False(page.Historical.CanEdit);
        Assert.False(page.Historical.Close());
        var evidence=page.Historical.Evidence;
        page.Historical.SearchText="other";
        page.Historical.FromText="1900-01-01";
        await page.Historical.ReadAsync();
        Assert.Same(evidence,page.Historical.Evidence);
        Assert.Equal(1,fake.Reads);
        page.HistoricalRange.SelectedStatusId="STS-ABSENT";
        page.HistoricalRange.Reason="تصحيح معتمد للسجل التاريخي";
        await page.HistoricalRange.ConfirmAsync();
        Assert.Empty(fake.Commits);
        await page.HistoricalRange.PreviewAsync();
        await page.HistoricalRange.ConfirmAsync();
        Assert.NotNull(page.HistoricalRange.Receipt);
        Assert.Single(fake.Commits);
        await page.CloseHistoricalRangeAsync();
        Assert.False(page.HistoricalRange.IsOpen);
        Assert.Equal(2,fake.Reads);
        Assert.NotSame(evidence,page.Historical.Evidence);
        Assert.True(page.Historical.Close());
        Assert.False(page.IsReviewOpen);
    }

    [Fact]
    public async Task Settings_historical_uncertain_commit_blocks_close_and_logout_clears_reviews()
    {
        var fake=new Fake { Lose=true };
        NativeIdentity? actor=Admin;
        var page=new SettingsPageViewModel();
        page.AttachHistorical(fake,fake,fake,()=>actor);
        await Load(page);
        await page.OpenHistoricalRangeAsync();
        page.HistoricalRange.SelectedStatusId="STS-ABSENT";
        page.HistoricalRange.Reason="تصحيح معتمد للسجل التاريخي";
        await page.HistoricalRange.PreviewAsync();
        await page.HistoricalRange.ConfirmAsync();
        Assert.True(page.HistoricalRange.HasUnverifiedCommit);
        await page.CloseHistoricalRangeAsync();
        Assert.True(page.HistoricalRange.IsOpen);
        Assert.False(page.Historical.Close());
        Assert.Equal(1,fake.Reads);
        actor=null;
        page.Clear();
        Assert.False(page.IsReviewOpen);
        Assert.Null(page.Historical.Evidence);
        Assert.Null(page.HistoricalRange.Preview);
        Assert.False(page.CanOpenHistorical);
    }

    [Fact]
    public async Task Settings_historical_stale_preview_requires_reread_without_commit()
    {
        var fake=new Fake { Stale=true };
        var page=new SettingsPageViewModel();
        page.AttachHistorical(fake,fake,fake,()=>Admin);
        await Load(page);
        await page.OpenHistoricalRangeAsync();
        page.HistoricalRange.SelectedStatusId="STS-ABSENT";
        page.HistoricalRange.Reason="تصحيح معتمد للسجل التاريخي";
        await page.HistoricalRange.PreviewAsync();
        Assert.True(page.HistoricalRange.NeedsPeriodReload);
        Assert.False(page.HistoricalRange.ConfirmCommand.CanExecute(null));
        await page.HistoricalRange.ConfirmAsync();
        Assert.Empty(fake.Commits);
        fake.Stale=false;
        await page.CloseHistoricalRangeAsync();
        Assert.False(page.HistoricalRange.IsOpen);
        Assert.Equal(2,fake.Reads);
        Assert.NotNull(page.Historical.Evidence);
        Assert.True(page.CanOpenHistoricalRange);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_direct_single_closed_day_uses_single_service_and_reloads_on_stale_or_receipt(bool stale)
    {
        var fake=new Fake { Stale=stale };
        var page=new SettingsPageViewModel();
        page.AttachHistorical(fake,fake,fake,()=>Admin);
        await Load(page);
        Assert.False(page.CanOpenHistoricalSingle);
        page.Historical.ToText=Iso(Day);
        await page.Historical.ReadAsync();
        Assert.True(page.CanOpenHistoricalSingle);
        await page.OpenHistoricalSingleAsync();
        Assert.True(page.HistoricalSingle.IsOpen);
        Assert.False(page.Historical.CanEdit);
        Assert.False(page.CanOpenHistoricalRange);
        Assert.False(page.Historical.Close());
        page.HistoricalSingle.SelectedStatusId="STS-ABSENT";
        page.HistoricalSingle.Reason="تصحيح معتمد للسجل التاريخي";
        await page.HistoricalSingle.ConfirmAsync();
        Assert.Empty(fake.SingleCommits);
        await page.HistoricalSingle.PreviewSelectedAsync();
        if (stale)
        {
            Assert.True(page.HistoricalSingle.NeedsPeriodReload);
            Assert.Null(page.HistoricalSingle.Preview);
        }
        else
        {
            Assert.NotNull(page.HistoricalSingle.Preview);
            await page.HistoricalSingle.ConfirmAsync();
            Assert.NotNull(page.HistoricalSingle.Receipt);
        }
        Assert.Equal(stale?0:1,fake.SingleCommits.Count);
        Assert.Empty(fake.Commits);
        fake.Stale=false;
        await page.CloseHistoricalSingleAsync();
        Assert.Equal(3,fake.Reads);
        Assert.False(page.HistoricalSingle.IsOpen);
        Assert.True(page.CanOpenHistoricalSingle);
        page.Clear();
        Assert.False(page.IsReviewOpen);
        Assert.Null(page.Historical.Evidence);
    }

    [Fact]
    public void Direct_single_day_requires_one_saved_past_date()
    {
        var today=DateOnly.FromDateTime(DateTime.Now);
        var request=new NativeHistoricalPeriodRequest("P-1",Day,Day);
        var evidence=new NativeHistoricalPeriodEvidence(request,"أحمد","123",
            [new(Day,true,"موجود","المركز",7)]);
        Assert.True(NativeClosedDayCorrectionReview.CanUsePeriod(evidence));
        Assert.False(NativeClosedDayCorrectionReview.CanUsePeriod(evidence with
            { Days=[new(Day,false,"لا يوجد سجل تحضير","",0)] }));
        Assert.False(NativeClosedDayCorrectionReview.CanUsePeriod(evidence with
            { Request=new("P-1",today,today), Days=[new(today,true,"موجود","المركز",7)] }));
        Assert.False(NativeClosedDayCorrectionReview.CanUsePeriod(evidence with
            { Request=request with { To=Day.AddDays(1) } }));
    }

    private sealed class Fake : INativeHistoricalPeriodReader, INativeHistoricalRangeBackend, INativeClosedDayCorrectionBackend
    {
        public int Reads;
        public bool Lose,Stale;
        public List<string> Commits=[];
        public List<string> SingleCommits=[];
        public Task<IReadOnlyList<NativeHistoricalPersonCandidate>> SearchHistoricalPeopleAsync(NativeIdentity actor,string term,CancellationToken ct=default)
            => Task.FromResult<IReadOnlyList<NativeHistoricalPersonCandidate>>([new("P-1","أحمد","123","المركز",3)]);
        public Task<NativeHistoricalPeriodEvidence> ReadHistoricalPeriodAsync(NativeIdentity actor,NativeHistoricalPeriodRequest request,CancellationToken ct=default)
        {
            Reads++;
            return Task.FromResult(new NativeHistoricalPeriodEvidence(request,"أحمد","123",
                Enumerable.Range(0,request.SpanDays).Select(i=>i==request.SpanDays-1
                    ? new NativeHistoricalPeriodDay(request.From.AddDays(i),true,"موجود","الموقع الثاني",Stale?6:7)
                    : new NativeHistoricalPeriodDay(request.From.AddDays(i),false,"لا يوجد سجل تحضير","",0)).ToArray()));
        }
        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadCorrectionChoicesAsync(NativeIdentity actor,CancellationToken ct=default)
            => Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>([new("STS-ABSENT","غياب")]);
        public Task<NativeHistoricalRangePreview> PreviewRangeAsync(NativeIdentity actor,NativeHistoricalRangeRequest request,CancellationToken ct=default)
            => Task.FromResult(new NativeHistoricalRangePreview(request,new string('a',64),"أحمد","123",
                [new(request.From,false,0,"A-1","SITE-1","المركز","__UNPREPARED__","غير محضر","غياب","غ","#FF0000"),
                 new(request.To,true,7,"A-2","SITE-2","الموقع الثاني","STS-PRESENT","موجود","غياب","غ","#FF0000")],true));
        public Task<NativeHistoricalRangeReceipt> CommitRangeAsync(NativeIdentity actor,NativeHistoricalRangePreview approved,string operationId,CancellationToken ct=default)
        {
            Commits.Add(operationId);
            if (Lose) throw new IOException("Lost reply");
            return Task.FromResult(new NativeHistoricalRangeReceipt(approved.Request.PersonId,approved.Request.From,8));
        }
        public Task<NativeClosedDayCorrectionPreview> PreviewAsync(NativeIdentity actor,NativeClosedDayCorrectionRequest request,CancellationToken ct=default)
            => Task.FromResult(new NativeClosedDayCorrectionPreview(request,new string('b',64),7,"أحمد","123","الموقع الثاني",
                "STS-PRESENT","موجود","غياب",true));
        public Task<NativeClosedDayCorrectionReceipt> CommitAsync(NativeIdentity actor,string token,string operationId,CancellationToken ct=default)
        {
            SingleCommits.Add(operationId);
            return Task.FromResult(new NativeClosedDayCorrectionReceipt("P-1",Day,8));
        }
    }
}
