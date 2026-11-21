using System.Windows;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new NativeShellViewModel();
    }
}
