using System.Threading;
using System.Windows;

namespace AlTamam.NativeDesktop;

/// <summary>
/// Shared kernel mutex with AlTamam.DesktopHost; prevents running the
/// experimental native WPF login against a live production SQLCipher writer.
/// Never starts an extra database, scheduler, WebView2, or browser profile.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true,
            name: @"Local\AlTamamDaily_DesktopHost", createdNew: out bool acquired);
        if (!acquired)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show(
                "التمام اليومي يعمل بالفعل. أغلق النسخة التشغيلية قبل فتح نسخة WPF التجريبية.",
                "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsSingleInstance = true;
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
            _ownsSingleInstance = false;
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
