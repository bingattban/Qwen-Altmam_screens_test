using System.Windows.Controls;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Presentation-only WPF approval view. Every preview/commit, permission,
/// historical revision and SQLCipher transaction stays in the original
/// canonical C# services behind the typed native view model.
/// </summary>
public partial class NativePersonnelMutationView : UserControl
{
    public NativePersonnelMutationView() => InitializeComponent();
}
