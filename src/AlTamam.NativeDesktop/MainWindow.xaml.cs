using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AlTamam.NativeDesktop.Auth;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop;

/// <summary>
/// New WPF host uses real input only for renewal, read-only session polling,
/// explicit audited logout and the same per-user encrypted data root. The
/// unconverted six section previews never expose production mutations.
/// </summary>
public partial class MainWindow : Window
{
    private readonly NativeShellViewModel _viewModel = new();
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(25) };
    private NativeSqlCipherAuthenticationBackend? _backend;
    private Task<NativeSqlCipherAuthenticationBackend>? _openingTask;
    private bool _sessionCheckRunning;
    private bool _closeInProgress;
    private bool _mayClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _sessionTimer.Tick += OnSessionTimerTick;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_openingTask is not null) return;
        try
        {
            // DPAPI, WAL reconciliation and schema migrations are intentionally
            // performed away from the STA UI thread. Production and pilot own
            // the SAME OS mutex so concurrent writers cannot start.
            _openingTask = Task.Run(() => NativeSqlCipherAuthenticationBackend.OpenAsync());
            _backend = await _openingTask;
            if (_closeInProgress) return;
            await _viewModel.InitializeAuthenticationAsync(_backend);
            if (_closeInProgress || _mayClose) return;
            _viewModel.AttachDashboardReader(_backend.CreateDashboardReader());
            // Start the SAME durable 08:00/catch-up engine as DesktopHost,
            // only after SQLCipher recovery and native UI initialization.
            _backend.StartAutomaticAttendanceScheduler();
            _backend.StartAutomaticBackupScheduler();
            _sessionTimer.Start();
        }
        catch
        {
            MessageBox.Show(
                "تعذر فتح قاعدة بيانات التمام اليومي المشفرة بأمان. لن يتاح تسجيل الدخول في النسخة التجريبية حتى تُعالج مشكلة قاعدة البيانات أو مفتاحها.",
                "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Error);
            // IsReady stays false; the login cannot authenticate against an
            // invalid DB or fall back to a fabricated privileged account.
        }
    }

    private async void OnSessionTimerTick(object? sender, EventArgs e)
    {
        if (_closeInProgress || _sessionCheckRunning || !_viewModel.IsAuthenticated) return;
        _sessionCheckRunning = true;
        try { await _viewModel.VerifyCurrentSessionAsync(); }
        finally { _sessionCheckRunning = false; }
    }

    private void OnNativePointerInput(object sender, MouseButtonEventArgs e)
    {
        if (!_closeInProgress && _viewModel.IsAuthenticated)
            _ = _viewModel.RecordGenuineUserActivityAsync();
    }

    private void OnNativeKeyboardInput(object sender, KeyEventArgs e)
    {
        if (!_closeInProgress && _viewModel.IsAuthenticated && !e.IsRepeat)
            _ = _viewModel.RecordGenuineUserActivityAsync();
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        if (_closeInProgress || !_viewModel.IsAuthenticated || _viewModel.IsBusy) return;
        await _viewModel.LogoutAsync();
        if (_viewModel.IsAuthenticated)
            MessageBox.Show("لم يكتمل إلغاء جلسة المستخدم. حاول تسجيل الخروج مجددا.",
                "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_mayClose) return;
        // Prevent release of the mutex and DB disposal until any real session
        // has been revoked, and until a pending initialization has completed.
        e.Cancel = true;
        if (_closeInProgress) return;
        _closeInProgress = true;
        IsEnabled = false;
        bool drainStarted = false;
        try
        {
            if (_openingTask is not null)
            {
                try { await _openingTask; }
                catch { /* startup failed with no acquired authenticated session */ }
            }
            if (_viewModel.IsAuthenticated)
                await _viewModel.LogoutAsync();
            if (_viewModel.IsAuthenticated)
            {
                MessageBox.Show(
                    "تعذر إلغاء الجلسة الحالية. لا يمكن إغلاق النسخة التجريبية قبل تأمين تسجيل الخروج.",
                    "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // The canonical scheduler may be writing a catch-up ledger:
            // finish its cancellation/drain BEFORE disposing SQLCipher,
            // releasing the existing single-instance OS mutex or exiting.
            if (_backend is not null)
            {
                try
                {
                    drainStarted = true;
                    await _backend.DisposeAsync();
                    _backend = null;
                }
                catch
                {
                    MessageBox.Show(
                        "تعذر تأكيد إيقاف المجدولات بأمان. لم يتم تحرير قاعدة البيانات. يلزم التحقق من حالة البرنامج قبل إعادة تشغيله.",
                        "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            _mayClose = true;
            Close();
        }
        finally
        {
            if (!_mayClose && !drainStarted)
            {
                _closeInProgress = false;
                IsEnabled = true;
            }
            // Once admission is closed, do not resume the old UI even on drain failure.
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _sessionTimer.Stop();
        _backend?.Dispose();
        _backend = null;
    }
}
