#!/usr/bin/env python3
"""Test-first fail-closed Phase 1 native WPF authentication and visual parity gate.

This is a structural guard in addition to actual .NET xUnit/Windows XAML builds.
It must not label the 69 operational WebUI routes as migrated.
"""
from pathlib import Path
from native_theme_contract import check_visual_resources
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {
    "theme_light":"src/AlTamam.NativeDesktop/Themes/Light.xaml",
    "theme_dark":"src/AlTamam.NativeDesktop/Themes/Dark.xaml",
    "theme_app":"src/AlTamam.NativeDesktop/App.xaml",
    "host": "src/AlTamam.NativeDesktop/MainWindow.xaml",
    "login": "src/AlTamam.NativeDesktop/Views/NativeLoginView.xaml",
    "login_csharp": "src/AlTamam.NativeDesktop/Views/NativeLoginView.xaml.cs",
    "native_app": "src/AlTamam.NativeDesktop/App.xaml.cs",
    "backend": "src/AlTamam.NativeDesktop/Auth/NativeSqlCipherAuthenticationBackend.cs",
    "vm": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativeAuthenticationPhase1Tests.cs",
    "production": "src/AlTamam.DesktopHost/MainWindow.xaml.cs",
    "release": ".github/workflows/windows-release.yml",
    "matrix": "docs/native-wpf-migration/SCREEN_MIGRATION_MATRIX.json",
}

def check(files: dict[str, str]) -> list[str]:
    problems = []
    def require(file: str, token: str, reason: str):
        if token not in files[file]:
            problems.append(reason)

    for file in ("host", "login"):
        try:
            ET.fromstring(files[file])
        except ET.ParseError as exc:
            problems.append("Invalid WPF XAML "+file+": "+str(exc))
    require("login", 'FlowDirection="RightToLeft"', "Arabic RTL login required")
    problems.extend(check_visual_resources(files["login"],files["theme_light"],files["theme_dark"],files["theme_app"],(
        ("Background","NativeHeader","#0F2942"),
        ("Background","NativeAccentFill","#0F766E"),
        ("Background","NativeCanvas","#F1F5F9"),
        ("BorderBrush","NativeBorder","#CBD5E1"),
    )))
    for label in ("اسم المستخدم", "كلمة المرور", "تأكيد كلمة المرور",
                  "إنشاء الإدارة العامة والدخول"):
        require("login", label, "Native login has dropped an existing Arabic field: "+label)
    if files["host"].count('Binding="{Binding IsAuthenticated}"') < 2:
        problems.append("Both login visibility and native shell visibility must depend on real authentication")
    require("host", "NativeLoginView", "Real XAML authentication form must be wired to the window")
    require("vm", "ResolveSessionAsync(answer.Identity.SessionNonce", "Login must resolve live nonce before unlocking navigation")
    require("vm", "RoleAuthorizationPolicy.CanPerformStandardOperations", "RBAC required on navigation")
    require("vm", "IsAuthenticated && _pages.ContainsKey", "Unauthenticated route switching must be denied")
    require("vm", "RecordGenuineUserActivityAsync", "Verified native input must govern idle refresh")
    require("vm", "VerifyCurrentSessionAsync", "Background session checker required")
    require("vm", "RevokeSessionAsync(session.SessionNonce", "Logout must revoke the durable nonce")
    require("login_csharp", "PasswordInput.Password", "Must use native PasswordBox, no fake password binding")
    require("login_csharp", "PasswordInput.Clear()", "Native PasswordBox must clear submitted credentials")
    for token in ("BridgeSharedServices", "AuthenticateAsync(username, password",
                  "ProvisionFirstAdministratorAsync(", "IsProvisionedAsync(",
                  "ResolveSessionAsync(", "RevokeSessionAsync(", "RefreshSessionIdleDeadlineAsync(",
                  "AppDataLayout.ResolveDatabasePath()",
                  "WindowsDatabaseKeyStore", "OfflineRestoreSwitch",
                  "RevokeOrphanedSessionsOnStartupAsync("):
        require("backend", token, "Live canonical SQLCipher authentication capability removed: "+token)
    require("native_app", r'Local\AlTamamDaily_DesktopHost', "Pilot must share production mutex")
    require("production", "private readonly WebView2 _webView", "The existing production WebView2 host must be preserved")
    require("release", "dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj",
            "Do not silently ship the native pilot before 69-route parity")
    require("release", "verify-release-test-shards.py", "All 1162+ original tests remain required")
    require("tests", "[Fact]", "Native authentication xUnit coverage missing")
    if files["tests"].count("[Fact]") < 7:
        problems.append("Native authentication xUnit test cases reduced")
    if "public string Password" in files["vm"] or "public string SessionNonce" in files["vm"]:
        problems.append("ViewModel must never expose password or nonce as public UI state")
    if files["matrix"].count('"migrationState": "not_migrated"') != 69:
        problems.append("Do not claim operational screen conversion before native end-to-end parity")
    return problems


if __name__ == "__main__":
    files = {key:(ROOT/path).read_text(encoding="utf-8") for key,path in SOURCES.items()}
    issues=check(files)
    if issues: raise SystemExit("NATIVE AUTH PHASE1 CONTRACT FAIL:\n"+"\n".join(issues))
    for key,term in (
        ("login","{DynamicResource NativeAccentFill}"),
        ("theme_light",'x:Key="NativeAccentFill" Color="#0F766E"'),
        ("theme_dark",'x:Key="NativeAccentFill"'),
        ("theme_app",'Source="Themes/Light.xaml"'),
        ("vm", "ResolveSessionAsync(answer.Identity.SessionNonce"),
        ("vm", "IsAuthenticated && _pages.ContainsKey"),
        ("backend", "ProvisionFirstAdministratorAsync("),
        ("backend", "RevokeOrphanedSessionsOnStartupAsync("),
        ("native_app", r'Local\AlTamamDaily_DesktopHost'),
        ("host", 'Binding="{Binding IsAuthenticated}"'),
        ("release", "dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj"),
    ):
        changed=files.copy()
        changed[key]=changed[key].replace(term,"INTENTIONALLY_REMOVED")
        assert check(changed),f"Negative-path regression was bypassed for {key}: {term}"
    print("NATIVE AUTH PHASE1 CONTRACT PASS: RTL parity, live SQLCipher, native RBAC, login/expiry/logout and unchanged production gates")
