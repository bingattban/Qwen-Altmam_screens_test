using System.Linq;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// UI-only selection forwarding. The authorized ViewModel revalidates every
/// object identity against the current roster and SQLCipher role before read.
/// </summary>
public partial class NativePersonnelGroupReportView : UserControl
{
    public NativePersonnelGroupReportView() => InitializeComponent();

    private void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not NativePersonnelGroupReportViewModel vm || !vm.IsOpen ||
            sender is not ListBox picker)
            return;
        vm.SelectRows(picker.SelectedItems
            .OfType<NativePersonnelDisplayRow>()
            .ToArray());
    }
}
