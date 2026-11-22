# Native WPF/MVVM migration — Phase 1: real native authentication

**Experimental. NOT PRODUCTION READY.**

This independent pilot lives only in Qwen-Altmam_screens_test.
The original AlTamam.DesktopHost executable, full WebUI, 69-route manifest,
Domain, Application, SQLCipher infrastructure, and x86/x64 Portable/Installer
workflow are intentionally preserved unchanged.

## Phase 1 — implemented and pending Windows acceptance

- A real Arabic RTL WPF/XAML login/first-run form using the same colors and
  interface hierarchy as the shipped WebUI: navy #0F2942, teal #0D9488,
  #F1F5F9 background, white card, red validation and Arabic labels.
  This is a native interpretation of the existing UI, not a claim of
  pixel-identical rendering across WPF and Chromium.
- Native login uses actual BridgeSharedServices, encrypted SQLCipher
  %LOCALAPPDATA%\AlTamam\altamam.db, Windows DPAPI key and Argon2id.
  No default password, fake administrative user, role spoofing or second DB.
- A single founding GeneralAdmin is created only on an unprovisioned DB
  with explicit matching confirmation and canonical password policy.
  Existing users use the existing role/roster/lockout rules.
- A real server-issued nonce and user/role identity are revalidated before
  any native shell content is displayed. Login failures remain on the form.
- The WPF host shares the original OS single-instance mutex to prevent
  concurrent SQLCipher access while the WebView2 host is open.
  Interrupted encrypted restore reconciliation precedes DB connection.
- Five-minute idle expiry remains server-enforced: read-only polling
  does not extend sessions; genuine WPF keyboard/mouse activity calls
  the guarded idle extension. Logout revokes the durable nonce.
- PasswordBox credentials are never bound into ViewModels or written to disk.
- Test-first XUnit and structural negative guards cover first-run policy,
  invalid credentials, navigation denial, logout, expiry and unchanged CI.

## Explicit limitations
- Six shell sections (dashboard, daily/monthly attendance, administration,
  reports and settings) remain disabled XAML previews. No personnel data or
  business mutations are connected in these sections yet.
- The 69-route migration matrix stays not_migrated: native login alone is
  not a claim of full native route parity.
- The pilot compiles as part of the solution, but four customer packages
  still publish the original complete AlTamam.DesktopHost executable.
- Running the new pilot uses the existing SQLCipher DB for **normal audited
  login and session writes**, not a cloned test database. The original app
  must be closed. Back up the original data before manual pilot acceptance.

## Next gates
1. Windows Core and full Windows Release both succeed on one exact SHA,
   real x86/x64 Portable and Installer acceptance and SHA-256 audit.
2. Real native login UI/manual DPI 100/125/150% visual check and server
   account cases: founder, role tiers, lockouts, expiry, logged-out state.
3. Convert the first real dashboard data route (read-only) with SQLCipher
   service parity, snapshots and server role validation.
4. Continue test-first conversion through all 69 routes; do not remove
   operational HTML/WebView2 until end-to-end parity has been established.


## Phase 2 — real native Dashboard (read-only slice)

The **primary** Dashboard in the experimental WPF shell now uses native XAML
instead of the disabled pilot GroupBox. The source of truth is unchanged:

- **GetDashboardSummary** semantics from DailyMonthlyReadService.DashboardSummaryAsync.
  Totals come from SQLCipher grouped attendance/assignments, never from rows.
  Saved absent attendance counts as prepared; same-day internal transfers are
  not incorrectly included as unprepared and do not leave the total force.
- **QueryDailyRoster** semantics from DailyMonthlyReadService.DailyRosterAsync.
  Filter by current local business date, assigned site, configured status,
  name/number (Arabic normalizer), and server-side pages of 100.
- **QuerySites** semantics from BridgeSharedServices.ListSitesAsync with only
  active site filters. No fabricated sites or locally cached personnel.
- Exactly the existing functional UI's primary counters: total strength,
  present (when configured), other saved statuses, automatically prepared,
  and same-day transfers when present. **No unprepared count** as a primary
  Dashboard card. Status cards are read-only roster filters.
- Roster columns show number, number (A), name, site, saved status and
  all visible administrator-defined custom columns from canonical metadata.
