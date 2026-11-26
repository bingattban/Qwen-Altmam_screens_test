using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;
using Microsoft.Win32;

namespace AlTamam.NativeDesktop.Views;

public partial class NativeManualBackupView : UserControl
{
    public NativeManualBackupView()=>InitializeComponent();

    private void OnChooseBackup(object sender,RoutedEventArgs e)
    {
        if(DataContext is not SettingsPageViewModel settings || !settings.CanOpenInspection) return;
        var picker=new OpenFileDialog
        {
            Title="اختيار نسخة احتياطية مشفرة للفحص",
            Filter="نسخ التمام المشفرة (*.dbbackup;*.db)|*.dbbackup;*.db",
            CheckFileExists=true, CheckPathExists=true, Multiselect=false
        };
        var current=settings.Overview.Snapshot?.BackupDirectory;
        if(!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            picker.InitialDirectory=current;
        var owner=Window.GetWindow(this);
        bool? selected=owner is null?picker.ShowDialog():picker.ShowDialog(owner);
        if(selected==true && settings.CanOpenInspection)
            settings.OpenInspection(picker.FileName);
    }

    private async void OnRestoreClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is SettingsPageViewModel settings && settings.Inspection.CanRequestRestore &&
            Window.GetWindow(this) is MainWindow host)
            await host.RequestRestoreAsync(settings);
    }

    private async void OnRecoveryExportClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is SettingsPageViewModel settings && settings.CanExportRecovery &&
            Window.GetWindow(this) is MainWindow host)
            await host.RequestRecoveryExportAsync(settings);
    }

    private async void OnFullResetClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is SettingsPageViewModel settings && settings.CanExportRecovery &&
            Window.GetWindow(this) is MainWindow host)
            await host.RequestFullResetAsync(settings);
    }

    private void OnChooseFolder(object sender,RoutedEventArgs e)
    {
        if(DataContext is not SettingsPageViewModel settings || !settings.CanOpenBackup) return;
        var picker=new OpenFolderDialog { Title="اختيار مجلد محلي للنسخ والتصدير",Multiselect=false };
        var current=settings.Overview.Snapshot?.BackupDirectory;
        if(!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            picker.InitialDirectory=current;
        var owner=Window.GetWindow(this);
        bool? selected=owner is null?picker.ShowDialog():picker.ShowDialog(owner);
        if(selected==true && settings.CanOpenBackup)
            settings.OpenBackupReview(picker.FolderName);
    }
}
