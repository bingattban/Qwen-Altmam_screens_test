namespace AlTamam.NativePresentation;

/// <summary>
/// Shared native page base. Reports and unconverted settings remain placeholders;
/// connected pages and administrative catch-up use canonical SQLCipher services.
/// No placeholder may mutate domain data.
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
    public bool CanOpenCatchUp => IsDataConnected && !CatchUp.IsOpen;
    public override bool IsDataConnected => _backend is not null &&
        _liveActor?.Invoke() is { } actor &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);
    public override string StatusMessage
    {
        get
        {
            var actor = _liveActor?.Invoke();
            if (actor is null)
                return "لم يتم تسجيل الدخول أو ربط جلسة معتمدة بخدمات الإعدادات.";
            if (!AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
                return "لا تملك صلاحية استدراك التحضير الناقص؛ بقية الإعدادات قيد التحويل.";
            return _backend is null
                ? "لم يتم ربط خدمة استدراك التحضير الناقص بعد."
                : "استدراك التحضير الناقص متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
        }
    }
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
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusMessage)));
        ((NativeCommand)OpenCatchUpCommand).NotifyAvailabilityChanged();
    }
    public void Clear() => CatchUp.ForceClear();
}
