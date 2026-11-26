using System.Windows;
using System.Windows.Controls;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

public partial class NativeAccountLifecycleView : UserControl
{
    public NativeAccountLifecycleView() => InitializeComponent();

    private async void OnConfirmClick(object sender,RoutedEventArgs e)
    {
        if(DataContext is not NativeAccountLifecycleReview review || !review.CanConfirm) return;
        string password=review.NeedsPassword?PasswordInput.Password:"";
        string confirmation=review.NeedsPassword?ConfirmationInput.Password:"";
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
