using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

public partial class NativeAccountCreateView : UserControl
{
    public NativeAccountCreateView() => InitializeComponent();

    private async void OnConfirmClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is not NativeAccountCreateReview review || !review.CanConfirm) return;
        string password=PasswordInput.Password;
        string confirmation=ConfirmationInput.Password;
        ClearPasswords();
        try { await review.ConfirmAsync(password,confirmation); }
        finally { ClearPasswords(); }
    }
    private void OnPreviewVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(e.NewValue is false) ClearPasswords();
    }
    private void OnUnloaded(object sender,RoutedEventArgs e)=>ClearPasswords();
    private void ClearPasswords()
    {
        PasswordInput?.Clear();
        ConfirmationInput?.Clear();
    }
}
