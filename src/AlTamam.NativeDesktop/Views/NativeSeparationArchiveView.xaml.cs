using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>Read-only native archive; site selection never mutates personnel history.</summary>
public partial class NativeSeparationArchiveView : UserControl
{
    public NativeSeparationArchiveView() => InitializeComponent();

    private async void OnSiteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NativeSeparationArchiveViewModel vm ||
            sender is not ComboBox box || box.SelectedValue is not string site ||
            site == vm.SelectedSiteId ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen))
            return;
        await vm.SelectSiteAsync(site);
    }
}
