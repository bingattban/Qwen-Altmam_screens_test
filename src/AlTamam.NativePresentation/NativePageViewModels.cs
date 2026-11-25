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
    private INativeClosedDayPolicyBackend? _policyBackend;
    private Func<NativeIdentity?>? _policyActor;
    private Func<NativeIdentity?>? _liveActor;
    public SettingsPageViewModel() : base(NativeScreen.Settings, "الإعدادات")
    {
        CatchUp = new NativeAdministrativeCatchUpReview();
        Policy = new NativeClosedDayPolicyReview();
        OpenCatchUpCommand = new NativeCommand(_ =>
        {
            if (CanOpenCatchUp && _backend is not null && _liveActor is not null)
                CatchUp.Open(_backend,_liveActor);
        }, _ => CanOpenCatchUp);
        EnablePolicyCommand = new NativeCommand(_ => OpenPolicy(true), _ => CanOpenPolicy);
        DisablePolicyCommand = new NativeCommand(_ => OpenPolicy(false), _ => CanOpenPolicy);
        CatchUp.PropertyChanged += (_, _) => NotifySessionChanged();
        Policy.PropertyChanged += (_, _) => NotifySessionChanged();
    }
    public NativeAdministrativeCatchUpReview CatchUp { get; }
    public System.Windows.Input.ICommand OpenCatchUpCommand { get; }
    public NativeClosedDayPolicyReview Policy { get; }
    public System.Windows.Input.ICommand EnablePolicyCommand { get; }
    public System.Windows.Input.ICommand DisablePolicyCommand { get; }
    public bool IsReviewOpen => CatchUp.IsOpen || Policy.IsOpen;
    private static bool Authorized(NativeIdentity? actor) => actor is not null &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);
    private bool HasCatchUpAccess => _backend is not null && Authorized(_liveActor?.Invoke());
    private bool HasPolicyAccess => _policyBackend is not null && Authorized(_policyActor?.Invoke());
    public bool CanOpenCatchUp => HasCatchUpAccess && !IsReviewOpen;
    public bool CanOpenPolicy => HasPolicyAccess && !IsReviewOpen;
    public override bool IsDataConnected => HasCatchUpAccess || HasPolicyAccess;
    public override string StatusMessage
    {
        get
        {
            var actor = _liveActor?.Invoke() ?? _policyActor?.Invoke();
            if (actor is null)
                return "لم يتم تسجيل الدخول أو ربط جلسة معتمدة بخدمات الإعدادات.";
            if (!AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
                return "لا تملك صلاحية استدراك التحضير الناقص؛ بقية الإعدادات قيد التحويل.";
            if (HasCatchUpAccess && HasPolicyAccess)
                return "استدراك التحضير الناقص والتحكم في إذن التصحيح الاستثنائي متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
            if (HasPolicyAccess)
                return "التحكم في إذن التصحيح الاستثنائي متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
            return HasCatchUpAccess
                ? "استدراك التحضير الناقص متاح للإدارة؛ بقية الإعدادات قيد التحويل."
                : "لم يتم ربط خدمات الإعدادات بعد.";
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
    public void AttachClosedDayPolicy(INativeClosedDayPolicyBackend backend,Func<NativeIdentity?> liveActor)
    {
        if (_policyBackend is not null) throw new InvalidOperationException("Settings policy backend already attached.");
        _policyBackend = backend;
        _policyActor = liveActor;
        NotifySessionChanged();
    }
    private void OpenPolicy(bool enabled)
    {
        if (CanOpenPolicy && _policyBackend is not null && _policyActor is not null)
            Policy.Open(_policyBackend,_policyActor,enabled);
    }
    public void NotifySessionChanged()
    {
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenCatchUp)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDataConnected)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusMessage)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenPolicy)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsReviewOpen)));
        ((NativeCommand)OpenCatchUpCommand).NotifyAvailabilityChanged();
        ((NativeCommand)EnablePolicyCommand).NotifyAvailabilityChanged();
        ((NativeCommand)DisablePolicyCommand).NotifyAvailabilityChanged();
    }
    public void Clear()
    {
        CatchUp.ForceClear();
        Policy.ForceClear();
    }
}
