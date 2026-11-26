using AlTamam.Application.Scheduling;
using AlTamam.DesktopHost.Scheduling;
using AlTamam.Infrastructure.Persistence;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Scheduling;

/// <summary>Host-owned adapter over the exact original backup scheduler.
/// Uses the existing recovered SQLCipher database and persisted settings.</summary>
internal sealed class NativeDurableBackupScheduler : INativeAutomaticScheduler
{
    private readonly AutomaticBackupSchedulerService _original;

    public NativeDurableBackupScheduler(AlTamamDbContext database)
        => _original = new AutomaticBackupSchedulerService(
            new AutomaticBackupService(database));

    public void Start() => _original.Start();

    public ValueTask StopAsync() => _original.DisposeAsync();
}
