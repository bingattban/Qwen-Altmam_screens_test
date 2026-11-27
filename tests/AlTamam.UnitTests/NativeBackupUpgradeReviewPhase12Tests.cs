using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeBackupUpgradeReviewPhase12Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly string FilePath=Path.Combine(Path.GetTempPath(),"upgrade-example.dbbackup");
    private static NativeBackupInspection Evidence(int version,bool compatible=false)=>
        new(FilePath,new string('a',64),4096,version,compatible,true,new(2,3,4,1))
        { CurrentSchemaVersion=16 };

    [Theory]
    [InlineData(15,false,true)]
    [InlineData(14,false,false)]
    [InlineData(16,true,false)]
    [InlineData(17,false,false)]
    [InlineData(15,true,false)]
    public async Task OnlyImmediatelyPreviousSchemaCanBePrepared(int version,bool compatible,bool allowed)
    {
        var review=new NativeBackupInspectionReview();
        Assert.True(review.Open(new Probe(Evidence(version,compatible)),Admin,()=>Admin,FilePath));
        Assert.False(review.CanRequestUpgrade);
        await review.InspectAsync();
        Assert.Equal(allowed,review.CanRequestUpgrade);
        Assert.Equal(allowed,review.GetUpgradeIntent() is not null);
        if(allowed)
        {
            Assert.False(review.CanRequestRestore);
            Assert.False(review.CanPreview);
        }
    }

    [Theory]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task NonFoundingRolesCannotPrepare(UserRole role)
    {
        var actor=Admin with { Role=role };
        var review=new NativeBackupInspectionReview();
        Assert.True(review.Open(new Probe(Evidence(15)),actor,()=>actor,FilePath));
        await review.InspectAsync();
        Assert.False(review.CanRequestUpgrade);
    }

    [Fact]
    public async Task ReinspectionAndSessionChangeInvalidateOldIntent()
    {
        NativeIdentity? actor=Admin;
        var review=new NativeBackupInspectionReview();
        var probe=new Probe(Evidence(15));
        Assert.True(review.Open(probe,Admin,()=>actor,FilePath));
        await review.InspectAsync();
        var first=Assert.IsType<NativeBackupUpgradeIntent>(review.GetUpgradeIntent());
        Assert.True(review.MatchesUpgradeIntent(first));
        Assert.False(review.MatchesUpgradeIntent(first with { Evidence=first.Evidence with {} }));
        probe.Result=Evidence(15);
        await review.InspectAsync();
        Assert.False(review.MatchesUpgradeIntent(first));
        var next=Assert.IsType<NativeBackupUpgradeIntent>(review.GetUpgradeIntent());
        actor=Admin with { SessionNonce="NONCE-B" };
        Assert.False(review.MatchesUpgradeIntent(next));
        Assert.Null(review.GetUpgradeIntent());
        review.ForceClear();
        Assert.False(review.CanRequestUpgrade);
    }

    private sealed class Probe(NativeBackupInspection result):INativeBackupInspectionBackend
    {
        public NativeBackupInspection Result=result;
        public Task<NativeBackupInspection> InspectBackupAsync(NativeIdentity actor,string path,CancellationToken ct=default)
            =>Task.FromResult(Result);
        public Task<NativeRestoreComparison> PreviewBackupRestoreAsync(NativeIdentity actor,NativeBackupInspection evidence,CancellationToken ct=default)
            =>throw new InvalidOperationException("Upgrade must not use restore preview.");
    }
}
