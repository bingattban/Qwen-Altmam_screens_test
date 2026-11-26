using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AlTamam.NativeDesktop.Auth;
using AlTamam.NativeDesktop.Views;
using System.Text.Json.Nodes;
using AlTamam.DesktopHost.Bridge;
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
    private bool _restoreInProgress;
    private bool _recoveryExportInProgress;
    private bool _resetInProgress;
    private bool _resetQuiescing;
    private volatile bool _restoreQuiescing;
    private bool _restoreDrainCompleted;

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
        if (_resetInProgress || _resetQuiescing || _recoveryExportInProgress || _restoreInProgress || _restoreQuiescing || _closeInProgress || _sessionCheckRunning || !_viewModel.IsAuthenticated) return;
        _sessionCheckRunning = true;
        try { await _viewModel.VerifyCurrentSessionAsync(); }
        finally { _sessionCheckRunning = false; }
    }

    private void OnNativePointerInput(object sender, MouseButtonEventArgs e)
    {
        if (!_resetInProgress && !_resetQuiescing && !_recoveryExportInProgress && !_restoreInProgress && !_restoreQuiescing && !_closeInProgress && _viewModel.IsAuthenticated)
            _ = _viewModel.RecordGenuineUserActivityAsync();
    }

    private void OnNativeKeyboardInput(object sender, KeyEventArgs e)
    {
        if (!_resetInProgress && !_resetQuiescing && !_recoveryExportInProgress && !_restoreInProgress && !_restoreQuiescing && !_closeInProgress && _viewModel.IsAuthenticated && !e.IsRepeat)
            _ = _viewModel.RecordGenuineUserActivityAsync();
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        if (_resetInProgress || _resetQuiescing || _recoveryExportInProgress || _restoreInProgress || _restoreQuiescing || _closeInProgress || !_viewModel.IsAuthenticated || _viewModel.IsBusy) return;
        await _viewModel.LogoutAsync();
        if (_viewModel.IsAuthenticated)
            MessageBox.Show("لم يكتمل إلغاء جلسة المستخدم. حاول تسجيل الخروج مجددا.",
                "التمام اليومي", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Physical native button only: the review captures the exact live session,
    // selected file and completed comparison. No XAML password binding.
    internal async Task RequestRestoreAsync(SettingsPageViewModel settings)
    {
        if(_resetInProgress || _resetQuiescing || _recoveryExportInProgress || _restoreInProgress || _restoreQuiescing || _closeInProgress || _mayClose ||
            _viewModel.IsBusy || _backend is not { } backend ||
            !ReferenceEquals(settings,_viewModel.Settings) ||
            !ReferenceEquals(_viewModel.CurrentPage,settings) ||
            settings.Inspection.GetRestoreIntent() is not { } intent) return;

        _restoreInProgress=true;
        try
        {
            string? password=NativeRestoreConfirmationDialog.RequestFreshPassword(
                this,System.IO.Path.GetFileName(intent.Evidence.Path),intent.Evidence.Sha256,
                intent.Comparison.Incoming.People,intent.Comparison.Current.People);
            if(password is null || !settings.Inspection.MatchesRestoreIntent(intent)) return;
            IsEnabled=false;
            Task<AlTamam.Application.Bridge.NativeRestoreActivationCoordinator.Result?> activation;
            try
            {
                activation=backend.TryRestoreAsync(intent,password,
                    (prepared,ct)=>Dispatcher.InvokeAsync(()=>
                        !ct.IsCancellationRequested && settings.Inspection.MatchesRestoreIntent(intent) &&
                        NativeRestoreConfirmationDialog.ConfirmFinal(this,prepared)).Task,
                    ()=>
                    {
                        // Publish the boundary before dispatching to STA so a
                        // timed-out callback cannot accidentally reopen input.
                        _restoreQuiescing=true;
                        return Dispatcher.InvokeAsync(()=>
                        {
                            _sessionTimer.Stop();
                            DataContext=null;
                        }).Task;
                    },
                    ()=>Dispatcher.InvokeAsync(()=>
                    {
                        _restoreDrainCompleted=true;
                        _backend=null;
                    }).Task);
            }
            finally { password=null; }

            var result=await activation;
            if(result is null)
            {
                MessageBox.Show(this,"لم تنفذ الاستعادة. ألغيت الموافقة أو تعذر التحقق من الحساب أو النسخة أو المفتاح. لم تستبدل قاعدة البيانات.",
                    "الاستعادة الآمنة",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            if(!_restoreDrainCompleted)
                throw new InvalidOperationException("Restore returned without a completed native drain.");
            MessageBox.Show(this,"اكتملت الاستعادة مع الاحتفاظ بنسخ الإنقاذ المشفرة. افتح البرنامج مجددا لتسجيل الدخول ببيانات النسخة المستعادة.",
                "اكتملت الاستعادة",MessageBoxButton.OK,MessageBoxImage.Information);
            _mayClose=true;
            Close();
        }
        catch
        {
            MessageBox.Show(this,_restoreQuiescing
                ? "تعذر استكمال الاستعادة بعد بدء الإغلاق. لن تستأنف الجلسة القديمة. احتفظ بنسخ الإنقاذ؛ يتحقق البرنامج من سجل التعافي عند التشغيل التالي."
                : "تعذر فحص أو تجهيز النسخة قبل التبديل. تحقق من الملف والمفتاح وأعد الفحص.",
                "تعذر الاستعادة",MessageBoxButton.OK,MessageBoxImage.Error);
            if(_restoreQuiescing && _restoreDrainCompleted)
            {
                _mayClose=true;
                Close();
            }
        }
        finally
        {
            _restoreInProgress=false;
            if(!_restoreQuiescing && !_mayClose)
            {
                // A rejected or cancelled attempt requires a fresh review.
                settings.Inspection.ForceClear();
                IsEnabled=true;
            }
            // On an incomplete drain retain the mutex and keep old UI disabled.
        }
    }

    internal async Task RequestRecoveryExportAsync(SettingsPageViewModel settings)
    {
        if(_resetInProgress || _resetQuiescing || _recoveryExportInProgress || _restoreInProgress || _restoreQuiescing || _closeInProgress ||
            _mayClose || _viewModel.IsBusy || _backend is not { } backend ||
            !ReferenceEquals(settings,_viewModel.Settings) ||
            !ReferenceEquals(_viewModel.CurrentPage,settings) ||
            settings.RecoveryExportActor is not { } actor) return;

        _recoveryExportInProgress=true;
        try
        {
            var picker=new Microsoft.Win32.SaveFileDialog
            {
                Title="حفظ مفتاح الاسترداد المشفر في ملف جديد",
                Filter="مفتاح استرداد التمام (*.altrecovery)|*.altrecovery",
                DefaultExt=".altrecovery",AddExtension=true,OverwritePrompt=false,
                CheckPathExists=true,RestoreDirectory=true
            };
            if(picker.ShowDialog(this)!=true) return;
            if(settings.GetRecoveryExportIntent(picker.FileName) is not { } intent ||
                !intent.Matches(actor)) return;
            if(System.IO.File.Exists(intent.Destination))
            {
                MessageBox.Show(this,"الملف موجود مسبقا. اختر اسما جديدا؛ لا يتم استبدال مفتاح استرداد سابق.",
                    "ملف موجود",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }

            var credentials=NativeRecoveryKeyExportDialog.RequestCredentials(this,intent.Destination);
            if(credentials is null || !intent.Matches(settings.RecoveryExportActor)) return;
            IsEnabled=false;
            Task<bool> exporting;
            try
            {
                exporting=backend.ExportRecoveryKeyAsync(intent,credentials.CurrentAdminPassword,
                    credentials.RecoveryPassphrase);
            }
            finally { credentials=null; }
            bool saved=await exporting;
            MessageBox.Show(this,saved
                ? "تم حفظ ملف مفتاح الاسترداد المشفر. احفظ كلمة مروره في مكان آمن منفصل عن الملف والنسخة الاحتياطية."
                : "لم يتم تصدير المفتاح؛ تعذر التحقق من الحساب أو كلمة المرور أو صلاحية الجلسة.",
                "مفتاح الاسترداد",MessageBoxButton.OK,saved?MessageBoxImage.Information:MessageBoxImage.Warning);
        }
        catch
        {
            MessageBox.Show(this,"تعذر تأكيد حفظ مفتاح الاسترداد. تحقق من المسار والملف؛ لا تستبدل أي مفتاح سابق.",
                "تعذر التصدير",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally
        {
            _recoveryExportInProgress=false;
            IsEnabled=true;
            await _viewModel.VerifyCurrentSessionAsync();
        }
    }

    internal async Task RequestFullResetAsync(SettingsPageViewModel settings)
    {
        if(_resetInProgress || _resetQuiescing || _recoveryExportInProgress || _restoreInProgress ||
            _restoreQuiescing || _closeInProgress || _mayClose || _viewModel.IsBusy ||
            _backend is not { } backend || !ReferenceEquals(settings,_viewModel.Settings) ||
            !ReferenceEquals(_viewModel.CurrentPage,settings) ||
            settings.RecoveryExportActor is not { } actor) return;

        _resetInProgress=true;
        IsEnabled=false;
        try
        {
            var review=await Task.Run(()=>backend.PreviewFullResetAsync(actor));
            if(review is null || !review.Matches(settings.RecoveryExportActor))
            {
                MessageBox.Show(this,"تعذر إنشاء معاينة تصفير صالحة للجلسة الحالية.",
                    "التصفير الآمن",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            var approval=NativeFullResetDialog.Confirm(this,review);
            if(approval is null || !review.CanCommit(settings.RecoveryExportActor,approval.Consent,approval.Elapsed))
                return;

            // The accepted destructive operation owns the process until a
            // verified drain/disposal finishes. No old session may resume.
            _resetQuiescing=true;
            _sessionTimer.Stop();
            DataContext=null;
            var accepted=approval;
            approval=null;
            var result=await Task.Run(()=>backend.CommitFullResetAsync(review,accepted.Consent,
                accepted.Password,accepted.Elapsed));
            if(result?.Success==true && result.Data is JsonObject data &&
                data["reset"]?.GetValue<bool>()==true && data["requiresFirstSetup"]?.GetValue<bool>()==true &&
                data["rescuePath"]?.GetValue<string>() is { Length:>0 } rescue)
            {
                string retained=data["backupFilesNotDeleted"]?.ToString()??"غير معروف";
                MessageBox.Show(this,"اكتمل التصفير. نسخة الإنقاذ المشفرة: "+rescue+
                    "\nعدد النسخ التي تعذر حذفها: "+retained+"\nافتح البرنامج مجددا للتهيئة الأولى.",
                    "اكتمل التصفير",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,result?.Success==false?result.UserMessage:
                    "تعذر تأكيد نتيجة التصفير. تحقق من نسخة الإنقاذ وحالة البيانات عند التشغيل التالي.",
                    "التصفير الآمن",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        }
        catch
        {
            MessageBox.Show(this,_resetQuiescing
                ? "تعذر تأكيد نتيجة التصفير؛ لن تستأنف الجلسة القديمة. تحقق من البيانات ونسخة الإنقاذ عند التشغيل التالي."
                : "تعذر تجهيز معاينة التصفير.",
                "تعذر التصفير",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally
        {
            if(_resetQuiescing)
            {
                try
                {
                    await backend.DisposeAsync();
                    _backend=null;
                    _mayClose=true;
                    Close();
                }
                catch
                {
                    // Failed drain: keep the process mutex and old UI disabled.
                    MessageBox.Show(this,"تعذر تأكيد إيقاف خدمات البرنامج بأمان. لا تستأنف الجلسة القديمة.",
                        "تعذر الإغلاق",MessageBoxButton.OK,MessageBoxImage.Error);
                }
            }
            else
            {
                IsEnabled=true;
                await _viewModel.VerifyCurrentSessionAsync();
            }
            _resetInProgress=false;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_mayClose) return;
        if (_resetInProgress || _resetQuiescing)
        {
            e.Cancel=true;
            return;
        }
        if (_recoveryExportInProgress)
        {
            e.Cancel=true;
            return;
        }
        if (_restoreInProgress || _restoreQuiescing)
        {
            if (!_restoreInProgress && _restoreDrainCompleted)
            {
                _mayClose=true;
                return;
            }
            e.Cancel=true;
            return;
        }
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
