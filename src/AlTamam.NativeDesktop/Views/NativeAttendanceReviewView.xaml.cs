using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Views;

/// <summary>
/// WPF-only accessibility boundary for the isolated approved-change dialog.
/// Business changes are never implemented by UI event handlers.
/// </summary>
public partial class NativeAttendanceReviewView : UserControl
{
    public NativeAttendanceReviewView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsVisible) CancelReviewButton.Focus();
                }), DispatcherPriority.Input);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not NativeAttendanceReviewViewModel vm
                || !vm.IsOpen) return;
            e.Handled = true;
            vm.Cancel(); // no mutation; disabled while a confirmed commit runs
        };
    }
}
