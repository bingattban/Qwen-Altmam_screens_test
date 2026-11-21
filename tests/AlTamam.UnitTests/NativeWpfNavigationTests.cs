using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeWpfNavigationTests
{
    [Fact]
    public void Main_navigation_includes_all_requested_native_sections()
    {
        Assert.Equal(6, NativeSections.All.Count);
        Assert.Equal(
            new[] { "الرئيسية", "التمام اليومي", "التمام الشهري", "الإدارة", "التقارير", "الإعدادات" },
            NativeSections.All.Select(section => section.Title).ToArray());
        Assert.Equal(6, NativeSections.All.Select(section => section.Screen).Distinct().Count());
    }

    [Fact]
    public void Navigation_changes_selected_page_without_touching_application_data()
    {
        var vm = new NativeShellViewModel();
        Assert.Equal(NativeScreen.Dashboard, vm.CurrentPage.Screen);
        vm.NavigateCommand.Execute(NativeScreen.MonthlyAttendance);
        Assert.Equal(NativeScreen.MonthlyAttendance, vm.CurrentPage.Screen);
        vm.NavigateCommand.Execute(NativeScreen.Reports);
        Assert.Equal(NativeScreen.Reports, vm.CurrentPage.Screen);
    }

    [Fact]
    public void Unknown_screen_is_rejected_and_does_not_change_selection()
    {
        var vm = new NativeShellViewModel();
        Assert.False(vm.NavigateCommand.CanExecute((NativeScreen)999));
        vm.NavigateCommand.Execute((NativeScreen)999);
        Assert.Equal(NativeScreen.Dashboard, vm.CurrentPage.Screen);
    }

    [Fact]
    public void Prototype_never_claims_access_to_protected_personnel_data()
    {
        var vm = new NativeShellViewModel();
        Assert.All(vm.Pages, page =>
        {
            Assert.False(page.IsDataConnected);
            Assert.Contains("لم يتم", page.StatusMessage);
        });
    }
}
