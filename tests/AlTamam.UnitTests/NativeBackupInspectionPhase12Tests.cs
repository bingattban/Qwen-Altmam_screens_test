using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeBackupInspectionPhase12Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly string FilePath=Path.Combine(Path.GetTempPath(),"AlTamam-example.dbbackup");
    private static readonly NativeBackupCounts Counts=new(2,3,4,1);
    private static NativeBackupInspection Evidence()=>new(FilePath,new string('a',64),4096,12,true,true,Counts);
    private static NativeRestoreComparison Comparison(NativeBackupInspection evidence)=>
        new(evidence.Path,evidence.Sha256,evidence.SchemaVersion,evidence.Counts,new(5,6,7,2));

    [Fact]
    public async Task Inspection_then_explicit_preview_matches_hash_and_counts()
    {
        var backend=new Probe();
        var review=new NativeBackupInspectionReview();
        Assert.True(review.Open(backend,Admin,()=>Admin,FilePath));
        Assert.Equal(0,backend.Inspections);
        Assert.False(review.CanPreview);
        await review.InspectAsync();
        Assert.True(review.CanPreview);
        Assert.Equal(1,backend.Inspections);
        Assert.Equal(0,backend.Previews);
        await review.PreviewAsync();
        Assert.Equal(1,backend.Previews);
        Assert.Equal(Counts,review.Comparison!.Incoming);
        Assert.Equal(5,review.Comparison.Current.People);
        Assert.True(review.Close());
        Assert.Null(review.Evidence);
    }

    [Fact]
    public async Task Supervisor_can_inspect_but_never_preview()
    {
        var actor=Admin with { Role=UserRole.Supervisor };
        var backend=new Probe();
        var review=new NativeBackupInspectionReview();
        Assert.True(review.Open(backend,actor,()=>actor,FilePath));
        await review.InspectAsync();
        Assert.NotNull(review.Evidence);
        Assert.False(review.CanPreview);
        await review.PreviewAsync();
        Assert.Equal(0,backend.Previews);
    }

    [Fact]
    public async Task Invalid_path_incompatible_schema_and_forged_comparison_fail_closed()
    {
        var backend=new Probe();
        var review=new NativeBackupInspectionReview();
        Assert.False(review.Open(backend,Admin,()=>Admin,"relative.dbbackup"));
        Assert.True(review.Open(backend,Admin,()=>Admin,FilePath));
        backend.Result=Evidence() with { Path=Path.Combine(Path.GetTempPath(),"other.dbbackup") };
        await review.InspectAsync();
        Assert.Null(review.Evidence);
        backend.Result=Evidence() with { Compatible=false };
        await review.InspectAsync();
        Assert.NotNull(review.Evidence);
        Assert.False(review.CanPreview);
        backend.Result=Evidence();
        await review.InspectAsync();
        backend.Forged=true;
        await review.PreviewAsync();
        Assert.Null(review.Comparison);
        Assert.Null(review.Evidence);
        Assert.False(review.CanPreview);
    }

    [Fact]
    public async Task Late_inspection_cannot_repaint_after_session_teardown()
    {
        var backend=new Probe();
        NativeIdentity? actor=Admin;
        var review=new NativeBackupInspectionReview();
        Assert.True(review.Open(backend,Admin,()=>actor,FilePath));
        var pending=new TaskCompletionSource<NativeBackupInspection>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var running=review.InspectAsync();
        actor=null;
        review.ForceClear();
        pending.SetResult(Evidence());
        await running;
        Assert.False(review.IsOpen);
        Assert.Null(review.Evidence);
    }

    private sealed class Probe:INativeBackupInspectionBackend
    {
        public int Inspections,Previews;
        public bool Forged;
        public NativeBackupInspection Result=Evidence();
        public Task<NativeBackupInspection>? Pending;
        public Task<NativeBackupInspection> InspectBackupAsync(NativeIdentity actor,string path,CancellationToken ct=default)
        {
            Inspections++;
            return Pending??Task.FromResult(Result);
        }
        public Task<NativeRestoreComparison> PreviewBackupRestoreAsync(NativeIdentity actor,NativeBackupInspection evidence,CancellationToken ct=default)
        {
            Previews++;
            var comparison=Comparison(evidence);
            return Task.FromResult(Forged?comparison with { Sha256=new string('b',64) }:comparison);
        }
    }
}
