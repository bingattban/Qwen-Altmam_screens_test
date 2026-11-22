using AlTamam.Application.Scheduling;
using AlTamam.DesktopHost.Scheduling;
using AlTamam.Infrastructure.Persistence;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Scheduling;

/// <summary>
/// Delegates, without modification or replication, to the original
/// EightPmSchedulerService compiled from DesktopHost's exact source file.
/// The host already owns the same single-instance mutex and SQLCipher DB.
/// </summary>
internal sealed class NativeDurableAttendanceScheduler : INativeAutomaticScheduler
{
    private readonly EightPmSchedulerService _original;

    public NativeDurableAttendanceScheduler(AlTamamDbContext database)
    {
        _original = new EightPmSchedulerService(
            new AutoAttendanceService(database),
            tokenRetention: new PreviewTokenRetentionService(database));
    }

    public void Start() => _original.Start();

    public ValueTask StopAsync() => _original.DisposeAsync();
}
