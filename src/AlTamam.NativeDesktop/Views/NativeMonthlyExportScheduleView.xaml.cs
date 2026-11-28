using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;
using Microsoft.Win32;

namespace AlTamam.NativeDesktop.Views;

public partial class NativeMonthlyExportScheduleView : UserControl
{
    public NativeMonthlyExportScheduleView()=>InitializeComponent();

    private void OnChooseFolder(object sender,RoutedEventArgs e)
    {
        if(DataContext is not SettingsPageViewModel settings || !settings.MonthlyExportSchedule.CanEdit ||
            settings.MonthlyExportSchedule.Before is not {} before)return;
        var review=settings.MonthlyExportSchedule;
        var picker=new OpenFolderDialog {Title="اختيار مجلد محلي للتصدير التلقائي",Multiselect=false};
        if(Directory.Exists(review.Directory))picker.InitialDirectory=review.Directory;
        var owner=Window.GetWindow(this);
        bool? selected=owner is null?picker.ShowDialog():picker.ShowDialog(owner);
        if(selected==true && ReferenceEquals(DataContext,settings) && review.CanEdit &&
            ReferenceEquals(review.Before,before))review.Directory=picker.FolderName;
    }
}
