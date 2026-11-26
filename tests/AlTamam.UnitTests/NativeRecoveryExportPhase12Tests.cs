using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeRecoveryExportPhase12Tests
{
    private static readonly NativeIdentity Founder=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly string Destination=Path.Combine(Path.GetTempPath(),"recovery.altrecovery");

    [Fact]
    public void Intent_requires_founder_and_live_same_nonce()
    {
        var intent=new NativeRecoveryExportIntent(Founder,Destination);
        Assert.True(intent.IsValid);
        Assert.True(intent.Matches(Founder));
        Assert.False(intent.Matches(null));
        Assert.False(intent.Matches(Founder with { SessionNonce="NONCE-B" }));
        Assert.False(intent.Matches(Founder with { Username="other" }));
        Assert.False((intent with { Actor=Founder with { Role=UserRole.OrdinaryAdministration } }).IsValid);
        Assert.False((intent with { Actor=Founder with { Role=UserRole.Supervisor } }).IsValid);
    }

    [Theory]
    [InlineData("relative.altrecovery")]
    [InlineData(@"\\server\share\key.altrecovery")]
    [InlineData("//server/share/key.altrecovery")]
    [InlineData("https://example.com/key.altrecovery")]
    public void Rejects_nonlocal_or_relative_destinations(string path)
    {
        Assert.False(new NativeRecoveryExportIntent(Founder,path).IsValid);
    }

    [Fact]
    public void Rejects_wrong_extension_and_noncanonical_path()
    {
        Assert.False(new NativeRecoveryExportIntent(Founder,Path.ChangeExtension(Destination,".db")).IsValid);
        Assert.False(new NativeRecoveryExportIntent(Founder,Path.Combine(Path.GetTempPath(),"..","recovery.altrecovery")).IsValid);
        Assert.False(new NativeRecoveryExportIntent(Founder,Destination+"\n").IsValid);
    }

    [Fact]
    public void Settings_require_founder_and_no_other_review()
    {
        var settings=new SettingsPageViewModel();
        NativeIdentity? actor=Founder;
        Assert.False(settings.CanExportRecovery);
        settings.AttachBackupInspection(new InspectionStub(),()=>actor);
        Assert.True(settings.CanExportRecovery);
        Assert.NotNull(settings.GetRecoveryExportIntent(Destination));
        Assert.True(settings.OpenInspection(Path.ChangeExtension(Destination,".dbbackup")));
        Assert.False(settings.CanExportRecovery);
        Assert.Null(settings.GetRecoveryExportIntent(Destination));
        settings.Inspection.Close();
        actor=Founder with { Role=UserRole.OrdinaryAdministration };
        Assert.False(settings.CanExportRecovery);
        actor=null;
        Assert.False(settings.CanExportRecovery);
    }

    private sealed class InspectionStub:INativeBackupInspectionBackend
    {
        public Task<NativeBackupInspection> InspectBackupAsync(NativeIdentity actor,string path,CancellationToken ct=default)
            =>throw new NotSupportedException();
        public Task<NativeRestoreComparison> PreviewBackupRestoreAsync(NativeIdentity actor,NativeBackupInspection evidence,CancellationToken ct=default)
            =>throw new NotSupportedException();
    }
}
