namespace AlTamam.NativePresentation;

/// <summary>
/// Shared native page base. Reports/Settings are still placeholders; the real
/// Dashboard, Daily, Monthly and Administration people register use dedicated
/// SQLCipher-backed view models. No placeholder may mutate domain data.
/// Domain operations remain in the existing audited Application/Domain/Infrastructure.
/// </summary>
public abstract class NativePageViewModel(NativeScreen screen, string title)
{
    public NativeScreen Screen { get; } = screen;
    public string Title { get; } = title;
    public virtual bool IsDataConnected => false;
    public virtual string StatusMessage =>
        "تم تفعيل تسجيل الدخول الأصلي والتحقق من الجلسة. لم يتم بعد ربط هذه الشاشة بالخدمات التشغيلية؛ هذه معاينة XAML للمرحلة التالية.";
}

public sealed class ReportsPageViewModel() : NativePageViewModel(NativeScreen.Reports, "التقارير");
public sealed class SettingsPageViewModel : NativePageViewModel, System.ComponentModel.INotifyPropertyChanged
{
    private INativeAdministrativeCatchUpBackend? _backend;
    private Func<NativeIdentity?>? _liveActor;
    public SettingsPageViewModel() : base(NativeScreen.Settings, "الإعدادات")
    {
        CatchUp = new NativeAdministrativeCatchUpReview();
        OpenCatchUpCommand = new NativeCommand(_ =>
        {
            if (CanOpenCatchUp && _backend is not null && _liveActor is not null)
                CatchUp.Open(_backend,_liveActor);
        }, _ => CanOpenCatchUp);
        CatchUp.PropertyChanged += (_, _) => NotifySessionChanged();
    }
    public NativeAdministrativeCatchUpReview CatchUp { get; }
    public System.Windows.Input.ICommand OpenCatchUpCommand { get; }
    public bool CanOpenCatchUp => _backend is not null && !CatchUp.IsOpen &&
        _liveActor?.Invoke() is { } actor &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);
    public override bool IsDataConnected => _backend is not null && _liveActor?.Invoke() is not null;
    public override string StatusMessage => "استدراك التحضير الناقص متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void AttachCatchUp(INativeAdministrativeCatchUpBackend backend,Func<NativeIdentity?> liveActor)
    {
        if (_backend is not null) throw new InvalidOperationException("Settings catch-up backend already attached.");
        _backend = backend;
        _liveActor = liveActor;
        NotifySessionChanged();
    }
    public void NotifySessionChanged()
    {
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenCatchUp)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDataConnected)));
        ((NativeCommand)OpenCatchUpCommand).NotifyAvailabilityChanged();
    }
    public void Clear() => CatchUp.ForceClear();
}
