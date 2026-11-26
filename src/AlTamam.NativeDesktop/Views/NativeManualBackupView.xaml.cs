using System.IO;
using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;
using Microsoft.Win32;

namespace AlTamam.NativeDesktop.Views;

public partial class NativeManualBackupView : UserControl
{
    public NativeManualBackupView()=>InitializeComponent();

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