- Arabic right-to-left, original navy #0F2942 / teal #0D9488, white cards,
  gray surfaces, errors, pagination, responsive wrapping and selected filters.

Every fetch checks **the real server-side SQLCipher session nonce, role and
identity before AND after the canonical database reads**. The ViewModel also
rejects stale async responses after logout, account replacement and filter
changes, with immediate clearing of old personnel on error or revoked login.
The normal five-minute idle expiration is NOT prolonged by reading/polling.

### Phase 2 safety and remaining gaps

This is a first functional native screen **slice**, not the full migration
of the dashboard route:
- No automatic-preparation overlay, manual attendance mutation, 08:00 timer,
  backup or month conversion has been moved to native WPF yet.
- Historical/administration/monthly screens are still disabled placeholders.
- The route matrix remains 69 **not_migrated** because complete
  functional/e2e screen parity has not been demonstrated.
- The customer x86/x64 Portable/Installer builds still package the current
  production DesktopHost/WebView2 and must **not** be repointed to the pilot.
- Real Windows visual approval at 100/125/150% DPI and clean-machine
  acceptance remain separate final gates. The pilot uses the live encrypted
  local database and enforces the same production single-instance mutex.

**Test-first:** NativeDashboardPhase2Tests (7 cases) cover the original
counter labels/order, live-readonly rows, active filters, 100-row pagination,
session replacement, stale post-logout results, malformed response rejection,
and failure-closed search. Both Windows Core and Windows Release enforce the
negative-path native dashboard contract and compile the WPF solution before
the actual test matrix and four production package acceptance jobs.

Next: add native attendance policy/countdown and the distinct automatic
prepared register with identical permissions and cancellation behavior;
then perform authenticated/unauthenticated desktop UI parity tests before
claiming Dashboard route migration.


## Phase 3 — Dashboard auto-attendance clock and native register (read-only)

The main Dashboard now displays a native WPF countdown based on the real
AutoAttendancePolicyService.GetAsync persisted dueTimeLocal setting in SQLCipher.
It supports administrator-configured HH:mm (rather than hardcoding 08:00).
If the canonical clock is invalid or unavailable, a fake time is NOT shown.

A once-per-second WPF DispatcherTimer updates DISPLAY ONLY. Exactly as in the
old WebUI, the UI may re-read committed Dashboard counters for ten minutes
after due time at most once per 30 seconds. It does NOT trigger a scheduler,
commit or replay attendance and does NOT extend the 5-minute idle deadline.

The separate المحضّرون تلقائياً counter now opens a genuine native WPF
modal rather than silently applying a primary-roster filter. This modal
uses the original DailyMonthlyReadService.DailyRosterAsync with the
__AUTO_PREPARED__ filter, exact business date, current site, a 100-row
page and normalized name/service-number search. It shows the original
saved business date, source, UTC commit receipt, status and every visible
custom column. Neither a fake row nor a saved-status change is generated.

Both policy and register reads revalidate the actual server-issued
SQLCipher nonce and role before and after each canonical query. Closing
the modal, logout, route change, date change and account replacement
invalidate any in-flight reads and erase the personnel display.

### Important limitations

- The experimental NativeDesktop pilot does NOT start the durable scheduler.
  The authoritative EightPmSchedulerService still runs in the original
  DesktopHost, which cannot coexist with the pilot because of the shared
  database single-instance mutex. The native countdown is informational;
  it is NOT evidence that a scheduled job will run while the pilot is open.
- The new modal is READ-ONLY. The original HTML workflow supports selection
  of up to 100 people, separate status choice, server preview, human review,
  idempotent audited commit and recovery after a lost response. Those edits
  have NOT been migrated. The native pilot intentionally does not pretend
  those actions work or expose mutation buttons.
- The daily, monthly, administration and remaining screens are still not
  functionally migrated. The 69-route migration matrix remains not_migrated.
  Customer x86/x64 Portable/Installer packages continue publishing the
  original complete DesktopHost/WebView2 application.
- Real native visual and accessibility review at 100%, 125%, 150% DPI and
  clean-machine Windows acceptance remain mandatory before any release switch.

