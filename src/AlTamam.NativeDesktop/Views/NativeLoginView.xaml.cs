using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// Passwords remain inside WPF PasswordBox and are passed directly to the
/// server-authentication call once. Never bind passwords into the ViewModel,
/// save them in settings, log them, or use a fake successful login.
/// </summary>
public partial class NativeLoginView : UserControl
{
    public NativeLoginView()
    {
        InitializeComponent();
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NativeShellViewModel vm || !vm.IsReady || vm.IsBusy)
            return;
        string username = UsernameInput.Text;
        string password = PasswordInput.Password;
        string confirmation = vm.IsFirstRun ? ConfirmPasswordInput.Password : "";
        string fullName = vm.IsFirstRun ? FullNameInput.Text : "";
        try
        {
            await vm.LoginAsync(username, password, confirmation, fullName);
        }
        finally
        {
            // Clear control memory immediately following submission, including
            // the unsuccessful case, to avoid stale sensitive credentials.
            PasswordInput.Clear();
            ConfirmPasswordInput.Clear();
        }
    }
}
