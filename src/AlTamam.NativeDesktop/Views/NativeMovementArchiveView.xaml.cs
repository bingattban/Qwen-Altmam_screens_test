using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Pure WPF navigation for an immutable, canonical SQLCipher archive.
/// No direct SQL, movement writes, or role overrides.
/// </summary>
public partial class NativeMovementArchiveView : UserControl
{
    public NativeMovementArchiveView()
    {
        InitializeComponent();
    }

    private async void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || DataContext is not NativeMovementArchiveViewModel vm ||
            sender is not ComboBox box || box.SelectedValue is not string type ||
            type == vm.SelectedType ||
            (!box.IsKeyboardFocusWithin && !box.IsDropDownOpen))
            return;
        await vm.SelectTypeAsync(type);
    }
}
