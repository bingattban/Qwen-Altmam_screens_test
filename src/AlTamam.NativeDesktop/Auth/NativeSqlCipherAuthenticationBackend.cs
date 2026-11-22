using System.IO;
using AlTamam.Application.Bridge;
using AlTamam.NativeDesktop.Dashboard;
using AlTamam.NativeDesktop.Scheduling;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Security;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Auth;

/// <summary>
/// Actual native authentication against the SAME audited SQLCipher, Argon2id,
/// account-lockout, RBAC and session services as the production WebView2 host.
///
/// The two applications share the original single-instance OS mutex and cannot
/// write the encrypted database simultaneously. This pilot never simulates
/// authentication, bypasses founding-account policy or stores a password.
/// </summary>
public sealed class NativeSqlCipherAuthenticationBackend : INativeAuthenticationBackend, IDisposable, IAsyncDisposable
{
    private readonly AlTamamDbContext _database;
    private readonly BridgeSharedServices _services;
    private bool _disposed;
    private readonly NativeAutomaticSchedulerGate _autoAttendance;
    private Task? _shutdownTask;

    private NativeSqlCipherAuthenticationBackend(AlTamamDbContext database)
    {
        _database = database;
        _services = new BridgeSharedServices(database);
        _autoAttendance = new NativeAutomaticSchedulerGate(
            new NativeDurableAttendanceScheduler(database));
    }

    public static async Task<NativeSqlCipherAuthenticationBackend> OpenAsync(
        CancellationToken ct = default)
    {
        string databasePath = AppDataLayout.ResolveDatabasePath();
        string key = new WindowsDatabaseKeyStore(
            Path.Combine(AppDataLayout.DataRoot, "db.key.protected")).LoadOrCreate(databasePath);
        // Identical recovery preflight as the audited production host. A
        // pending encrypted restore must be reconciled BEFORE database access.
        if (File.Exists(databasePath) ||
            File.Exists(Path.Combine(AppDataLayout.DataRoot,
                ".altamam-restore-activation.pending.json")))
        {
            await new OfflineRestoreSwitch()
                .ReconcileAtStartupAsync(databasePath, key, ct).ConfigureAwait(false);
        }

        var db = new AlTamamDbContext(databasePath, key);
        try
        {
            await db.InitializeAsync(ct).ConfigureAwait(false);
            var backend = new NativeSqlCipherAuthenticationBackend(db);
            // The OS mutex guarantees the original host is not still running.
            // Stale sessions from a previous process are never resurrected.
            await backend._services.RevokeOrphanedSessionsOnStartupAsync(ct).ConfigureAwait(false);
            return backend;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    // Startup is called ONLY by the authenticated native host after it has
    // initialized the UI; the canonical engine handles bounded catch-up and
    // actual due-time writes, not the dashboard countdown or a login click.
    public void StartAutomaticAttendanceScheduler()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSqlCipherAuthenticationBackend));
        _autoAttendance.StartOnce();
    }

    // Reuse one audited DB instance and session resolver for native dashboard.
    // The reader never offers a mutating operation to XAML.
    public INativeDashboardReader CreateDashboardReader()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSqlCipherAuthenticationBackend));
        return new NativeSqlCipherDashboardReader(_services, _database);
    }

    public Task<bool> IsProvisionedAsync(CancellationToken ct = default) =>
        _services.IsProvisionedAsync(ct);

    public async Task<NativeLoginResult> SignInAsync(string username, string password,
        bool firstRun, string confirmation, string fullName, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSqlCipherAuthenticationBackend));
        bool provisioned = await _services.IsProvisionedAsync(ct).ConfigureAwait(false);
        if (!provisioned)
        {
            if (!firstRun || !string.Equals(password, confirmation, StringComparison.Ordinal))
                return new NativeLoginResult(null,
                    "يجب إكمال التهيئة الأولى وتأكيد كلمة المرور المطابقة قبل إنشاء الإدارة العامة.");
            var founded = await _services.ProvisionFirstAdministratorAsync(
                username, password, ct, fullName).ConfigureAwait(false);
            if (!founded.Success)
                return new NativeLoginResult(null, founded.MessageAr);
        }
        else if (firstRun)
        {
            return new NativeLoginResult(null,
                "تمت تهيئة البرنامج مسبقا؛ لا يمكن إنشاء إدارة عامة ثانية.");
        }

        var (principal, failure) = await _services.AuthenticateAsync(username, password, ct)
            .ConfigureAwait(false);
        return principal is null
            ? new NativeLoginResult(null, failure?.MessageAr ?? "بيانات الدخول غير صحيحة.")
            : new NativeLoginResult(new NativeIdentity(
                principal.UserId, principal.Username, principal.Role, principal.SessionNonce), null);
    }

    public async Task<NativeIdentity?> ResolveSessionAsync(string nonce,
        CancellationToken ct = default)
    {
        var found = await _services.ResolveSessionAsync(nonce, ct).ConfigureAwait(false);
        return found is null ? null : new NativeIdentity(
            found.UserId, found.Username, found.Role, found.SessionNonce);
    }

    public Task RefreshIdleDeadlineAsync(string nonce, CancellationToken ct = default)
        => _services.RefreshSessionIdleDeadlineAsync(nonce, ct);

    public Task RevokeSessionAsync(string nonce, CancellationToken ct = default)
        => _services.RevokeSessionAsync(nonce, ct);

    public ValueTask DisposeAsync()
    {
        _shutdownTask ??= ShutdownCoreAsync();
        return new ValueTask(_shutdownTask);
    }

    private async Task ShutdownCoreAsync()
    {
        _disposed = true;
        try
        {
            // Wait for original scheduler to cancel and drain all pending
            // SQLCipher operations while the original OS mutex is still held.
            await _autoAttendance.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _database.Dispose();
        }
    }

    // Safety fallback for exceptional UI teardown: preserve stop-before-DB order.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
