using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;
using Microsoft.Win32;

namespace AlTamam.NativeDesktop.Views;

/// <summary>Local file chooser only; all XLSX parsing and SQL remain original.</summary>
public partial class NativeExcelPersonnelImportView : UserControl
{
    public NativeExcelPersonnelImportView() => InitializeComponent();

    private async void PickWorkbook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NativeExcelPersonnelImportViewModel vm ||
            !vm.CanSelect) return;
        var picker = new OpenFileDialog
        {
            Title = "اختيار كشف الأفراد بصيغة XLSX",
            Filter = "كشوف Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog() != true) return;
        byte[]? bytes = null;
        try
        {
            var file = new FileInfo(picker.FileName);
            if (file.Length is < 1 or > 160 * 1024)
            {
                MessageBox.Show("حجم كشف Excel يتجاوز الحد الآمن 160 كيلوبايت.",
                    "تحقق من كشف Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bytes = await File.ReadAllBytesAsync(file.FullName);
            await vm.LoadWorkbookAsync(file.Name, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException)
        {
            MessageBox.Show("تعذرت قراءة ملف Excel المحلي. اختر كشفا صحيحا متاحا للقراءة.",
                "تعذر فتح الكشف", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
