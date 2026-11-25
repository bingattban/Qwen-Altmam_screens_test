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
    private INativeHistoricalPeriodReader? _historicalReader;
    private INativeHistoricalRangeBackend? _historicalBackend;
    private INativeClosedDayCorrectionBackend? _historicalCatalogue;
    private Func<NativeIdentity?>? _historicalActor;
    private Func<NativeIdentity?>? _liveActor;
    public SettingsPageViewModel() : base(NativeScreen.Settings, "الإعدادات")
    {
        CatchUp = new NativeAdministrativeCatchUpReview();
        Policy = new NativeClosedDayPolicyReview();
        Historical = new NativeHistoricalPeriodReview();
        HistoricalRange = new NativeHistoricalRangeReview();
        OpenHistoricalCommand = new NativeCommand(_ => OpenHistorical(), _ => CanOpenHistorical);
        OpenHistoricalRangeCommand = new NativeCommand(_ => _ = OpenHistoricalRangeAsync(), _ => CanOpenHistoricalRange);
        CloseHistoricalRangeCommand = new NativeCommand(_ => _ = CloseHistoricalRangeAsync(), _ => HistoricalRange.CanClose);
        Historical.PropertyChanged += (_, _) => NotifySessionChanged();
        HistoricalRange.PropertyChanged += (_, _) => Historical.NotifyScopeChanged();
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
    public NativeHistoricalPeriodReview Historical { get; }
    public NativeHistoricalRangeReview HistoricalRange { get; }
    public System.Windows.Input.ICommand OpenHistoricalCommand { get; }
    public System.Windows.Input.ICommand OpenHistoricalRangeCommand { get; }
    public System.Windows.Input.ICommand CloseHistoricalRangeCommand { get; }
    public bool IsReviewOpen => CatchUp.IsOpen || Policy.IsOpen || Historical.IsOpen || HistoricalRange.IsOpen;
    private static bool Authorized(NativeIdentity? actor) => actor is not null &&
        AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role);
    private bool HasCatchUpAccess => _backend is not null && Authorized(_liveActor?.Invoke());
    private bool HasPolicyAccess => _policyBackend is not null && Authorized(_policyActor?.Invoke());
    public bool CanOpenCatchUp => HasCatchUpAccess && !IsReviewOpen;
    public bool CanOpenPolicy => HasPolicyAccess && !IsReviewOpen;
    private bool HasHistoricalAccess => _historicalReader is not null && _historicalBackend is not null &&
        _historicalCatalogue is not null && Authorized(_historicalActor?.Invoke());
    public bool CanOpenHistorical => HasHistoricalAccess && !IsReviewOpen;
    public bool CanOpenHistoricalRange => HasHistoricalAccess && Historical.CanEdit && Historical.HasEvidence &&
        !CatchUp.IsOpen && !Policy.IsOpen && !HistoricalRange.IsOpen;
    public override bool IsDataConnected => HasCatchUpAccess || HasPolicyAccess || HasHistoricalAccess;
    public override string StatusMessage
    {
        get
        {
            var actor = _liveActor?.Invoke() ?? _policyActor?.Invoke() ?? _historicalActor?.Invoke();
            if (actor is null)
                return "لم يتم تسجيل الدخول أو ربط جلسة معتمدة بخدمات الإعدادات.";
            if (!AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
                return "لا تملك صلاحية استدراك التحضير الناقص؛ بقية الإعدادات قيد التحويل.";
            if (HasHistoricalAccess)
                return "البحث التاريخي وتصحيح فترة محددة متاح للإدارة؛ بقية الإعدادات قيد التحويل.";
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
    public void AttachHistorical(INativeHistoricalPeriodReader reader,INativeHistoricalRangeBackend backend,
        INativeClosedDayCorrectionBackend catalogue,Func<NativeIdentity?> liveActor)
    {
        if (_historicalReader is not null) throw new InvalidOperationException("Historical settings backend already attached.");
        _historicalReader=reader;
        _historicalBackend=backend;
        _historicalCatalogue=catalogue;
        _historicalActor=liveActor;
        NotifySessionChanged();
    }

    private void OpenHistorical()
    {
        if (CanOpenHistorical && _historicalReader is not null && _historicalActor is not null)
            Historical.Open(_historicalReader,_historicalActor,()=>HistoricalRange.IsOpen);
    }

    public async Task OpenHistoricalRangeAsync(CancellationToken ct=default)
    {
        if (!CanOpenHistoricalRange || _historicalBackend is null || _historicalCatalogue is null ||
            _historicalActor is null || Historical.Evidence is not { } evidence) return;
        if (HistoricalRange.OpenForPeriod(_historicalBackend,_historicalCatalogue,_historicalActor,()=>Historical.Evidence,evidence))
            await HistoricalRange.LoadChoicesAsync(ct);
    }

    public async Task CloseHistoricalRangeAsync(CancellationToken ct=default)
    {
        bool committed=HistoricalRange.Receipt is not null;
        if (!HistoricalRange.Close()) return;
        if (committed)
        {
            Historical.InvalidateEvidence();
            await Historical.ReadAsync(ct);
        }
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
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenHistorical)));
        PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOpenHistoricalRange)));
        ((NativeCommand)OpenHistoricalCommand).NotifyAvailabilityChanged();
        ((NativeCommand)OpenHistoricalRangeCommand).NotifyAvailabilityChanged();
        ((NativeCommand)CloseHistoricalRangeCommand).NotifyAvailabilityChanged();
    }
    public void Clear()
    {
        CatchUp.ForceClear();
        Policy.ForceClear();
        HistoricalRange.ForceClear();
        Historical.ForceClear();
    }
}
