# Native WPF/MVVM pilot — NOT PRODUCTION READY

This experimental migration lives only in Qwen-Altmam_screens_test.
The original AlTamam.DesktopHost, the full WebUI, the 69-route screen manifest,
Domain, Application, SQLCipher Infrastructure and the two original Windows release
workflows are deliberately preserved.

## Implemented in this first pilot
- Separate .NET 10 WPF pilot host with Arabic RTL XAML navigation for:
  dashboard, daily attendance, monthly attendance, administration, reports, settings.
- Pure C# MVVM navigation and command validation, under AlTamam.NativePresentation.
- Unit tests for full menu coverage, screen switching, invalid route denial and
  explicit unauthenticated / disconnected state.
- No live data, attendance writes, user auth, backups or protected operations are
  exposed in the pilot. It must NOT replace the operational host.

## Acceptance gate for subsequent migration
1. Build a typed native authentication/session adapter without bypassing RBAC,
   5-minute lockout, active-session refresh or operationId idempotency.
2. Migrate one route end-to-end using existing Application/Domain services.
3. Verify read-only parity and historical snapshots against the WebUI with synthetic SQLCipher fixtures.
4. Verify all mutations (attendance, movement, dismissal, editing, export, backup/restore)
   preserve transactions, conflict protection, audit chain and role enforcement.
5. Reach 69/69 real native route parity and verify right-to-left UI, scaling 100/125/150%.
6. Run Windows Core and Windows Release concurrently on identical SHA.
   Within Windows Release all tests must pass before x86/x64 Portable and Installer packaging.
7. Do not merge into main or switch product packaging until everything above is verified.

The legacy HTML/CSS/JS and WebView2 may be removed only AFTER complete functional parity.
