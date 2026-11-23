using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;
using Microsoft.Win32;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// The only Windows file-system read is behind this explicit OpenFileDialog.
/// Raw bytes are never retained in WPF, logs or a new SQLite table.
/// </summary>
public partial class NativePersonnelExcelImportView : UserControl
{
    public NativePersonnelExcelImportView() => InitializeComponent();

    private async void PickWorkbook_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NativePersonnelExcelImportViewModel vm ||
            !vm.CanPickFile) return;
        var dialog = new OpenFileDialog
        {
            Title = "اختر كشف الأفراد بصيغة XLSX",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };
        if (dialog.ShowDialog() != true || !vm.CanPickFile) return;

        byte[]? workbookBytes = null;
        try
        {
            var file = new FileInfo(dialog.FileName);
            if (!file.Exists || file.Length is < 1 or > 160 * 1024 ||
                !file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                vm.ReportFileReadFailure();
                return;
            }
            workbookBytes = await File.ReadAllBytesAsync(file.FullName);
            if (!vm.CanPickFile) return;
            await vm.LoadWorkbookAsync(file.Name, workbookBytes);
        }
        catch
        {
            vm.ReportFileReadFailure();
        }
        finally
        {
            if (workbookBytes is not null)
                CryptographicOperations.ZeroMemory(workbookBytes);
        }
    }
}
