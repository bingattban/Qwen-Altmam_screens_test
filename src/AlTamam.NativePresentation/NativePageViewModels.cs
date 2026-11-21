namespace AlTamam.NativePresentation;

/// <summary>
/// Shell-only placeholders: intentionally no domain mutation, fake personnel rows,
/// database connection, authentication shortcut, or claim of completed migration.
/// Domain operations remain in the existing audited Application/Domain/Infrastructure.
/// </summary>
public abstract class NativePageViewModel(NativeScreen screen, string title)
{
    public NativeScreen Screen { get; } = screen;
    public string Title { get; } = title;
    public bool IsDataConnected => false;
    public string StatusMessage =>
        "لم يتم ربط هذه الواجهة بالمصادقة والخدمات الأصلية بعد. هذه معاينة XAML فقط.";
}

public sealed class DashboardPageViewModel() : NativePageViewModel(NativeScreen.Dashboard, "الرئيسية");
public sealed class DailyAttendancePageViewModel() : NativePageViewModel(NativeScreen.DailyAttendance, "التمام اليومي");
public sealed class MonthlyAttendancePageViewModel() : NativePageViewModel(NativeScreen.MonthlyAttendance, "التمام الشهري");
public sealed class AdministrationPageViewModel() : NativePageViewModel(NativeScreen.Administration, "الإدارة");
public sealed class ReportsPageViewModel() : NativePageViewModel(NativeScreen.Reports, "التقارير");
public sealed class SettingsPageViewModel() : NativePageViewModel(NativeScreen.Settings, "الإعدادات");