Phase 3 tests-first cover policy clock fidelity, zero scheduler writes,
separate register, paging, search, cancelled/late reads, logout,
session replacement, invalid clock, and input validation. Core and Release
must run the new negative-path contract alongside native xUnit, canonical
SQLCipher tests, actual Windows WPF/WebView2 E2E and all four package
acceptance jobs with independent SHA256 audit.

Next: reuse the original durable scheduler safely from the native host
without a duplicate writer; then migrate full preview/review/confirm
workflows and verify full 69-route parity before changing customer builds.


## Phase 4 — reuse the exact durable scheduler and native approval workflow

The experimental NativeDesktop WPF pilot now compiles **the very same**
DesktopHost/Scheduling/EightPmSchedulerService.cs source file through an
MSBuild Compile Link. It is not a copied, simplified or redesigned scheduler.
The native host starts that original scheduler **once** after database key
recovery, migrations, live-session cleanup and successful native UI startup.
Its existing 30-second/one-minute checks, administrator-configured local
due time, SQLCipher ledger and bounded missed-day catch-up all remain
authoritative. The original PreviewTokenRetentionService is likewise reused.
The same exclusive application mutex prevents DesktopHost and native WPF
from running their SQLCipher schedulers concurrently.

NativeAutomaticSchedulerGate is an independently xUnit-tested lifecycle
boundary. Window shutdown MUST first revoke the interactive session, then
await scheduler termination and its pending SQLCipher transaction, then close
the encrypted DB and finally release the original OS mutex. If startup
fails, there is no fake successful scheduler or fallback in-memory database.

The native automatic-register modal now supports **selecting individual
people and selecting up to 100 on the visible page**, retaining selections
across pages, and a separate native WPF review dialog. A canonical
GetActiveAttendanceStatuses read supplies actual active status IDs.
To avoid invalid indefinite leave/permission in this migration slice,
date-range statuses are deliberately unavailable in the pilot until their
bounded start/end-date editor is migrated; they remain in the original
shipped user interface.

For normal same-day revisions the native review backend calls **the existing**
AttendanceCommitService.PreviewAsync with the real SQLCipher user/role/nonce,
exact local business date, site, up to 100 immutable PersonIds and mode
revise. It validates the preview token, every returned PersonId, previous
status, name, service number and site against the selection. Operators see
all old statuses in the **separate** review window and explicitly press
Confirm before any attendance record is changed.

Only that Confirm command invokes AttendanceCommitService.CommitAsync with
revise=true, the original one-time token and an immutable user-intent
operationId. Existing canonical services enforce the 20:00 (configurable)
daily lock, transfer restrictions, optimistic revision checks, active
statuses, approval-token ownership/expiry, transactional receipts and
immutable SQLCipher audit records. A lost response does NOT automatically
repeat the action or mint another ID: a second explicit confirmation reuses
the same token and operationId, enabling safe receipt replay or an
authoritative rejection. Logout, changes to the selected people, account
replacement, modal cancellation and changed date revoke uncommitted
previews without unapproved writes. The original WebView2 implementation
remains the shipped customer UI and is untouched.

### Still required before native production replacement

- Native WPF date-range editor (leave/permission start/end) and full
  approved human-selection discard interaction/focus acceptance
- 100%, 125% and 150% DPI visual and keyboard audit on real Windows
- Full functional migration of all 69 routes; the manifest remains
  not_migrated until proven parity
- Native WPF implementation of all other original host responsibilities,
  including automated backup, monthly Excel, restore and settings
- Independent clean-machine x86/x64 Portable/Installer acceptance before
  any decision to redirect customer packaging from DesktopHost to native WPF

Tests-first: NativeAutomaticSchedulerPhase4Tests exercise exactly-once
start, awaited cancellation/drain, disposal without start, blocked restart
and startup failure. NativeAttendanceReviewPhase4Tests exercise no
unauthorized read, nonmutating selection, canonical preview, mismatched
person rejection, human-approved commit, immutable-ID retry after lost
reply, session swap, cancellation, 100-person bound and stale late preview.
Both Windows Core and Release execute an additional negative-path Phase 4
source/XAML contract before compiling and running all existing tests,
real Windows WebView2 E2E, four original customer package acceptances and
independent SHA-256 checks.
