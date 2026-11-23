using System.Windows.Controls;

namespace AlTamam.NativeDesktop.Views;

/// <summary>Native-only transfer dialog; canonical movement logic stays in the
/// original SQLCipher application service, not UI code-behind.</summary>
public partial class NativePersonnelTransferView : UserControl
{
    public NativePersonnelTransferView() => InitializeComponent();
}
