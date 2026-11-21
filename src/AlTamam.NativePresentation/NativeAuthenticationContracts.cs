using AlTamam.Domain.Enums;

namespace AlTamam.NativePresentation;

/// <summary>
/// Native WPF authentication contract. Only the production SQLCipher adapter
/// may issue a real session. This surface deliberately has no access to HTML,
/// WebView2, password storage, fake roles, or unauthenticated route writes.
/// </summary>
public interface INativeAuthenticationBackend
{
    Task<bool> IsProvisionedAsync(CancellationToken ct = default);
    Task<NativeLoginResult> SignInAsync(string username, string password,
        bool firstRun, string confirmation, string fullName, CancellationToken ct = default);
    Task<NativeIdentity?> ResolveSessionAsync(string nonce, CancellationToken ct = default);
    Task RefreshIdleDeadlineAsync(string nonce, CancellationToken ct = default);
    Task RevokeSessionAsync(string nonce, CancellationToken ct = default);
}

public sealed record NativeIdentity(
    string UserId, string Username, UserRole Role, string SessionNonce);

public sealed record NativeLoginResult(NativeIdentity? Identity, string? ErrorMessage);
