using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeManualBackupPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly string Folder=Path.Combine(Path.GetTempPath(),"AlTamamBackup");
    private static readonly NativeSettingsOverview Snapshot=new(Actor.Username,Actor.Role,Folder,true,"16:00",false,0,null,"","light");
    private static readonly string BackupId="BKP-"+new string('a',32);
    private static NativeManualBackupReceipt Receipt(string folder)=>new(BackupId,
        Path.Combine(folder,"AlTamam-20261126-160000-"+BackupId+".dbbackup"),new string('b',64),8192,true);

    [Fact]
    public void Receipt_requires_exact_folder_identity_hash_and_verified_catalog()
    {
        var result=Receipt(Folder);
        Assert.True(result.Matches(Folder));
        Assert.False(result.Matches(Path.Combine(Folder,"other")));
        Assert.False((result with { Sha256="bad" }).Matches(Folder));
        Assert.False((result with { Verified=false }).Matches(Folder));
        Assert.False((result with { FileSize=0 }).Matches(Folder));
        Assert.False((result with { Path=Path.Combine(Folder,"other.dbbackup") }).Matches(Folder));
        Assert.False((result with { BackupId="BKP-invalid" }).Matches(Folder));
    }

    [Fact]
    public async Task Backup_requires_live_snapshot_and_explicit_confirmation_and_blocks_duplicate_calls()
    {
        var backend=new Backend();
        var review=new NativeManualBackupReview();
        Assert.False(review.Open(backend,Actor,Snapshot with { },()=>Actor,()=>Snapshot));
        Assert.True(review.Open(backend,Actor,Snapshot,()=>Actor,()=>Snapshot));
        Assert.Equal(0,backend.Calls);
        var pending=new TaskCompletionSource<NativeManualBackupReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=review.ConfirmAsync();
        Assert.True(review.IsBusy);
        Assert.False(review.Close());
        await review.ConfirmAsync();
        Assert.Equal(1,backend.Calls);
        pending.SetResult(Receipt(Folder));
        await task;
        Assert.NotNull(review.Receipt);
        Assert.False(review.CanConfirm);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Folder_change_is_explicit_and_available_when_old_folder_is_missing()
    {
        var backend=new Backend();
        var review=new NativeManualBackupReview();
        var unavailable=Snapshot with { DirectoryAvailable=false };
        Assert.False(review.Open(backend,Actor,unavailable,()=>Actor,()=>unavailable));
        var target=Path.Combine(Folder,"new");
        Assert.True(review.Open(backend,Actor,unavailable,()=>Actor,()=>unavailable,target));
        Assert.True(review.IsFolderChange);
        Assert.Equal(0,backend.Calls);
        await review.ConfirmAsync();
        Assert.Equal(target,review.SavedDirectory);
        Assert.Equal(1,backend.Calls);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Ambiguous_backup_cannot_be_repeated_or_reported_as_success()
    {
        var backend=new Backend { Fail=true };
        var review=new NativeManualBackupReview();
        Assert.True(review.Open(backend,Actor,Snapshot,()=>Actor,()=>Snapshot));
        await review.ConfirmAsync();
        Assert.True(review.HasUnverifiedResult);
        Assert.Null(review.Receipt);
        Assert.False(review.CanConfirm);
        Assert.False(review.Close());
        await review.ConfirmAsync();
        Assert.Equal(1,backend.Calls);
        review.ForceClear();
        Assert.False(review.IsOpen);
    }

    [Fact]
    public async Task Rejected_or_stale_destination_requires_refresh_without_automatic_retry()
    {
        var backend=new Backend { Reject=true };
        var review=new NativeManualBackupReview();
        Assert.True(review.Open(backend,Actor,Snapshot,()=>Actor,()=>Snapshot));
        await review.ConfirmAsync();
        Assert.True(review.NeedsReload);
        Assert.False(review.CanConfirm);
        Assert.True(review.Close());
        Assert.Equal(1,backend.Calls);
    }

    [Fact]
    public async Task Late_backup_receipt_is_discarded_after_session_change()
    {
        var backend=new Backend();
        var review=new NativeManualBackupReview();
        NativeIdentity? actor=Actor;
        Assert.True(review.Open(backend,Actor,Snapshot,()=>actor,()=>Snapshot));
        var pending=new TaskCompletionSource<NativeManualBackupReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=review.ConfirmAsync();
        actor=null;
        review.ForceClear();
        pending.SetResult(Receipt(Folder));
        await task;
        Assert.Null(review.Receipt);
        Assert.False(review.IsOpen);
    }

    private sealed class Backend:INativeManualBackupBackend
    {
        public int Calls;
        public bool Fail,Reject;
        public Task<NativeManualBackupReceipt>? Pending;
        public Task<NativeManualBackupReceipt> CreateManualBackupAsync(NativeIdentity actor,string directory,CancellationToken ct=default)
        {
            Calls++;
            if(Fail) throw new IOException("response lost");
            if(Reject) throw new NativeBackupRejectedException();
            return Pending??Task.FromResult(Receipt(directory));
        }
        public Task<string> SaveBackupDirectoryAsync(NativeIdentity actor,string directory,CancellationToken ct=default)
        {
            Calls++; return Task.FromResult(directory);
        }
    }
}
