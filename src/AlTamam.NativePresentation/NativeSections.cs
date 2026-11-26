namespace AlTamam.NativePresentation;

public enum NativeScreen
{
    Dashboard,
    DailyAttendance,
    MonthlyAttendance,
    Administration,
    Reports,
    Settings,
    AuditLog,
    Accounts
}

public sealed record NativeSectionDescriptor(NativeScreen Screen, string Title);

public static class NativeSections
{
    public static IReadOnlyList<NativeSectionDescriptor> All { get; } =
        Array.AsReadOnly(new[]
        {
            new NativeSectionDescriptor(NativeScreen.Dashboard, "الرئيسية"),
            new NativeSectionDescriptor(NativeScreen.DailyAttendance, "التمام اليومي"),
            new NativeSectionDescriptor(NativeScreen.MonthlyAttendance, "التمام الشهري"),
            new NativeSectionDescriptor(NativeScreen.Administration, "الإدارة"),
            new NativeSectionDescriptor(NativeScreen.Reports, "التقارير"),
            new NativeSectionDescriptor(NativeScreen.AuditLog, "سجل العمليات"),
            new NativeSectionDescriptor(NativeScreen.Accounts, "حسابات المستخدمين"),
            new NativeSectionDescriptor(NativeScreen.Settings, "الإعدادات")
        });
}
