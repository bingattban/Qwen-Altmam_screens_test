using AlTamam.Application.Scheduling;
using AlTamam.DesktopHost.Scheduling;
using AlTamam.Infrastructure.Persistence;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Scheduling;

/// <summary>Host-owned adapter over the exact original opt-in monthly Excel scheduler.</summary>
internal sealed class NativeDurableMonthlyExcelScheduler : INativeAutomaticScheduler
{
    private readonly AutomaticMonthlyExcelSchedulerService _original;

    public NativeDurableMonthlyExcelScheduler(AlTamamDbContext database)
        => _original = new AutomaticMonthlyExcelSchedulerService(new AutomaticMonthlyExcelService(database));

    public void Start() => _original.Start();

    public ValueTask StopAsync() => _original.DisposeAsync();
}
