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
The bounded native start/end date editor now enables active leave and permission
statuses. The authoritative attendance policy still rejects missing, reversed,
future-started, or overlong date ranges before any SQLCipher preview. The
original shipped user interface continues to work unchanged.

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

- Complete native daily-attendance grid and manual keyboard/focus acceptance
  for the bounded leave/permission editor and review dialogs
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
\n## Phase 5 — reconciliation of overlapping PR #12 and PR #13\n\nPR #13 was merged first after both Windows Core and Windows Release passed on\nits exact source SHA, including all four original DesktopHost packages and\nindependent SHA-256 audit. PR #12 independently passed the same gates but\nproposed overlapping native date-picker implementation and duplicate preview\ncontracts. The safe reconciliation keeps the tested PR #13 UI and backend as\nthe **single** implementation, without duplicating the date policy or any\nSQLCipher writer. The additional PR #12 guarantees are retained as targeted\nregression tests, an explicit Phase 5 fail-closed source/XAML contract in\n**both** Windows workflows, updated native register guidance, and this\ndocumentation. PR #13's protection against reopening an unresolved approved\noperation remains in force. The original Windows customer packages are still\nWebView2/DesktopHost until native production parity and real-machine UAT.\n\n## Phase 6 — daily attendance, slice 1: native roster read and filters\n\nThe previous disabled daily-attendance placeholder is replaced with a real\nWPF RTL read-only roster connected to the already-verified SQLCipher\nNativeSqlCipherDashboardReader. Its canonical DailyMonthlyReadService\nDailyRosterAsync and DashboardSummaryAsync handle pagination, site and\nattendance-status filters, unprepared/automatic/transfer status filters,\nsearch by name or service number and configured custom columns. The native\nrank column shows **only** a real visible custom field captioned الرتبة/رتبة;\nif the authoritative schema has no such field the cell remains blank rather\nthan inventing personnel ranks. Remaining custom fields are appended once.\n\nSecurity: 100 rows/page, read-only DataGrid; original native nonce and role\nverified before and after SQLCipher reads; changed session and logout wipe\npersonnel immediately; stale/in-flight async results cannot repaint; invalid\nfilters and malformed response echoes fail closed. Daily navigation triggers\na real read, not a scheduler run or hidden write. Six test-first xUnit cases\nand the additional negative-path Phase 6 contract run in both Windows CI\nworkflows. No new SQL, tables, migrations, alternative scheduler or token.\n\n**Still pending for this route:** native explicit initial attendance or\nrevisions for selected people via audited preview/confirm, allowed-status and\nclosed-day controls, operator receipts and full original-route parity. The\ncustomer x86/x64 Portable/Installer remain original DesktopHost/WebView2.\n\n**The 69-route migration matrix remains NOT MIGRATED** until whole route\nbehavior and clean-machine acceptance are independently verified.\n

## Phase 7 — native monthly attendance, read-only archive pilot

The previously disabled monthly preview is replaced with a genuine RTL WPF
month-by-month **read-only** grid. This view reuses the existing SQLCipher
`DailyMonthlyReadService.ArchivePeriodsAsync` and `MonthlyGridAsync` services,
with server-authorized session checks before and after each read. The canonical
history provides date-indexed attendance symbols, labels, saved identity/site
snapshots and movement/separation/return notes; missing dates remain empty.
The grid allows archived periods only (plus the current month), historical
name/service number search, site filtering and 100-person pages. Native
columns display الرقم، الرقم (ع)، الرتبة، الاسم، الموقع followed by the
numbered days and extra configured custom columns. Visible rank and custom
fields reflect **current custom-field values** because the authoritative
projection currently does not store dated custom-field snapshots; they must
not be confused with archived names and attendance.

Three safety boundaries remain unchanged: the native SQLCipher session and
role policy, immutable PersonId for historical identity, and the original
audited attendance writer. No new SQL or schema, no synthetic statuses,
no monthly data mutations, no export route or claiming 69-route parity.
Six test-first xUnit tests plus a negative-path Phase 7 contract run in
both Windows Core and Release before packaging. Physical desktop keyboard,
DPI 100/125/150%, installer and archive acceptance are still required
before the pilot can be considered production-ready.

**Still to implement in the monthly route:** approved monthly cell details
interaction, archive-close/reopen and Excel export where permitted by
the original functional routes. All four existing release packages remain
DesktopHost/WebView2 until verified native parity.

 
## Phase 7 — slice 2: canonical monthly day details (read-only)

Native WPF monthly day columns now use accessible click/keyboard buttons
to open the **original** `DailyMonthlyReadService.MonthlyCellDetailsAsync`
through `NativeSqlCipherDashboardReader`. A fresh verified live SQLCipher
principal is required both before and after every historical detail request.
The original service enforces historical site perspective. Each response
must echo the immutable PersonId and exact day selected in the current
native month/page. Forged rows, old page references, future/out-of-range
days, session swaps, filter changes, late responses and logout all fail
closed without displaying another person's data.

The read-only RTL dialog displays dated identity/name/service number/site,
committed attendance status, original attendance source, movement or
separation/return, and up to 100 actual historical revisions. If no
attendance record exists it explicitly says no attendance was recorded:
**no missing status is inferred**. Close/navigation/refresh invalidates
pending detail reads; the overlay never mutates attendance or site data.

Four test-first xUnit cases cover canonical snapshots, forgery/stale
day rejection, nonce/logout cancellation and invalid response echo.
The existing Phase 7 static negative-path contract now also protects
this detailed read in BOTH Windows Core and Release.

This is not equivalent to full monthly Excel/export, close/reopen or
all 69 original routes. Customer packages are still DesktopHost/WebView2
until native functional parity, physical keyboard/DPI (100/125/150%)
and clean-machine x86/x64 portable/installer approval.

 
## Phase 7 — slice 3: monthly Excel export with explicit operator review

The native monthly WPF page offers **معاينة تصدير Excel** only for a
live SQLCipher-authorized session, currently applied search term,
the selected archived/current month and valid site. Opening the modal
does not create any files. The authoritative
`MonthlyExcelExportService.PreviewAsync` returns the actual matching
historical person count, chosen site, filename and number of days;
the user separately presses **تأكيد تصدير Excel** to export.

The SAME original `MonthlyExcelExportService.ExportAsync` handles
file creation, local preconfigured `OutputDirectorySettings` validation,
non-overwriting file names, authenticated site captions, Arabic RTL XLSX,
500-row export-only SQLCipher pagination and atomic .part completion.
If all sites are selected, canonical export creates a consolidated
workbook plus separate relevant site files, and reports the resulting
count and primary path. The native UI does **not** accept a filesystem
path or create a second XLSX implementation.

A monthly search draft must be applied before a preview can open.
A frozen period/site/search and live nonce are rechecked before
confirmation and after export. Logout, navigation, page refresh, or
modified filters revoke the in-flight intent, suppressing stale
results. A failed or ambiguous export cannot be retried from an old
preview; inspect the configured reports directory before manually
starting a new preview. Preview is a point-in-time count, not a promise
that SQLCipher rows will remain unchanged while a long export runs.

Five test-first xUnit cases cover zero pre-confirm writes, site and
search propagation, multi-file all-sites behavior, logout/nonce changes
during awaited operations, and malformed preview/receipt rejection.
The same Phase 7 negative-path contract runs in BOTH Windows CI gates.

**Production caveat:** Existing x86/x64 Portable/Installer still package
the original DesktopHost/WebView2 application. Native WPF export relies
on the currently configured local reports directory; a complete native
output-directory settings UI, remaining 69-route parity and real
Windows keyboard/DPI acceptance must precede switching production.

 
## Phase 8 — slice 1: Native WPF administration personnel register (read-only)

The Administration placeholder is replaced by a real RTL WPF **سجل الأفراد**
bound to the ORIGINAL SQLCipher `PersonRosterService.QueryPersonsAsync`
and `GetPersonSummaryAsync`. The list supports current organization status
(active, externally transferred, separated, archived or all), current site,
name/service number search, bounded 100-person pages, stable PersonId,
rank/custom columns, site and status. When a manager selects a person, a
separate read-only dialog presents their current name and service number
plus up to 50 saved historical identity revisions. Archived users are
only included when an explicit organization-state filter requests them.

Only the original role policy's General/Ordinary Administration roles can
navigate to the native management page or read its canonical data.
`NativeSqlCipherDashboardReader` checks the SQLCipher nonce and role
BEFORE and AFTER every read. The view model rejects stale PersonId
references, mismatched page filters, altered row revisions, canceled
reads, invalid role changes, forged service echoes and duplicate ids.
Logout immediately scrubs rows and profile data. No independent SQL,
fake roster, direct import, create, edit, restore, separation or
transfer execution exists in this first native slice.

Six test-first xUnit cases cover unauthenticated/supervisor denial,
real rank/service-number/site projection, search/site/status/page
behavior, forged/stale PersonId and nonce changes, immutable historical
profile review and async logout/malformed data. A dedicated Phase 8
static negative-path guard runs in BOTH Windows Core and Release
before C# compilation and all four customer package jobs.

**Not yet converted:** personnel create/update/delete and their audited
preview/confirm flows, internal/external moves, dismissal/return, site
and status CRUD, roster Excel import/export, users, reports, backups
and settings, as well as full 69-route parity and real Windows clean
machine keyboard/DPI approval. Customer binaries remain the original
DesktopHost/WebView2 until parity is verified.

 
## Phase 8 — slice 2: native personnel create and identity correction

The WPF administration people list now has two authorized actions:
**إضافة فرد** (new permanent PersonId) and **تعديل اسم أو رقم الفرد المحدد**.
Both use a separate RTL preview/confirmation dialog. No WPF SQLite
writer, direct SQL, replacement identity store or destructive edit
path was introduced. Only the existing canonical audited services
`ArchivedReturnService.PreviewAsync`,
`PersonRosterService.PreviewPersonIdentityUpdateAsync`,
`CreatePersonAsync` and `UpdatePersonIdentityAsync` are used.
General/Ordinary Administration role and the exact live SQLCipher
actor/nonce must pass before AND after every service operation.

**Create:** The original archived-identity preview checks active-number
uniqueness and all name/number collisions with transferred, separated
and force-archived people; an existing active site is required.
A missing/unspecified site is a distinct explicit choice.
If even one archived identity candidate exists, native confirmation
is blocked; the existing legacy identity-resolution/return route must
be used, without pretending a new PersonId represents the same human.
The canonical create transaction re-checks collisions and assigns a
fresh PersonId with audit and immutable initial identity snapshot.

**Edit:** The UI binds exactly the current canonical page row's
PersonId/RowVersion, not the visible name as a key. The original
preview checks identity number collisions and returns the old/new
snapshots, a token and the effective local date. The separate human
confirmation includes the reason, frozen row revision, date, and
one immutable operationId. SQLCipher's original atomic edit preserves
the previous identity-history interval, the original attendance and
audited change reason. Stale revisions or changed page/role/nonce
invalidate the approval.

No save happens just by typing or previewing. Any input change
revokes a previous preview. The UI sends the write only once and
does NOT retry after an ambiguous error, logout or cancellation,
because the canonical transaction may already have committed.
The operator should inspect the roster and audit log before trying
again; a different authenticated session is required to clear the
ambiguous-write lock. Closing a successful receipt refreshes the
canonical directory.

Six test-first xUnit cases cover no-session/supervisor denial, no-write
previews, archived collision fail-closed, stable PersonId/revision/
reason confirmation, cancellation/nonces and no automatic retries.
A separate static negative-path contract is executed in BOTH Core and
Release before compiling, and the original test-first four-package
x86/x64 Portable/Installer gate remains unchanged.

**Limitations:** This slice intentionally does not migrate choosing
same/different archived identity, dismissed-number reuse, restoring
persons, external/internal transfers, separation, status/site CRUD,
Excel personnel import, full reports or native configuration.
The production packages continue to use DesktopHost/WebView2 until
all 69 routes, keyboard/DPI testing and clean-machine acceptance
are completed.

 
## Phase 8 — slice 3: native internal/external personnel transfer (one person)

A manager can choose **نقل داخلي** or **نقل خارجي** for one CURRENT
person from the native WPF personnel directory. The source site's
canonical **SiteId must first be explicitly selected in the directory
filter**, and the chosen row's current visible site name must agree
with that site's canonical catalogue entry. Transfers are not offered
from "all sites", from an archived/separated person, or from a
supervisor session. The list remains read-only; movement happens only
from a separate RTL preview/confirm overlay.

The typed WPF adapter delegates BOTH phases strictly to the original
`PersonnelMovementService.PreviewAsync` and `CommitAsync`, using
`BridgeSharedServices.ResolveSessionAsync` and exact actor/role/nonce
checks before and after each call. Source site, active PersonId,
original number, name, RowVersion and source assignment are verified
against the canonical server preview. Internal destinations must be
a distinct active site (enforced by the original service); external
transfers require an explicit 2–150 character target organization.
The original SQLCipher transaction rejects changes to an old day,
a duplicate movement that day, stale assignments and changed
revisions, and atomically records the movement history, new/closed
assignment, audit evidence and one-use preview token. External moves
also use the existing account suspension policy.

Merely opening the form or previewing NEVER mutates data. Editing
the destination revokes preview approval. The operator must separately
confirm, which sends a single immutable operationId. A lost or
ambiguous commit reply must NOT trigger an automatic retry; inspect
the movement archive and operations journal first. Leaving the
page, changing source filter, logout, stale person row or changed
session cancels pending approval and suppresses delayed responses.
Successful approval is followed by a fresh original roster query.

Six test-first xUnit cases cover permissions, mandatory source site,
no-write internal preview, named external transfer, changed input
revocation, forged/stale person snapshot, nonce and logout, and
no double-send after an ambiguous result. A Phase 8 fail-closed
security contract executes in parallel Windows Core AND Release,
prior to x86/x64 Portable+Installer packaging, real Windows package
acceptance and independent SHA-256 audit.

**Limitations and deliberate exclusions:** this initial native slice
moves **one person at a time**. The canonical movement service
supports 1–100 people per operation, but the native multi-select UX
and bulk preview are not yet implemented or tested. Archive search,
details, separation, return/restoration, site/status administration
and remaining functionality still require migration. All customer
installer/portable packages remain the approved DesktopHost/WebView2
product until full native 69-route parity and physical Windows
keyboard/DPI/clean-machine acceptance.


## Phase 8 — slice 4: native read-only historical movement archive

The existing native administration directory offers **أرشيف التنقلات**
as a separate authenticated, Arabic RTL modal. This slice is read-only.
The original `PersonnelMovementService.ArchiveAsync` and
`DetailsAsync` remain the sole source for historically saved movement
snapshots, sorted and paged in SQLCipher. Type filters are `all`
(internal, external and return), `internal`, `external` and `return`;
search by historical name/service number is applied on the server, with
a hard bound of 50 movements per page. Separation remains in the
original separate archive and is **not** incorrectly included.

Each archive listing/detail call revalidates the exact SQLCipher
General/Ordinary Administration actor and session nonce both before and
after invoking the original canonical service. The WPF view checks the
original immutable movement ID, PersonId, dated name, number, origin,
destination, type and UTC commit timestamp against the selected row.
Forged or stale responses, unexpected pages, unauthorized sessions and
late asynchronous replies fail closed. Closing the archive, logout,
navigation and a personnel directory refresh discard pending reads and
erase historical rows. No SQLite writer, audit mutation, schema change,
new index, copy of the historical data, or alternate archive source
is introduced.

Six C# xUnit regressions were committed **before** implementation,
including read authorization, exact bounded pages and history, filters,
invalid input, logout cancellation and forged detail rejection.
A separate Python source/XAML negative-path contract runs with its own
immediate exit-code guard in BOTH Windows Core and Windows Release. The
existing nine full Release shards, real Windows WPF/WebView2 tests,
x86/x64 Portable and Installer packaging/acceptance and independent
SHA-256 audit remain mandatory on the exact final source SHA.

**Still not native:** batch transfer (up to 100), personnel separation,
archived-person identity resolution/return, site/status CRUD, full
settings, historical reports and the remaining 69-route parity. The
four customer packages still use the original complete
DesktopHost/WebView2, not this experimental WPF pilot. Real Windows
keyboard/DPI checks at 100%, 125%, 150% and independent clean-machine
acceptance remain necessary before any production switch.


## Phase 8 — slice 5: native one-person separation (preview, 5-second approval, commit)

The WPF personnel directory adds **فصل الفرد المحدد** for managers, and
only for ONE currently active person in an explicitly selected canonical
source-site filter. The current person must match the original page's
`PersonId`, `RowVersion`, site name and manager SQLCipher session.
Separation from "all sites", inactive/archive rows, a supervisor session
or stale page context is deliberately prohibited.

The Arabic RTL dialog requires a **5–1000 character explicit reason**.
Typing or opening the modal never writes to the database. A preview
calls the original `PersonnelSeparationService.PreviewAsync`; the
canonical response includes the live name, number, site, assignment,
revision, today's date, reason and one-use preview token. All are checked
against the frozen selected row. The UI waits at least **five seconds
after the preview is received** before enabling the separate confirmation.
The original SQLCipher service independently enforces its five-second
minimum from token issuance; the UI cannot bypass that server rule.

Only explicit confirmation can invoke the existing audited
`PersonnelSeparationService.CommitAsync` with one immutable operation
ID. The canonical atomic SQLCipher transaction closes the current
assignment, sets separated organizational status, stores a dated
movement, audit and idempotency receipt, and suspends linked eligible
user sessions; it does NOT physically delete the person, attendance
history or audit records. Any stale identity, changed day, invalid
token, day/month closure or previously recorded movement is rejected by
the canonical service. Logout, navigation, reason change, session/nonce
change and altered roster selection cancel pending preview and the
local confirmation delay. An ambiguous write outcome is **never
automatically retried**: the operator must consult the historical
separation archive and operations journal first.

Six C# xUnit regressions were committed before the implementation:
authorization, exact source, reason/nonmutating preview, mandatory five
seconds, forged identity, cancellation, and a lost commit response.
A separate negative-path structural contract runs with an immediately
checked exit code in BOTH Windows Core and Windows Release. The original
nine Release shards and canonical SQLCipher integration tests must
finish before building all four customer artifacts; real packaged
x86/x64 Portable/Installer acceptance and independent SHA-256 audit
must also succeed on the same final head SHA.

**Limitations:** Only one person at a time is exposed in native WPF;
the original distinct dismissal archive UI, archived identity return
and recovery, multi-person transfers, site/status administration,
remaining 69-route parity and real Windows 100/125/150% keyboard/DPI
and clean-machine UAT are still separate migration milestones. The
four customer binaries continue packaging DesktopHost/WebView2; there
is NO native production switch in this slice.


## Phase 8 — slice 6: immutable canonical separation archive in native WPF

Native **أرشيف الفصل** is a distinct manager-only read-only dialog in the
personnel administration screen. Its source of truth is the ORIGINAL
`PersonnelSeparationService.ArchiveAsync` and `DetailsAsync`, not
current roster status or a mutable UI cache. Records preserve the
historical name, number, source site, separation reason, effective local
date, actual UTC commit time and auditor identity from SQLCipher.
Search (name/number) and source-site filters use the original parameterized
queries. Every page is bounded to 50 rows, and the exact page metadata,
unique movement IDs, history fields and detail identity are verified.
The native modal exposes no update, delete, restoration or reactivation
button and issues no SQL or account mutation commands.

Authorization is revalidated before and after each SQLCipher read using
the exact original manager session nonce. Switching users or navigating
away scrubs pending page/detail operations and the displayed snapshots.
Results from an obsolete asynchronous read cannot leak into a new login
or replace historical facts. The detail view cross-checks every
immutable field, including the original separation reason and actor
ID, against the selected archive row.

Six test-first C# xUnit regressions cover forbidden roles/no session,
historical snapshots and paging, filtered search/site, delayed reads
on logout, forged detail payloads and malformed pages. A negative-path
structural guard runs independently with immediate exit checks in BOTH
Windows Core and Windows Release. The nine full Release shards,
pre-package WPF/WebView2 E2E, all four real x86/x64 package acceptances
and independent SHA-256 audit remain mandatory before merging.

**Not included:** an action to return separated individuals to active
service, which requires its own business-rule preview/commit review and
test-first migration. Customer packages still use the existing DesktopHost
WebView2 entrypoint. Native UI full route parity and clean-machine/DPI
manual approval have not been certified.


## Phase 8 — slice 7: audited native archived-person return (explicit SAME identity)

The native WPF personnel directory exposes **إعادة الفرد المؤرشف للخدمة**
only for one CURRENT SQLCipher roster row whose canonical state is
externally transferred (2), separated (3) or force-archived (4) with
ActiveFlag=false, RowVersion>=1 and a manager-only exact session nonce.
It NEVER activates a current active individual. The archived row's
immutable `PersonId`, name, service number, last-known organization
status and revision are frozen; selecting a different UI row cannot
change the intended identity. An explicit destination site is required,
and the original service checks that the destination remains active.

The WPF dialog calls **only** the original
`ArchivedReturnService.PreviewAsync` and shows the complete original
candidate list (up to 50) returned for the archived name and service
number, including each person's previous site, exit category and date.
The preview must contain exactly the selected person's existing
PersonId, name, number, version, organizational status and both
normalized name/number match indicators. No local or server-side guess
from a matching NAME alone is sufficient. A *separate unchecked
checkbox* requires the operator to certify this is the SAME person
before the confirmation button is enabled. A changed destination
invalidates the preview and clears the attestation.

Only that human-confirmed, one-time action calls original
`ArchivedReturnService.CommitReturnAsync` with the original
actor-bound preview token, the **same** PersonId, explicit `decision:
"same"` and a unique operationId. The canonical service revalidates
archived row version/status, historical assignment closure, future-day
continuity, day/month locks, conflicting active/archived numbers,
site activity, overlapping movements/assignments, and token ownership.
Its SQLCipher transaction appends a dated RETURN movement and audit
record, opens the new assignment and reuses the original PersonId. Old
attendance, identity and exit history are preserved; no duplicate
person or schema is created.

Both previews and writes revalidate the live General/Ordinary
Administration SQLCipher principal and session nonce before and after
the original service. A logout, route change or changed page invalidates
pending previews and hides stale responses. A missing or uncertain
write receipt does not auto-resend, and that same authenticated session
cannot reopen the workflow; operators must first examine the roster,
movement archive and operations audit.

Eight C# xUnit test-first cases cover role/session denial; archived
selection; explicit destination; nonmutating preview; multiple
candidate disclosure and same-person checkbox; duplicate archived number refusal; forged row revision;
stale responses; one successful write; and uncertain-response
no-retry. A dedicated negative-path source/XAML contract with an
immediately checked error status is added to BOTH Windows Core and
Windows Release. The nine Release shards, WPF/WebView2 E2E, two x86
and two x64 customer packages, real four-package acceptance and
independent SHA-256 provenance remain merge prerequisites.

**Production limitations:** This is ONE PERSON per action, NOT an
automatic resurrection of archived accounts or historical attendance
and NOT the identity correction/new-person feature. Full account
management, bulk moves, site/status CRUD, import/export,
reports/settings and 69-route parity are separate work. The shipped
customer installers and portable binaries continue using the original
DesktopHost/WebView2. Native WPF is NOT yet production-approved;
independent physical 100/125/150% DPI, accessibility, keyboard,
clean-machine and authenticated return UAT remain required.


## Phase 8 — slice 8: native manager site listing, creation and name history

The native **إدارة المواقع** dialog now uses the original
`BridgeSharedServices.ListSitesAsync(includeInactive:true)` to display
both active and retired site IDs, names and optimistic RowVersion.
The system-protected `SITE-UNSPECIFIED` cannot be renamed. A site row
can only be selected from the **current live** authenticated catalogue;
forged row references or stale page results are never trusted.

**Create:** the manager enters a validated 2–100 character name,
reviews a separate local, explicitly non-writing summary, and only a
distinct confirm press calls the canonical
`AdminConfigurationService.CreateSiteAsync`.
**Rename:** a current active site row provides immutable SiteId and
RowVersion; the new name goes through the same explicit review, then
calls original `AdminConfigurationService.RenameSiteAsync` with
`expectedRevision`. This is a local UI review and NOT a server-issued
preview token: the original canonical SQLCipher transaction is the
authoritative decision about duplicate names, protected sites, name
history collision within a day, stale row versions, and audit history.
Both actions use one operationId, matching audit receipts, server-side
session + manager role checks before and after the original service.
An ambiguous write is never auto retried, and the manager must consult
the catalogue and operations journal before any subsequent attempt.
Successful completion refreshes the administration roster catalogue.

No direct native SQLite/SQLCipher writer is introduced. Site deletion,
deactivation, automatic reassignment and historical movement rewriting
are expressly NOT implemented in this slice. The original
`RemoveSiteAndReassignCurrentPersonsAsync` remains unchanged until a
separate safety review and test-first implementation. Attendance
statuses, bulk personnel movement, Excel import and other native admin
routes remain future slices.

Eight test-first C# xUnit regressions cover the manager-only opening,
actual active/retired rows, protected site, new-site validation and
nonwriting review, exact original SiteId and revision on rename, changed
input invalidation, delayed read after logout, forged commit receipt
and no retry after unknown outcome. A negative-path fail-closed guard
runs before expensive Windows jobs in BOTH Windows Core and Windows
Release; all nine Release shards, real WPF/WebView2 E2E, all four
x86/x64 Portable/Installer package acceptance jobs and SHA256
provenance audit remain mandatory before merging.

**Production limitation:** the four customer binaries continue to
ship the ORIGINAL DesktopHost/WebView2 executable. Manual Windows
clean-machine, keyboard/accessibility and DPI 100/125/150% acceptance
and complete native 69-route parity remain required before any native
package switch.


## Phase 8 — slice 9: native attendance-status catalogue (create, edit, deactivate)

An Arabic RTL WPF **إدارة حالات التحضير** overlay is attached to the
authenticated administration page. The status list delegates **only** to the
original `BridgeSharedServices.ListActiveStatusesAsync` and shows canonical
`StatusId`, name, symbol, `#RRGGBB` color, `RowVersion` and the canonical
count used TODAY. It does not invent an inactive-status report or copy the
historical attendance definitions into native storage.

All three actions are explicitly reviewed locally before any write:
**Create** requires a 2–75-character name, a 1–5-character symbol and a
6-hex-digit color; a distinct confirmation calls the original
`AdminConfigurationService.CreateStatusAsync` with one operationId.
**Update** freezes the actual selected live row's immutable StatusId and
base revision; changes to name, symbol or color cancel a prior review;
confirmation calls original `UpdateStatusAsync` with
`expectedRevision`. The SQLCipher service independently rejects duplicate
normalized name/symbol, stale row, and same-day historical name-interval
overlaps and appends a dated snapshot/audit event.

**Deactivate** is logical, not destructive: only a live selected active
status can be reviewed; the summary includes today's canonical usage count.
An additional **unchecked checkbox** certifies the operator understands
the action removes the status from future options but does not delete
past AttendanceRecords or immutable snapshots. Confirmation calls only
original `AdminConfigurationService.DeactivateStatusAsync` with the exact
StatusId, base revision and operationId. The original SQLCipher transaction
independently **forbids deactivating the default automatic attendance
status**, revalidates version and writes the audited deactivation while
preserving existing historical records. The canonical deactivate receipt
does not expose a RowVersion; the WPF adapter reports the revision implied
by the successful version-checked original UPDATE, not a fabricated
server version field.

No write occurs on opening, typing, reviewing, navigation or changing
the selected item. Both read and write calls validate the exact live
General/Ordinary Administration role, SQLCipher session nonce and principal
before/after the original service. Closing, logout or switching pages
removes stale data and cancels previews. Uncertain write results, stale
identity or malformed receipts cause a same-session write lock; **no
automatic retry** is attempted. The operator must inspect the original
status catalogue and operations audit first.

Nine C# xUnit test-first cases cover no-session/supervisor refusal,
canonical active status projections including today's count, nonmutating
create review, exact original StatusId/version update, explicit logical
deactivation, changed field invalidation, duplicate/forged rows,
late response after logout, and forged/uncertain write response no-retry.
Both Windows Core AND Windows Release execute an immediate fail-closed
negative-path contract prior to the full C# tests. Merge still requires
nine deterministic Release shards + real WPF/WebView2 pre-package E2E,
followed by genuine x86/x64 Portable/Installer packages, four independent
package acceptance jobs and SHA256 audit **all on the final head SHA**.

**Limitations:** The native pilot deliberately does not expose raw status
deletion, a second SQLite writer, retroactive edits to any attendance
history, or protected-default bypass. Inactive status history and any
advanced status reactivation are separate future work. Remaining native
features include site retirement and personnel reassignments, batch
transfers, Excel personnel import, native reports, backups and settings,
plus remaining 69-route parity and independent Windows keyboard,
accessibility, clean-machine and 100/125/150% DPI UAT. The **four shipped
customer variants still use original DesktopHost/WebView2** until this
full production parity gate is explicitly approved.


## Phase 8 — slice 10: native WPF site retirement and safe reassignment

The original `AdminConfigurationService.RemoveSiteAndReassignCurrentPersonsAsync`
already implements an **atomic soft retirement**, not an SQL DELETE: it
deactivates a non-system site and moves only its current **open and
eligible** person assignments to the protected system site
`SITE-UNSPECIFIED` ("غير محدد"). It retains existing PersonIds,
historical site/name/identity snapshots, old dated assignments,
attendance records and prior movements; all new assignments and audit
events are in one original SQLCipher transaction.

**No destination is selectable:** unlike a standard person transfer,
site retirement has exactly one canonical destination. The native
sites catalogue now exposes a separate `إيقاف الموقع المحدد ونقل انتساباته`
command only for an actual **selected current active non-system site**,
with manager-only role and server-resolved live session nonce. System
site `SITE-UNSPECIFIED` and forged/stale list rows are denied.

A newly added **read-only canonical impact preview** method
`AdminConfigurationService.PreviewSiteRetirementAsync` reports the
original active site's immutable SiteId, name and RowVersion; exact
count of open assignments; effective local date; the fixed
`SITE-UNSPECIFIED` destination; and clear eligibility/blocking
reasons. It never writes to SQLCipher. It preflights the original
service's no-retirement rules for inactive or invalid open assignments,
same-day newly opened assignments, closed DAY/MONTH periods, and an
individual's attendance or movement on the effective date. Invalid
protected/missing/stale sites reject. Previews are advisory only;
the original commit rechecks ALL version, count, closure, assignment
and date constraints atomically, refusing change rather than rewriting
past records.

The modal discloses site name, stable ID, revision, active assignment
count, specific effective date and system destination. The operator
must separately tick an unchecked consent: **إيقاف الموقع ونقل
انتسابات جميع الأفراد إلى غير محدد مع الحفاظ على التاريخ**. Without
eligibility and consent, no commit is possible. Any stale/rejected
preview fails closed. The single explicit confirm calls ONLY original
`RemoveSiteAndReassignCurrentPersonsAsync` with actor-bound
`siteId`, `expectedRevision`, the exact preview
`expectedActiveCount`, `confirmed:true`, and one unique
`operationId`. The full canonical receipt must match original
SiteId/name, new RowVersion, exact reassigned count and date and must
report `active:false`.

No native direct SQL writer or schema migration is introduced.
The site catalogue and administration roster refresh after successful
close. Logout, page navigation, source row change and stale replies
invalidate a pending review. If a commit response is lost or malformed,
the same session is blocked from replaying the action; the operator
must inspect original sites, personnel assignments and audit events.
There is no silent retry and no workaround for a closed day.

**Tests-first:** Eight C# native view-model xUnit tests were
committed *before* implementation, covering no-session/supervisor,
protected site and forged-row denial; non-writing preview with
immutable source/version/count/fixed destination; blocked/stale
previews; explicit one-time consent; stale day/count/identity;
logout-late-response; unknown write no retry and nonselectable
destination. Two additional integration regressions in the existing
synthetic SQLCipher site-retirement suite verify the side-effect-free
count read and protected/closed-day behavior alongside existing atomic
reassignment/history tests. A structural negative-path source/XAML
contract runs with fail-closed immediate exit in BOTH Windows Core
and Windows Release before costly builds.

**Merge gate remains unchanged:** same-head green Core and Release,
nine disjoint C# Release shards (1,162+ unique tests), true pre-package
Windows WPF/WebView2 E2E, two 32/64-bit builds only AFTER tests, all
four real Portable/Installer acceptance jobs, then independent SHA-256
audit. Manual clean-machine Windows, keyboard/accessibility and
100/125/150% scaling evidence remain separate production requirements.
The four published customer packages continue to use original
DesktopHost/WebView2 until ALL native 69 routes and manual acceptance
are proven. Future native administration slices: batch movement,
import personnel Excel, reports and settings; this slice does NOT
substitute for the original person transfer features.


## Phase 8 — slice 11: native WPF internal batch transfers, selected current page ONLY

The original `PersonnelMovementService.PreviewAsync` and
`CommitAsync(external:false)` already support **1–100 PersonIds in ONE
atomic SQLCipher operation** using an actor-bound one-use preview token,
identical mutation idempotency and a single transaction for all new
movement records, effective-dated assignments and audits. The canonical
service independently validates current active status/assignment,
original stable PersonId and RowVersion, source site, destination site
activity and absence of any previous movement on the effective date;
any stale member rejects the ENTIRE batch. It does NOT rewrite an old
attendance snapshot or retroactively alter the person's earlier site.

The new Arabic RTL WPF `نقل جماعي داخلي — الصفحة الحالية` command is
available only to the live manager in administration with an
**explicit source-site filter** and active-person status filter.
Selection is NEVER inferred from the site's total count or from
unloaded pages. Only the at most 100 verified live rows in the CURRENT
visible directory page are available, all initially unchecked. The
operator ticks individual people, chooses a distinct active destination
site, and presses the separate **nonmutating canonical preview**.
Every returned person must match the selected immutable PersonId,
display name, number (ع), RowVersion, original source site and
source AssignmentId; the date must equal today, destination name must
match and the actor-bound one-use preview token must be a 64-digit hex
value. A forged, missing or extra member invalidates the **whole**
preview. Changing any selection or destination clears the old token
and the human attestation.

The preview shows every selected person's original name, number (ع)
and immutable PersonId, exact total and explicit source/destination
for review. A separate unchecked checkbox requires the manager to
certify the exact list and destination. Only after this consent does
one call to the ORIGINAL `PersonnelMovementService.CommitAsync` occur
with original one-use preview token, `external:false` and one
unique operationId; it is NEVER a loop of individual writers. The
canonical SQLCipher transaction independently rechecks the entire
batch, persists each immutable movement/assignment, and stores one
matching receipt for the whole action.

Both preview and commit validate live role, SQLCipher principal,
unchanged authenticated session nonce and the current page
identity before and after the operation. Navigation, logout,
page refresh or a changed destination/checkbox invalidate pending
previews; unknown outcome, lost response or malformed receipt locks
the same manager session from an accidental retry. The operator must
verify the roster, original movements archive and operations audit
before any new attempt. No silent resend of a possibly persisted
batch, no attendance-history edits and no direct native SQL writer.

**Test-first:** Nine C# xUnit regressions were added to the independent
feature branch BEFORE the implementation: RBAC/logout denial, wrong
source and stale/duplicated rows, manual selection of ONLY visible
people, non-writing preview, forged/missing candidate, selection or
destination invalidation, explicit one atomic write, cancellation of
late preview on logout, lost/forged receipt no-retry, and roster
replacement. The original SQLCipher
`PersonnelMovementIntegrationTests` continue to validate atomic
multi-person movement, replay idempotency, transfer after attendance
and preservation of prior monthly/attendance history. A fail-closed
negative-path structural contract executes separately in BOTH Windows
Core and Windows Release before C# tests and packaging.

**Boundaries:** This slice transfers INTERNAL personnel only;
EXTERNAL batch transfer must be treated as a separate sensitive slice
because it may revoke linked accounts/sessions. Batch selection does
not cover hidden roster pages or automatically select a site's full
strength. Site management, attendance, archived identities and
reports must not be silently affected. The shipped four customer
variants continue using the original DesktopHost/WebView2 host.
Production adoption of native WPF still requires all 69 route
equivalents plus clean-machine Windows, keyboard/accessibility,
100/125/150% DPI acceptance. The exact-head merge gate still
requires two green parallel workflows, nine disjoint C# Release
test shards and genuine WPF/WebView2 E2E BEFORE building x86/x64
Portable+Installer, four package acceptances and independent SHA256.


## Phase 8 — slice 12: external batch movement and linked-account safety

The native Arabic RTL `نقل جماعي خارجي — الصفحة الحالية` dialog adds
the original **external** operation to the separate internal batch
slice, without modifying the canonical `PersonnelMovementService`.
This is a higher-impact personnel exit: in its original ONE SQLCipher
transaction, `CommitAsync(external:true)` moves all selected active
people from their source assignment to an external destination,
marks each Person's status as externally transferred and inactive,
records individual immutable movements/assignment closure/audits,
**suspends every account linked to each exited PersonId and revokes
its sessions** using the existing
`AccountLifecycleService.SuspendAccountsForPersonnelExitAsync`.
Returning a person to active status later does **not** silently
reactivate any previously suspended account. Independent founders
with `LinkedPersonId = NULL` remain protected.

Selection is manual, initially zero and from the current visible,
authenticated source-filtered personnel page ONLY (1–100 rows maximum).
No hidden page, no entire site default, no batch written as a loop
of individual transfers. An explicit external organisation/destination
name (2–150 characters) is entered, not a native site ID. An original
`PersonnelMovementService.PreviewAsync(external:true)` issues an
actor-bound one-use token containing exactly the selected original
PersonIds, name, service number (ع), RowVersion, assignment ID, source
site and local business date. The WPF adapter and view model check
**every** returned person against its corresponding selected current
row, the token's 64-hex-digit envelope, exact external destination,
date and count. A missing member, modified row or replaced page rejects
the **entire preview**, without any write.

A visibly highlighted caution explains the real account/session
effect. Two separately **unchecked** operator acknowledgments are
required **after preview**, first for identities and destination,
second for **linked-account suspension and session revocation**.
Changing any selection or external destination clears BOTH approvals
and invalidates the original preview. Only a second explicit confirm
issues ONE original SQLCipher `CommitAsync(external:true)` request
with that preview token and a new unique operationId. The canonical
service revalidates every PersonId, row revision, source assignment
and absence of a prior same-day transfer in its own atomic transaction.
One failed member means **no one** is moved, no account is suspended
and no session is revoked. Canonical original idempotency protects
against duplicate movements at the storage layer. The WPF interface
does not automatically retry a possibly persisted batch; uncertain
writes or forged/missing receipts lock the same manager session until
the administrator reviews original movement archive, users, sessions,
roster and audit events.

Test-first: nine new native xUnit tests were committed before any
external interface/implementation, including manager-only RBAC,
duplicate/forged row refusal, explicit selection, 100-person cap,
forged identity/count/preview envelope rejection, invalidated
double approval, genuine two-person single atomic commit, stale
response after logout and uncertain no-retry. Two additional
**original SQLCipher integration tests** were committed before the
implementation: two independently linked accounts and sessions are
both suspended/revoked exactly once in a two-person batch, and a stale
second member causes a complete rollback leaving a first member's
linked account enabled. Existing integration tests still verify
single-person linked-session revocation, the unrelated founder,
historical attendance invariance and true transactional idempotency.

A separate negative-path structural contract now runs in **both
Windows Core and Windows Release**, before expensive .NET builds.
The untouched release workflow still requires both Windows workflows
green on the final PR head, all nine disjoint C# Release test shards
and real WPF/WebView2 E2E **before** building 32/64-bit Portable
and Installer (four customer variants), all four acceptance jobs,
and independent SHA-256 provenance. The manual clean-machine,
keyboard/accessibility and 100/125/150% Windows DPI gate remains
separate from GitHub CI. The four production customer variants
continue shipping the original DesktopHost/WebView2 executable
until complete native 69-route parity and manual approval.


## Phase 8 — slice 13: additive-only local Excel XLSX personnel import

A new RTL native WPF **استيراد كشف أفراد Excel** overlay can be opened
only from the authenticated manager's administration screen. A Windows
OpenFileDialog accepts a **local** `.xlsx` file (max 160 KiB, the original
bridge envelope). The bytes are passed in memory to the **original
`ExcelPersonnelImportService`**, never to a second parser, SQL command,
new schema, remote upload or untrusted database filename. Its original
`SelectAsync`, `AnalyzeAsync`, `PreviewAsync`, and `CommitAsync`
run with the **exact current authenticated SQLCipher actor** and role
verified before/after each stage; the file-picking code zeroes the file
byte array after staging.

**Additive-only guarantee.** This WPF slice always stages the workbook
with `updateExistingCustomFields:false` and commits with
`approveCustomUpdates:false`. It NEVER edits names, existing service
numbers, assignments, custom columns or past attendance for known
PersonIds. Original normalized service-number uniqueness and archived
identity checks reject duplicates into the original saved issues
report. Similar Arabic names are **only warnings**, NEVER evidence of
same-human identity, merge or a reason to automatically reopen an
archived person. An untrusted, incomplete or inconsistent source preview
is rejected before any commit. No direct native SQL writer exists.

The source parser's limit is 500 analysed rows; this initial native
pilot deliberately requires **at most 100 ACCEPTED new persons** and
displays each one before authorization. This is stricter than the
existing DesktopHost import, which still supports the original higher
limit of 500. Every rejected row and its Arabic explanation, all
proposed new sites, and **all** same-name warning candidates are visible
in the preview (at most 40 candidate groups are supported by the
original advisory API; otherwise native preview refuses to commit and
the operator uses the original DesktopHost importer). Every accepted
row's name, exact service number (ع), worksheet, row and target site
is rendered in WPF. The original parser preserves text leading zeroes,
validates numeric cells, rejects Excel formulae, detects repeated
numbers and provides original signed, one-use approval tokens.

Three separate, initially unchecked approvals are presented:
(1) review all new people and all rejected rows; (2) explicitly allow
the original import to **create any listed missing sites** in the
same atomic transaction; (3) explicitly acknowledge *all* shown
same-name warnings without merging identities. The latter two are
mandatory when corresponding sites or name hints exist. Selecting a
new file clears previous approvals and invalidates its original
one-use plan. No data is written by opening, choosing, selecting,
analyzing or previewing a workbook.

A single explicit confirm calls ONLY original
`ExcelPersonnelImportService.CommitAsync` with the signed plan token,
one operationId, `approveCustomUpdates:false`, actor's original
user ID and username. Original SQLCipher independently re-checks
accepted rows, all duplicates, custom fields, site catalog drift,
plan expiry, session-bound token and all required constraints **before
any inserts**. New PersonIds, dated identity history, assignments,
optional new sites, rejected-row report, batch receipt and audit are
committed in one original transaction, or rejected without partial
writes. The WPF adapter validates the returned original batch ID,
counts, custom-update count **zero** and site creation count.
Lost responses, stale session or forged receipts lock this manager's
same session from any automatic re-import. Review original import
history, roster, sites and audit before a new attempt. The original
application's request-idempotency remains untouched.

**Test-first:** nine xUnit regression tests were committed before
implementation, exercising role refusal, local file limits, review
without writes, independent site/name consents, duplicate and
historical-person issue handling, all accepted people displayed,
rejection of existing-custom-field updates, new-file invalidation,
late result after logout and unknown write no retry. Existing true
SQLCipher integration tests exercise all 4 original import stages,
same PersonId safety, duplicate number rejection, automatic site
creation, formula isolation, name-collision advisories, archived
person rejection, lost-reply idempotency, multi-sheet mapping and
historic attendance preservation. A new fail-closed structural guard
runs in BOTH Windows Core and Windows Release before .NET tests.

**Production and merge:** Native WPF remains a development pilot;
customer x86/x64 Portable/Installer packages still ship the original
DesktopHost/WebView2 UI with its larger importer. Merging the slice
requires the exact final SHA to pass BOTH parallel Windows Core and
Windows Release, all nine Release C# test shards, real prepackage WPF/
WebView2 E2E, then both x86/x64 package builds, all FOUR packaged
acceptance jobs and independent SHA256 audit. Clean-machine Windows,
keyboard/accessibility and DPI 100/125/150% manual UAT remains
separate. Legacy screen/route parity, export, historical import
results and custom-field updates will be handled in later slices.


## Phase 8 — slice 14: safe multi-worksheet XLSX import, single WPF workflow

After reconciling competing PRs #33 and #34, **#33 is the sole
approved native import implementation**, merged into main at
`29f14b594d23a9f185acd8bf4bc2a9c049492cac`. Do not merge PR #34
unchanged: it offers a tempting 500-addition approval while displaying
only the first 100 accepted identities, and duplicates the original
import modal/view model/adapter. Its multi-worksheet option is ported
here as an incremental change to the existing original SQLCipher
import, **without** introducing another importer.

The original WPF import overlay now has an explicit Arabic checkbox
`استيراد جميع أوراق العمل`. It defaults **unchecked**, so
first-worksheet import and all earlier callers/tests are unchanged.
The operator can opt in *before selecting* a local XLSX; the
existing `INativeExcelPersonnelImportBackend` has a safe optional
overload carrying `bool allSheets`, and an unsupported provider must
throw rather than silently import only the first sheet. The existing
SQLCipher adapter passes the flag to the **original**
`ExcelPersonnelImportService.SelectAsync` as `allSheets:true`,
which parses all worksheets using the original workbook relationships,
headers, duplicate/identity/site validation, signed actor-bound
workbook token and one-time import preview token. The adapter
independently validates the original `selectedWorksheets` allowlist
and requires every accepted, rejected and same-name-collision row to
belong to a selected worksheet.

**The critical limit is on accepted people, not merely Excel rows.**
At most 500 source rows are inspected, with every rejected row/issue
visible. The original parser now exposes all 1–500 accepted people
in its canonical `acceptedPreview` rather than silently truncating
at 100. All accepted people across ALL selected worksheets must appear
with immutable name, number (ع), site, worksheet and row before approval.
Never approve 500 additions based on a truncated 100-person display;
a file containing 501 or more data rows must be rejected without writes. The single original SQLCipher transaction
continues to enforce `updateExistingCustomFields:false` and
`approveCustomUpdates:false`: no existing PersonIds, identifiers,
site assignments or historical attendance are modified.

Changing the sheet-mode checkbox invalidates any staged original
review, one-time preview token and *all* prior unchecked consent
flags; the operator must choose and analyze the XLSX again.
Logout/navigation cancels late previews. A failed/unknown write
response is not retried automatically: inspect original import
batches/roster/audit before doing anything else. The four
production variants remain the original DesktopHost/WebView2
until full 69-route WPF parity and clean-machine acceptance.

**Test-first:** the seven original xUnit cases preceded the multi-sheet
implementation; additional 500-row boundary and truncated-preview tests
were committed before the 500-row extension (default first sheet, explicit two-sheet accepted
list, mode-change invalidation of three approvals, prevention of
truncated accepted identities, forged/custom-update refusal,
late logout response cancellation, no-session/supervisor denial).
Existing 9 unit tests for safe native XLSX imports remain in place,
alongside real SQLCipher multi-worksheet/relationships integration
tests. A new fail-closed structural negative-path contract now
executes in BOTH Windows Core and Release before all C# tests.
Exact final-head success is mandatory for BOTH Windows workflows;
nine disjoint Release test shards and real WPF/WebView2 E2E BEFORE
building x86/x64 Portable and Installer; all four package acceptance
jobs and independent SHA-256 provenance audit. Manual clean-machine
Windows, keyboard/accessibility and 100/125/150% DPI evidence remains
a separate production-gate requirement.


### Clarification — 23 November 2026: import limit

The native WPF importer accepts **up to 500 rows/names in one local XLSX**,
including an explicitly selected multi-worksheet workbook. The canonical SQLCipher
preview now returns **every accepted new identity**, not only the first 100,
so a manager can verify all names, service numbers, worksheets and sites before
individually approving creation. A workbook with 501 or more rows is rejected
without writes. Existing identities, rejected rows, and audit/idempotency
protections remain unchanged. The required first-test regressions include 500
accepted/committed atomically, 501 rejected, and a forged truncated 500-row
preview denied. This change does not relax the separate name-collision hint
safety gate: when complete collision evidence is unavailable, import is denied
rather than allowing an unseen approval.

## Phase 8 — slice 15: read-only XLSX import outcomes and rejected-row review

The native Administration window now provides a separate RTL WPF modal
**سجل دفعات استيراد Excel** after the original, unchanged import action.
It reads only the existing `ExcelImportOutcomeService.QueryAsync` and the
original encrypted SQLCipher tables. The canonical service restricts each
list and detail query to committed import batches whose `CreatedByUserId`
is the actual server-authenticated user. Consequently, this screen shows
the most recent 30 batches belonging to the currently signed-in manager;
it is not a global cross-account audit view. A selected batch displays its
saved rejected rows and Arabic reasons without reconstructing them from
the source workbook or from a transient import preview.

The adapter verifies the real manager's SQLCipher session/nonce and role
before and after each read. It checks canonical batch counts, committed
status, dates, distinct identifiers and the exact number of saved issues.
The MVVM requires that selection come from the currently displayed list,
matches the fetched immutable batch to the selection and fails closed on
truncated or mismatched evidence. Refresh, navigation, logout, cancellation
and changes to the active identity invalidate any in-flight list/detail read
and clear cached personnel/import evidence. No new database schema, ad-hoc
SQL, export, import-plan replay, or writes are introduced. Existing 500-row
XLSX import, complete approval preview and historical person integrity
remain unaffected.

Seven independent test-first xUnit regressions cover anonymous/supervisor
denial, original owner-scoped list and detail, forged batch mismatches,
duplicate batches, stale async results after logout and refresh/close
cancellation. The canonical `ExcelImportOutcomeIntegrationTests` continue
to exercise the original SQLite owner filter and rejected-row evidence.
A dedicated negative-path structural guard is required independently by
**both** Windows Core and Windows Release. The standard nine Release shards,
real WPF/WebView2 E2E, 32/64-bit Portable+Installer packaging, all four
package acceptance jobs and SHA-256 audit must pass on the exact PR SHA
before merge. Manual clean-machine 100/125/150% DPI and keyboard UAT still
blocks any claim that the experimental WPF shell is production ready.

Next slice: expose the original `ExcelImportOutcomeService.ExportIssuesAsync`
only behind an explicit audited export action, with verified local output
directory and no retry after uncertain file-write results. Then continue
native screen/69-route parity before switching production hosts.


## Phase 8 — slice 16: export committed Excel rejection evidence as XLSX

This increment completes the next route after slice 15 without replacing any
legacy production screen. The existing Arabic RTL WPF **سجل دفعات استيراد Excel**
modal displays a new explicit **تصدير الصفوف المرفوضة إلى Excel XLSX** action
only inside the reviewed details of a committed import batch owned by the
currently authenticated manager. A separate unchecked acknowledgement of
the rejected evidence is mandatory. There is no automatic file generation
while browsing the history, selecting a row, or opening details.

The native MVVM sends **only the verified original batch identifier**,
never a chosen report filename, directory or workbook body, through
`INativeImportOutcomeHistoryBackend.ExportIssuesAsync`. Its SQLCipher adapter
checks the live authenticated role/session before and after the operation
and calls ONLY the already-existing
`ExcelImportOutcomeService.ExportIssuesAsync`. That service independently
verifies `CreatedByUserId=$actor AND Status='COMMITTED'`, loads the actual
persisted rejected rows, and uses the configured and validated output
directory from settings. The canonical XLSX writer uses inline strings to
avoid formula injection, a non-overwriting new .part file, verified flush,
and a final atomic move. The original 500-name import limit and existing
person/attendance data are unchanged.

The returned receipt is accepted only when the exact selected batch,
rejected-row count, XLSX format, and an absolute output path with a matching
filename agree. A missing, late, cancelled, mismatched or failed response
is treated as **uncertain**, never retried automatically; the same manager
session must inspect the configured report directory before any new export
attempt. Closing, refresh, navigation, or logout revoke any in-progress
acknowledgement and stale UI result. A session that became uncertain
cannot silently retry merely by reopening the native modal. Output files
are not uploaded or placed inside GitHub.

The slice retains the current visual treatment and RTL layout. Seven C#
xUnit regressions were committed ahead of UI integration for authorization,
no export-on-read, mandatory explicit approval, no empty report,
exactly-one invocation, mismatched receipt, logout late results and
lost-response no replay. A dedicated fail-closed structural guard runs in
**both** Windows Core and Windows Release, on top of the full native and
original SQLCipher tests. Before merge: both workflows must be green for
the **exact PR head**, with all nine disjoint Release test shards and real
WPF/WebView2 E2E before the x86/x64 Portable and Installer packaging,
then all four packaged acceptance jobs and independent SHA-256 audit.
The separate clean-machine DPI 100/125/150%, keyboard and accessibility
manual UAT is still required before switching the customer DesktopHost.

**Next:** continue mapping original remaining routes and screen behaviors
into native C#/WPF in small test-first increments. Do not switch the
production entry point until all 69 routes and manual UAT are satisfied.


## Phase 9 — slice 1: first read-only native view of unified personnel report (screen_025)

This incremental native C#/WPF slice begins **the existing single unified
`screen_025` report inside Administration / Force Management**. It does not
introduce a second Reports section, a competing report engine, any SQL writer
or a new database table. The operator chooses exactly one person from the
currently displayed authenticated Administration roster page. The selection
must remain the same in-memory source-row object and the administrator must
retain a real, matching, authorized SQLCipher nonce/role. The screen remains
read-only.

The WPF adapter calls **only**
`PersonnelReportService.ReportAsync({personId, attendancePage, movementPage,
identityPage})`, using immutable `PersonId`, not person name or
reusable service number (ع). The original report already reads effective
historic attendance status/site snapshots, dated identity revisions,
internal/external transfers, dismissal/return movements and their original
executor attribution. It supports a bounded 100-row page for each of the
three displayed streams. The native display shows historic attendance
**name and number snapshots only when recorded**; missing source history is
explicitly shown as `غير موثق تاريخيا` and never replaced with the
person's current display identity.

The first screen slice displays complete aggregated attendance-status
counts and independently paged (up to 100 source rows at once) historical
attendance, movement/return/dismissal facts and identity changes.
Previous/Next only query the canonical service using the original
page parameters. The returned `PersonId`, page indexes, 100-row bound,
total counts, has-next flags and all visible rows are validated before
display. No selected-row list is used as an attendance source or to
reconstruct historic identity. Stale responses after logout, navigation,
a roster refresh, role/session change, cancellation or changed selected
person cannot publish an earlier person's report.

**Not yet native:** the unified report's group mode, its XLSX export,
site-history, monthly detail, revisions and full audit panes. These
remain available in the original complete DesktopHost/WebView2
implementation. Accordingly, `screen_025` is **not declared fully
migrated** within the original 69-route matrix, and the customer
x86/x64 Portable and Installer outputs still use the existing original
production host. This is a guarded functional increment, not a launch
or a claim of full UI parity.

Seven focused C# xUnit tests were committed **before** the adapter and
WPF UI implementation. They cover manager-only authorization, current
row-reference selection, original immutable person identity, explicit
missing historic identity evidence, forged identity/page rejection,
100-row attendance pagination, logout-late results and closed state
cleanup. A separate fail-closed Python structural negative-path guard is
registered independently in both Windows Core and Windows Release.

**Merge gates:** both Windows pipelines must pass on the identical final
PR head SHA, including real SQLCipher and WPF/WebView2 E2E before
packaging, the nine disjoint C# Release shards, x86/x64 Portable+Installer
builds, all four customer package acceptance jobs and independent SHA-256
audit. Real clean-machine UI/keyboard/accessibility validation at
100/125/150% DPI remains a separate manual production gate.

**Next slices:** extend the exact same report using the existing
canonical SQLCipher service to show the remaining original individual
history panels, then group mode and original XLSX report export, before
claiming `screen_025` functional parity or changing the production host.


## Phase 9 — slice 2: native effective-dated site history and attendance corrections

Builds on merged Phase 9 slice 1 (PR #38), inside the **same**
`screen_025` individual report and the **same** original
`PersonnelReportService.ReportAsync` SQLCipher source. The original
read-only report returns `PersonAssignments` with the effective-dated
site-name snapshot from `SiteNameHistory`, and append-only
`AttendanceRevisions` with original before/after status, original
effective date, explanation, authorization method, saved execution
timestamp and executor's original audit attribution. No data migration,
historical re-labeling, new SQL queries, duplicate reporting engine or
write operation was introduced.

The WPF view now includes two more independent, bounded 100-row
history lists: **previous sites / site memberships** and
**approved attendance corrections**. Each list has its own Previous/
Next controls and report parameter (`sitePage` or `revisionPage`)
while all existing attendance, movement and identity paging retains
the previous source request indices.

The native adapter verifies the current SQLCipher administrator nonce
and role before and after the original service read, checks the exact
`PersonId`, page echoes and source data. The presentation layer
independently checks total counts, next-page flags, all returned row
counts, required original historic fields and current selected-row
ownership, failing closed rather than displaying mismatched history.
Logout, navigation and a roster refresh invalidate the pending read;
the app does not present or fabricate historical evidence.

A new test-first xUnit class asserts isolated page transitions,
immutable site-name and correction executor snapshots, rejects
forged page indexes, totals, has-next flags, row counts and
unauthorized/stale roster selections, and verifies late-response
invalidation. A new fail-closed negative-path structural guard runs
in both Windows Core and Windows Release, in addition to the
previous Phase 9 tests.

**Still not fully migrated**: the original unified report's
monthly-status detail, full audit timeline, grouped reporting,
advanced report filters and XLSX export. All those remain in the
production DesktopHost until an equivalent native implementation
passes complete route/role/functional parity. Neither the 69-route
acceptance matrix nor the production packaging host is switched
by this incremental slice.

**Required PR merge gates**: success of BOTH Windows Core and
Windows Release at the same exact final SHA, all nine C# Release
test shards, pre-package Windows WPF/WebView2+SQLCipher E2E,
x86/x64 Portable and Installer builds, four package acceptance jobs
and independent SHA-256 verification. Manual clean-machine
Windows and DPI 100/125/150% acceptance remains a separate
production gate.


## Phase 9 — slice 3: original monthly status totals and individual audit timeline

Extends merged Phase 9 slices 1–2 in the SAME unified `screen_025`
native C#/WPF report under Administration. The only source continues
to be the canonical `PersonnelReportService.ReportAsync` using
immutable `PersonId`, original encrypted SQLCipher storage and real
signed-in admin role/nonce verification before and after service calls.

**Monthly status summary:** the original service returns
`monthlyTotals` grouped from every saved `AttendanceRecords` day by
`yyyy-MM`, immutable `StatusId` and historical status-name snapshot.
The native report renders all original month/status rows, not totals
inferred from the visible attendance page. It verifies that source
periods are calendar-valid, status names/IDs are present, counts are
positive, no historical month/status key is duplicated and the sum
equals the original individual `attendanceTotal`. If the original
non-paged monthly output exceeds a documented 5,000-row
client safety bound, the native view **fails closed** and explicitly
reports that the original evidence cannot be verified; it never
silently truncates a historical report.

**Original saved operations:** the same service supplies audit events
from the historic `AuditEvents` table in independently paged 100-row
`auditPage` increments. The view shows original Arabic operation,
effective date, UTC execution timestamp, original executor login/full
name snapshots, saved description and reason, not the current operator
or a reconstructed personnel identity. Audit paging retains the
positions of all other independent histories. Exact page echo,
100-row count, source totals and has-next consistency are verified
before rendering. The live authorized session and source-row ownership
are rechecked after every read, with cancellation and stale-response
invalidation on logout, roster refresh and navigation.

New xUnit tests were committed FIRST for month-status evidence,
audit source paging, original executor snapshots, forged totals
and page indexes, role restrictions, old-row rejection and late
response suppression. The original slice-one 101-day test fixture
was updated to include its expected month/status aggregate,
making cross-aggregate consistency independently testable.
Additional fail-closed Python structural negative-path checks run
in both Windows Core and Windows Release, supplementing all
existing native checks.

**Deferred (NOT native parity):** advanced report date/site/status/
movement filters, group reports, original XLSX export and its
progress/audit remain hosted by the original production DesktopHost
until separately migrated and accepted. This slice does NOT change
the 69-route inventory state or switch customer x86/x64 package
entry point to NativeDesktop.

**Required gates before merging this PR:** same final head SHA passes
Windows Core plus Windows Release; real Windows WPF/WebView2/
SQLCipher pre-package E2E, all nine C# Release test shards,
x86 and x64 Portable/Installer builds, all four real package
acceptance jobs and independent SHA-256 audit. Clean-machine
Windows keyboard/DPI 100/125/150% manual acceptance remains a
separate pre-production gate.


## Phase 9 — slice 4: native individual historical date range filter

Follows merged Phase 9 slice 3 (PR #40), adding **from/to inclusive
Gregorian local business dates** to the SAME `screen_025` individual
WPF report. No second report engine, new SQL, migration, write operation,
date rewrite, or production host redirect was introduced.

The RTL view has two explicit `YYYY-MM-DD` fields, an Apply action
and a Clear action. Empty lower/upper bounds are supported. The native
ViewModel rejects invalid/nonexistent Gregorian dates, non-ISO digits
or date notation and inverted ranges **before** contacting the backend.
Draft filters never masquerade as already applied report results:
`AppliedDateRangeLabel` shows ONLY the original returned and verified
date boundaries for the currently displayed history.

Applying a valid range resets **all** separate individual history
pagers to page 1, then requests a fresh original canonical
`PersonnelReportService.ReportAsync` read by immutable `PersonId`.
The original SQLCipher service applies date bounds (inclusive) to
attendance, historic monthly/status totals, movements, audit events
and attendance revisions according to their original effective-date
semantics. The identity-change timeline and effective-dated site
assignment history intentionally remain FULL lifetime context: the
original canonical service does not apply the date filters to those
two record streams, and the native UI states this distinction. WPF never filters only the displayed 100-row
page or recomputes historical values. The adapter verifies live
admin/role/session before/after query and exact original
`dateFrom`/`dateTo` source response echoes, person ID and
all independent pagination. The ViewModel verifies the same echoes,
current selected roster row, month-total consistency and pagination
counts before displaying the result. In-flight older range responses
are cancelled/discarded by generation; logout and route changes
invalidate any remaining response.

Seven test-first xUnit methods (including theory variants) cover valid
inclusive dates, open-ended and one-day ranges, invalid and reversed
dates, reset of every individual pager, clearing the filter to issue a
new real unbounded query, forged server echoes, stale in-flight reads,
role revocation and source-row changes. A separate fail-closed
structural negative-path guard runs in BOTH Windows Core and Release,
in addition to all previously adopted xUnit/SQLCipher/WPF tests.

**Not yet full screen_025 / 69-route native parity:** historical
site/status/movement filters, full multi-person group report,
original XLSX individual/group export and clean-machine WPF
keyboard/DPI acceptance. The packaged customer x86/x64 Portable and
Installer remain the existing complete DesktopHost/WebView2 product.
The operator must use real Windows acceptance (100%/125%/150% DPI)
before any change to the native production entry point.

The branch shall merge into `main` only after both Windows Core
and Windows Release pass at its exact head SHA, including nine
disjoint C# test shards, real Windows WPF/WebView2 + SQLCipher
pre-package E2E, four customer package acceptance jobs and
independent customer artifact SHA-256 audit.


## Phase 9 — slice 5: native movement-type filtering for the same unified report

This increments merged Phase 9 slice 4 (#41) in the existing native
`screen_025` individual historical report; no second SQLCipher query
engine, new database schema, data modification or production host
replacement. Original `PersonnelReportService.ReportAsync` already
supports `movementType`: exact canonical codes **0 = all**,
**1 = internal transfer**, **2 = external transfer**, **3 = separation**,
**4 = return to service**. The existing `PersonnelMovements` filter
`($moveType=0 OR MovementType=$moveType)` applies to the movement
count and rows only. The WPF selector explicitly labels this restriction
as **movement history only**, so attendance totals, monthly statuses,
identity histories, site membership history, revision records and audit
evidence are not incorrectly presented as movement-filtered.

The report ViewModel retains the currently committed inclusive
`dateFrom`/`dateTo` bounds, checks numeric type in the finite 0..4
range, resets only the **movement** page index and re-reads the
canonical encrypted SQLCipher service by immutable `PersonId`.
Clearing the filter performs a new real unfiltered movement read.
Applying new date bounds resets all history page indices but **preserves
the already applied movement type**, avoiding silently broadening the
operator's movement query. A separate source-verified display label
shows the filter actually represented by visible rows.

The authenticated native adapter verifies the exact source-echoed
`movementType` against the request, while continuing to validate
the live authorized admin session before and after the read.
Page/index/count checks, selected original roster-row ownership,
explicit first-person IDs and stale-response cancellation remain
fail-closed. Draft values never masquerade as an applied filter.

Test-first focused xUnit coverage adds valid canonical numeric codes,
invalid code denial before the first SQLCipher read, independent
movement paging, attendance/month integrity, precise result echo,
clearing a movement filter with retained period, preserving movement
filter when applying a new date range, unauthorized or stale
selection prevention and superseded asynchronous result rejection.
A new structural negative-path guard is run by BOTH Windows Core
and Windows Release, in addition to all earlier tests and full
Windows acceptance matrix.

**Remaining report work:** historical site and attendance-status filter
selectors backed by original evidence, full group reporting, grouped/
individual XLSX and separate Windows 100/125/150% physical acceptance.
The WPF pilot remains experimental. Customer x86/x64 Portable and
Installer still build original complete DesktopHost/WebView2; no
69-route parity claim or route matrix state change.

**Merge gate:** latest exact PR SHA must pass Windows Core and
Windows Release, Release nine xUnit test shards, actual Windows
SQLCipher/WPF/WebView2 pre-package E2E, all x86/x64 Portable and
Installer acceptance checks plus the independent four-package
SHA-256 audit. Manual clean-machine acceptance remains independent.


## Phase 9 — slice 6: immutable person site and attendance-status filters

This extends merged slices #41–#43 using the SAME read-only original
`screen_025` historical report and canonical SQLCipher
`PersonnelReportService.ReportAsync`. No schema migration, second
report writer, new route, permissions change, or production host switch.

The native report now has dropdowns for **historic site** and
**historic attendance status**, as well as Apply/Clear. The choices are
issued by the original source, exclusively for immutable `PersonId`,
and contain stable `SiteId` / `StatusId` keys with a remembered
display-name snapshot. No names are used as identity. The source looks
at the FULL lifetime of original attendance, assignments, movement
and attendance-correction evidence, independently of date/site/status
filters and 100-row pagination. Site choices also include locations
present only in movement source/destination records; status choices
include before/after correction snapshots. This avoids losing a
legitimate archived choice when filtering to a period with zero
matching attendance.

Each selector is bounded to at most 500 distinct IDs, and the canonical
service rejects requests that would require truncation instead of
silently concealing historic options. The native adapter requires
all original arrays and their PersonId scope, validates unique
nonempty IDs and bounded historical labels, matches the original
source echoes of both requested filters, and re-verifies current
manager/admin authorization before and after the canonical read.
The VM additionally checks that the requested IDs belong to the
supplied person-scoped lists, prevents stale/forged replies and resets
attendance, movement and correction pagers on filter changes.

The **site** predicate applies to historical attendance, movements
and attendance revisions. The **status** predicate applies to
attendance and attendance revisions, but **not** to movements.
Source status/monthly totals naturally follow the attendance filters.
Identity history, site membership history and the audit trail remain
unaltered by the site/status selectors, in accordance with original
service semantics. Date and movement-type filters remain active when
site/status filters are applied or cleared.

C# xUnit tests were committed before implementation for accepted
canonical IDs, unknown/poisoned IDs, clearing to a real unfiltered
service read, preserving dates and movement type, independent
pagination, stale in-flight replies, forged response echo, duplicate
source choices, live-role denial and original roster selection.
A separate fail-closed source-contract guard runs in **both** Windows
Core and Windows Release, before any customer packaging.

**Still remaining:** full group report WPF flows, individual/group
XLSX exports, route-by-route native functional acceptance and actual
clean Windows machine (100/125/150% DPI, keyboard, RTL) acceptance.
The four current customer packages remain the complete original
DesktopHost/WebView2 production app; native WPF remains a pilot until
all 69-route parity and independent manual acceptance are proved.
The branch merges only after both current-head Windows pipelines,
nine distinct Release test shards, real E2E, x86/x64 Portable+Installer
acceptance and independent four-package SHA-256 audit pass.


## Phase 9 — slice 7: read-only native WPF group history (screen_025)

This slice adds the **first actual native WPF group report** to the existing
administration register, alongside the previously merged native individual
report. Both use the **same original** `PersonnelReportService.ReportAsync`
on encrypted local SQLCipher. No new reporting SQL, persistence table,
database migration, external service, write action, file export, or new
route is introduced.

- The user opens **التقرير الجماعي** from the authenticated, authorized
  administration roster and explicitly multi-selects 1–100 individuals
  from the **current loaded roster page**. Selecting nothing never sends
  a request. A person is identified only by immutable `PersonId`,
  never the mutable name, service number or row number. Over-100 roster
  pages, malformed/duplicate IDs, unknown references and reloaded roster
  objects fail closed. Cross-page selections are intentionally deferred.
- `NativePersonnelGroupReportViewModel` owns selection, cancellable
  in-flight reads, inclusive ISO date range, bounded 0–4 movement
  filter and canonical daily status paging of 100 source rows. It
  resets the daily page on new group/filters, ignores late responses,
  wipes all sensitive rows when closed, and denies reads after session
  replacement, role revocation, a roster reload or leaving the
  administration route.
- The native SQLCipher adapter validates the live administrator session
  **before and after** calling the original `ReportAsync` with an exact
  `personIds` array. It verifies the canonical `mode=group`,
  selected PersonId order/cardinality, date/movement/page/filter echoes,
  count bounds, per-person and group attendance/movement totals and
  status totals; no current-roster summary can masquerade as historic
  evidence.
- The Arabic RTL WPF window displays the exact source person summaries,
  historical attendance and movement totals, original saved status
  totals, and paged daily status snapshots with both count and
  distinct-person count. No 100-row screen page is used to recalculate
  lifetime or period totals.
- C# xUnit tests were committed **before** implementation for explicit
  selection, rejected forged/stale/unknown identities, malformed dates,
  changing filters, 100-row summary paging, cancellation/revocation and
  group total integrity. Both Windows Core and Windows Release execute
  a negative-path group-source guard before C# build and test.

**Deliberately incomplete:** native individual/group Excel export;
cross-page group selection beyond the current 100-person roster page;
site/status group filter selectors; full 69-route native functional
parity; Windows clean-machine, DPI 100/125/150%, Arabic keyboard
and offline installed-app UAT. The existing shipped x86/x64 Portable
and Installer packages continue to include the complete original
DesktopHost/WebView2; no cutover to experimental WPF is authorized.

**Merge gate:** both Core and Release must pass at identical head SHA;
Release nine xUnit shards, Windows WPF/WebView2/SQLCipher E2E, original
four Windows x86/x64 Portable/Installer packages and their four independent
acceptance jobs, and final SHA-256 provenance audit must all succeed.
The manual clean-machine evidence gate remains separate and is never
misrepresented as a passed CI test.


## Phase 9 — slice 8 (#46): canonical group-history Excel export for WPF

This slice is explicitly **group Excel only**. The existing
`MonthlyExcelExportService` produces a fundamentally different monthly
roster and is not repurposed as a historical individual or group report.
Instead, `PersonnelGroupExcelExportService` reads the **same original**
`PersonnelReportService.ReportAsync` in its group mode. The native desktop
adapter verifies the live manager session, immutable selected `PersonId`
array and date/status/site/movement filters. No new SQL report projection,
database schema, attendance mutation, browser-selected output path or
independent business-source cache is introduced.

The operator first runs and validates a WPF group report for the current
selected roster page, then requests a separate Excel preview. Preview is
strictly read-only. Explicit confirmation supplies the same `PersonId`
order, period and filters plus the SHA-256 canonical group summary
fingerprint. The service re-reads every daily status page (100 rows per
canonical page), verifies the same source fingerprint and exact echoes,
bounds the daily report to **20,000** rows and fails rather than silently
truncating beyond that limit. It reconciles the sum of all original daily
counts against the canonical full-period attendance total; subsequent
source drift, invalid/duplicate IDs or expired authorization aborts
export. A fresh preview is mandatory after any unknown export failure;
the UI never auto-retries and cancels on navigation, session or group
selection/filters change.

The genuine offline `.xlsx` workbook has four RTL Arabic worksheets:
**الأفراد** (immutable IDs/current labels and two historical totals),
**الحالات** (group historical status counts), **الملخص اليومي**
(full bounded source-paged daily status/count/distinct-person evidence)
and **حالات الأفراد** (saved per-person historical status breakdowns).
All cells are Office Open XML inline strings, not executable formulas.
The renderer reuses `ExcelReportLayout` and
`ReportWorkbookCommit.VerifyFlushAndMove`, with the existing
`OutputDirectorySettings` local folder policy. The filename includes a
unique generated suffix, no preexisting workbook is overwritten, and
untrusted paths are never accepted from the WPF layer. Temporary partial
files are deleted on failure; interrupted post-commit authorization is
handled as a failed export and deletes this attempt's new file.

The C# test-first suite checks two-step approval, forged previews and
receipts, reordered/stale/duplicate identity selection, role and token
revocation, cancellation and no automatic retries. Another test
inspects a REAL four-worksheet OOXML ZIP, Arabic names, RTL sheet views,
unmodified immutable PersonId, XML safety and absence of any formula
cells. Both Windows Core and Release preflights execute the negative-
mutation guard before C# tests and, in Release, before all four customer
packages.

**Remaining:** individual native historical XLSX; optional historical
site/status group-filter selectors; group selection across multiple
roster pages; physical offline Windows 100/125/150% DPI acceptance and
remaining 69-route parity. The original WebView2 customer host remains
the shipping app. This source branch is safe to merge only after BOTH
exact-head Windows workflows, nine disjoint test shards, prepackage WPF
E2E, x86/x64 Portable and Installer, four package acceptances and
independent SHA-256 audit succeed; the skipped manual clean-machine gate
must never be reported as an automated pass.


## Phase 9 — group selection safety follow-up (before additional filters)

Base: `d9dfdb23e5f40d8fb169d098f67230a082cdd7dc` (merged #46).

A rejected group selection previously invalidated the displayed report and
Excel preview but retained the prior selected rows. A subsequent Run could
therefore read the previously selected individuals rather than requiring
explicit reselection. The fix clears the selection before invalidating all
in-flight results and Excel approval. No database schema, report projection,
role policy, packaging workflow or valid-selection behavior changes.

Regression tests were committed before implementation: invalid replacement
with a duplicate or foreign row after a successful report, and invalid
replacement during a pending report. They assert zero retained selections,
no additional canonical reads, no late report repaint, and successful
recovery by valid explicit reselection. This safety correction is not a
performance optimization; no measured speedup is claimed. Valid requests
retain the original query path; rejected requests must issue zero additional
SQLCipher report calls.

Both Windows workflows on the final PR head, all existing release test and
package gates, and SHA-256 audit remain mandatory before merge. Individual
historical Excel, group site/status selectors, cross-page selection, route
parity and physical Windows acceptance remain unfinished.


## Phase 9 — individual history Excel, part 1: complete-workbook renderer

Base: `ef7c6fb66e7f97378550b2b1850a3d7691c13422` (merged #47).

This is the pure workbook layer, not a connected WPF export command yet.
It projects the canonical individual report into nine RTL OOXML worksheets:
identity/summary, status totals, monthly totals, attendance, identity history,
movements, revisions, visible audit records and site membership history.
The existing group workbook's package/layout writer is reused without
changing its four sheet projections. All cells remain inline strings;
historical null identities are blank, never replaced by current identity.

Each of the six history arrays must match its canonical count in full,
with an explicit 20,000-row per-history bound and 5,000-row per-summary
bound. The status and monthly counts must both reconcile to attendance.
Missing/oversized tables, malformed fields and invalid XML text fail
before ZIP creation. Raw audit old/new JSON and current custom columns
are not included in this first visible-history projection.

Test-first xUnit cases verify real OOXML, nine RTL sheets, leading-zero
IDs and formula-like names as literal text, null historical identity,
six incomplete histories, missing tables, inconsistent monthly totals
and unsafe characters. Existing group workbook tests remain in place.
No database change, optimization claim or production package switch.
The next part must assemble and validate complete canonical source pages,
verify authorization and source stability, then integrate a separate
operator preview/confirm and atomic local file publication. No UI export
availability or full individual report parity is claimed yet.

Required: both Windows workflows on the exact final PR SHA, all automated
release gates and four-package SHA-256 audit. Physical Windows acceptance
remains separate.


## Phase 9 — individual history Excel, part 2: canonical complete-source reader

Base: `75aae47a17267a386688721c5da4ebd5616b0c57` (merged #48).

The new PersonnelIndividualExcelSource calls the existing
PersonnelReportService.ReportAsync, never an alternative SQL projection.
It requires an explicit immutable PersonId and exact date/site/status/
movement filters. In each request it advances the six history page
indices together, checking all six echoed indices, page size, counts and
has-next flags, even for streams already exhausted. The maximum stream
length determines the number of source calls per sweep. Status/monthly
totals must independently reconcile with the attendance count.

Before and after each original canonical call, a caller-supplied live
authorization callback must succeed; cancellation stops further reads.
The reader assembles the full visible projection and independently
re-reads every page to detect observed drift, including changes with
unchanged totals. SHA-256 covers the complete assembled report, not only
aggregate counts. The two sweeps are not a database transaction and do
not prove snapshot isolation under concurrent changes; confirmation must
re-read the complete source and compare its fingerprint. Raw old/new audit
JSON is excluded from exported evidence. Safety limits: 20,000 rows per
history, 5,000 rows per aggregate, 64 MiB serialized visible page evidence.
Excess or inconsistency is a failure, never silent truncation.

Test-first xUnit cases cover six independent streams with 0/1/2/100/101/
201 rows, exact row ordering, wrong person/filter/page/count/has-next,
partial pages, unchanged-count drift, summary mismatch, revoked
authorization and cancellation. No database schema or canonical SQL
change, no file writes and no UI export button yet. This is a correctness
feature, not a measured performance optimization: two verified sweeps
intentionally cost extra reads. The next slice must wire actual native
authorization, preview/confirm, full fingerprint revalidation and atomic
local workbook publication, then expose the WPF controls and receipts.

Both exact-head Windows workflows and all automated four-package gates
remain mandatory before merge. Physical Windows acceptance is separate.


## Phase 9 — individual history Excel, part 3: preview and safe publication

Base: `e07fdf9ba811dc098f702cca76fa41828f646b68` (merged #49).

The backend PersonnelIndividualExcelExportService now composes the canonical
complete-source reader and nine-sheet workbook. Preview performs authorized
reads and checks the configured local directory without creating a file.
Its approval fingerprint binds the complete report and the resolved folder.
Confirmation rereads all histories, requires the same fingerprint, checks
the local directory policy, generates a unique filename and writes only a
new temporary file. It rechecks the configured folder and authorization
before the existing ReportWorkbookCommit verifies, flushes and publishes
without overwriting. Authorization and cancellation are checked again after
publication. Failure cleanup targets only files created by this operation;
filesystem refusal can prevent cleanup, so it is not a guarantee of erasure.

Four test-first SQLCipher integration tests exercise preview without files,
real nine-sheet publication, identity isolation, changed filters and data,
revocation after publication, preservation of existing exports, cancellation
and invalid approval. No new SQL/schema, production entry point or bridge
action. The native adapter must still supply real role/session checks and
one-use approval, record export audit metadata, and connect WPF preview,
confirmation and receipt. The fingerprint alone is not an authorization
token. The service remains unexposed until those parts are implemented.
Physical Windows acceptance remains pending; no measured speedup claimed.

Both Windows workflows on the exact final PR SHA and four-package acceptance
and SHA-256 audit must succeed before merging.


## Phase 9 — individual history Excel, part 4: native authorization and audit

Base: `1e5e2a46af52a83aa9fa92e4e0e266ee50a32afb` (merged #50).
The native adapter now implements INativeIndividualExcelBackend. Preview
requires the existing SQLCipher manager-session resolver, validates the
canonical identity and filter echoes and issues a random in-memory token
bound to the precise actor/session and immutable request. The approval
expires after two minutes, is consumed under a lock before confirmation,
and is invalidated by a new preview or explicit cancellation. Reuse,
changed scope, expired approvals and concurrent duplicate confirmations
are denied. A failed attempt requires a fresh preview.

The export service accepts a publication audit callback; the native adapter
uses the existing transactional BridgeAuditWriter to record export metadata
without names, session secrets or historical row content. Audit failure
prevents a success receipt and invokes best-effort cleanup of the new
file. This is not a distributed transaction across filesystem and database.
Existing callers of the backend-only service retain their signatures via
the optional callback. No database schema or bridge action changes.

Test-first approval cases cover session/person/filter/token mismatch,
replacement, invalidation, expiry and concurrent single consumption.
The WPF view-model and controls still need to invoke the new backend and
cancel stale requests; no claim of UI availability or physical acceptance.
Both exact-head Windows workflows and all four package acceptance/SHA-256
gates remain mandatory before merge.


### PR #51 corrective architecture follow-up

Both original CI runs (45576044300 Release, 45576044256 Core) failed
the unchanged Phase 3 reader contract on `ExecuteInWriteTransactionAsync(`
inside NativeSqlCipherDashboardReader. The nine Release test shards and
pre-package Windows E2E passed but packaging was correctly skipped.
The audit transaction now belongs to PersonnelIndividualExcelExportService
.ExportAuditedAsync; the native adapter resolves the exact live principal
and delegates to that service. No direct audit/transaction call remains
in the reader. No guard or workflow is weakened. Integration regressions
cover metadata-only audit persistence and changed principal after
publication with cleanup. New full Core and Release evidence is required
on the corrected head; the original green shards are not sufficient.


## Phase 9 — individual history Excel, part 5: native WPF/MVVM workflow

Base: `9503a9547efcb7dc9020d4e2575645d6a037c4a5` (merged #51).

The existing native individual report now attaches an independent
NativeIndividualExcelViewModel when its reader implements the audited
INativeIndividualExcelBackend. A verified current report, manager session
and fully applied filters are required before preview. The RTL panel
displays the identity, six history counts and approved directory; a
separate explicit confirmation sends only the backend-issued one-use
approval token and exact immutable request. No UI file writing or SQL
is introduced. Draft/filter/report changes, paging and closing invalidate
the preview, cancel pending work and clear the sensitive result. The VM
validates the identity and all six counters against the displayed
snapshot, and the receipt against the approved fingerprint, request,
nine-sheet count and configured directory. Uncertain or failed
confirmation never automatically retries.

Five test-first xUnit cases exercise explicit confirmation, context
changes, forged preview/receipt, a late preview after closure, and
failed-confirmation retry prevention. The existing architectural guards
and Windows workflows remain unchanged. This provides the native pilot
UI path; it does not claim full parity for the 69 routes or customer
release readiness. Real Windows manual accessibility/DPI/offline
acceptance and remaining report parity (including legacy custom columns)
still need evidence. No production host switch or database change.
Both exact-head Windows workflows and four-package gates remain required.

## Phase 9 — individual history Excel, part 6: cancellation and late-result isolation

Base: `34674b1de3fe172e11495d9f6ca1f5f3551eb216` (merged #52).

Three test-first xUnit cases cover a pre-cancelled preview, a pre-cancelled
confirmation, and a late export receipt arriving after another report has
been opened. The view model now rejects an already cancelled preview
without opening a panel or calling the backend; an already cancelled
confirmation invalidates its pending approval without invoking the export
backend and requires a fresh preview. The late-receipt case protects the
existing generation guard and ensures a new preview is not overwritten.
The backend remains authoritative for live authorization and cancellation
races during publication. No database, host, workflow, or architectural
guard changes. This is a bounded correctness slice, not completion of
legacy custom-column parity or real Windows manual acceptance. Both exact-
head Windows workflows and automated four-package acceptance and SHA-256
audit must pass before merging.

## Phase 9 — individual history Excel, part 7: applied export scope

Base: `1672bfeb603e65857e81ead2c073e8d4b1ac2318` (merged #53).

Three test-first xUnit regressions cover verified applied date, movement,
site and status filters, explicit unrestricted scope, and removal of stale
scope when the report changes, closes or confirmation consumes the preview.
The native RTL confirmation panel now shows the accepted request's scope
using the verified report's filter option names and shared movement labels.
Unrestricted bounds are explicit; unknown labels fall back to their IDs,
never to an unrestricted label. The display is not derived from draft inputs.
The scope explanation preserves the existing history/filter semantics.
No backend request, authorization, publication, database, host, workflow or
architectural guard changes. Legacy custom-column parity and manual Windows
acceptance remain pending. Require both exact-head Windows workflows and all
four package acceptance/SHA-256 gates before merge.

## Phase 9 — group history: per-person status breakdown parity

Base: `208c2b389abb63c90037983f50dedfd8b5fc6a51` (merged #54).

The existing native group report now exposes the canonical per-person
historical status totals through read-only WPF row details. Selection is
of one displayed report row; it does not alter the selected report group
or execute another query. Counts and saved status names remain original
source values. The VM reconciles individual and group breakdowns for each
exact status ID and historical label pair, rejecting duplicate pairs or
inconsistent distributions even when the grand total agrees. Historical
renames are not collapsed. Three test-first xUnit cases cover immutable
person separation despite shared display numbers, forged distributions,
and duplicate pairs. No SQL, schema, export contract, CI or host changes.
Group historical site/status selectors, cross-page selection, custom-column
parity and manual Windows acceptance remain pending. Both exact-head
Windows workflows and four-package acceptance and SHA-256 gates must pass.

## Phase 9 — group historical site and status filters

Base: `f652f7614ae5cd813bc0c444b1b445a628116717` (merged #55).

The canonical group report exposes bounded historical site/status choices
for the selected immutable PersonIds, independent of date, page and active
filter. It reuses the individual SQL choice templates with parameterized
group IDs; no native SQL, schema or alternate data source is introduced.
The native adapter shares the existing strict selector parser. The WPF
screen first loads the group's report, then offers its historical choices.
Changing a draft invalidates the report and any export preview; a new
explicit run applies the filter IDs to the canonical request. Paging and
Excel carry the same applied IDs. Selection changes reset the choices and
filter drafts. Unknown choices and mismatched returned catalogs fail closed.
The site filter restricts attendance and movements; status restricts
attendance only, matching the original group service.
Three new test-first scenarios cover real SQLCipher lifetime group choices,
filter application/paging/clearing, and selection changes/foreign IDs.
No workflow or host change. No measured speedup is claimed; group catalog
reads add work and must be assessed on real datasets. Cross-page selection,
custom-column parity, and clean-machine manual Windows/DPI acceptance remain
pending. Both exact-head Windows workflows and the four package acceptance
and independent SHA-256 audit must succeed before merge.

### Compatibility correction — Release run 45627366962

The r01-excel lifecycle test failed at R01WireShapeSnapshot.Verify because
unconditional group selector fields changed the frozen legacy bridge shape.
The original two-argument ReportAsync contract now delegates with the new
catalog disabled; only the native WPF adapter explicitly opts in. This also
avoids unnecessary selector queries during legacy group exports and their
paged source reads. The integration regression checks that removing only
the two opted-in fields yields an identical legacy JSON payload. The frozen
wire-shape snapshots, lifecycle assertions, CI and package gates are unchanged.
The correction requires new exact-head Core and Release results before merge.

## Phase 9 — group report current custom-field parity

Base: `5f741b45165723209f7b77611938dcc5d4a1f088` (merged #56).

The canonical group response already contains visible current custom-field
metadata and per-person values. The native adapter now carries these
existing fields without changing the application service, legacy wire
contract, SQL queries or workbook. The ViewModel rejects inconsistent
metadata, duplicate IDs or misaligned per-person field projections.
The native RTL report shows a rank column only from an actual configured
rank field (blank if absent) and current custom fields in selected-row
read-only details. Current values are explicitly distinguished from saved
historical status totals; immutable PersonId and field ID remain the keys.
Three new test-first scenarios cover reused displayed personnel numbers,
missing values and invalid field mappings/metadata. No schema, host or CI
changes. Custom-field workbook parity and cross-page group selection
remain pending, as does manual clean-machine Windows acceptance.
Require both exact-head Windows workflows, four package acceptances and
independent SHA-256 audit before merge.

## Phase 9 — group XLSX current custom columns

Base: `ee48d405ac988ab3826dc5ac461dc710440ec55f` (merged #57).

The existing four-sheet native group workbook appends the canonical visible
current custom fields to its persons sheet in metadata order. Captions
explicitly identify current values; absent values remain blank. Immutable
field IDs map cells, and duplicate metadata, unknown IDs, malformed values,
and unsafe XML text fail closed before ZIP creation. The group export
fingerprint now includes custom column metadata as well as the existing
per-person values; changes during approval or paging abort export. The
preview verifies the projection before approval and explains current values.
Five new test-first scenarios cover actual OOXML, safe literal formula-like
text, malformed projections and metadata/value fingerprint changes.
No canonical SQL, legacy response shape, workbook sheet count, database
schema, host or CI changes. Existing fixed columns retain their order.
Individual workbook custom-column parity, cross-page group selection and
manual Windows acceptance remain pending. Both exact-head workflows and
all four package acceptance and independent SHA-256 gates must pass.

## Phase 9 — individual XLSX current custom-column parity

Base: `9a4eacbb1c4de5aff5fb427e47565cf32a0e480f` (merged #58).

The individual export now retains canonical current custom-column metadata
in its verified source header. Both complete source sweeps compare that
metadata and existing current values, and the approval fingerprint covers
both. The individual identity worksheet appends current values in canonical
metadata order through the same validated projection used by group Excel.
The other eight sheets and existing fixed columns are unchanged; current
values are never substituted for historical records. Unknown field IDs,
malformed metadata and unsafe values are rejected before workbook creation.
Three new test scenarios cover literal OOXML cells, unknown IDs and
metadata drift between source sweeps. No new SQL, schema, wire contract,
workbook sheet count, host or CI changes. Cross-page group selection and
manual clean-machine Windows acceptance remain pending. Both exact-head
Windows workflows, four package acceptances and SHA-256 audit must pass.

## Phase 9 — multi-page group selection

Base: `190a5323d4dc21abe0bab2301bf8c1f72ac2f822` (merged #59).

The native group picker reads additional pages through the existing authorized
personnel directory reader using the original site, search and organizational
status. Parent directory refresh, session replacement or an invalid page
closes the picker. The explicit selection basket accepts only verified row
references, deduplicates immutable PersonIds, preserves selection order and
limits the entire group to 100. Page highlighting alone never adds people.
Changing the basket or paging revokes prior report and Excel approval.
Observed roster total changes or incomplete pages fail closed; this is not a
transactional snapshot of concurrent roster changes. No new SQL, schema,
legacy wire response, packaging or workflow changes. Four test-first cases
and strengthened existing static guards cover multi-page selection, removal,
forged rows, limits and revoked/late results. Manual Windows acceptance is
still pending. Both exact-head Windows workflows and all four package gates
must pass before merge. Next: daily attendance mutations and monthly parity.

## Phase 10 — daily attendance reviewed-scope isolation

Base: `1eab3e4ceddf41f6acf3bde60eeb1140c6b834b6` (group selection #60).

The existing native daily single-person and bulk status changes already use
the canonical reviewed attendance pipeline. This slice freezes the parent
roster filters, search, pagination and manual refresh during a review,
including an uncertain commit, rather than introducing a second writer.
Only a verified successful receipt triggers the internal refresh; the
receipt remains visible until the operator closes it. Logout and changed
session still clear sensitive state. Three test-first scenarios cover
scope freeze, verified receipt refresh and uncertain-response replay using
the same operation ID. The existing fatal static guard is strengthened.
No SQL, schema, application service, host or packaging workflow changes.
Remaining: verify native closed-day exception and initial/revision parity,
monthly lifecycle controls, and manual Windows acceptance. Both exact-head
Windows workflows and all four package gates are required before merge.

## Phase 10 — initial versus revised daily attendance

Base: `7e536647549031c275462d40a5b8435c85966766` (merged #61).

The native daily roster now carries the canonical null saved status and
transfer-day flag; initial attendance is not inferred from an Arabic label.
Only homogeneous non-transfer selections enter the existing reviewed
pipeline. Initial preview uses mode initial and SubmitAttendance; revisions
retain mode revise and ReviseTodayAttendance. Preview validates the mode
and previous revision/status; the commit response must match the operation
mode. No automatic split, fallback from initial to revision, or new writer.
The native dialog labels the actual operation. Three new regression cases
and strengthened existing fatal guards protect initial submit, mixed-group
rejection and incorrect preview mode/previous status. Existing revision
entry points remain compatible. Both exact-head Windows workflows and four
package acceptance/SHA-256 gates are required before merge.
Closed-day and monthly lifecycle parity and manual native Windows acceptance
remain pending. No database, SQL, legacy response or packaging changes.

## Phase 10 — single closed-day correction, review foundation

Base: `566bdf3ed1d73ab6ef369fee4a2694f036c72da1` (merged #62).

The native presentation now contains a typed single-day correction review
state and an explicit adapter to the existing ClosedDayCorrectionService.
Only an authorized administration principal and the exact current historical
cell with saved attendance can initiate this review. A 10–400 character
reason, matched preview, explicit confirm and stable operation ID are
required. The canonical service retains authority for the enabled policy,
closed day, historical assignment, revision and audit/idempotency checks.
The adapter checks live principal and action permission before and after
requests and rejects range-shaped previews. Four new xUnit regressions and
stronger existing preflight guards cover role, context, preview identity,
reason, receipt and uncertain-response replay. No SQL, schema, policy switch,
legacy wire contract or release workflow change.

This is the foundation only: no correction button or monthly-page lifecycle
binding is exposed yet. Next: wire the monthly view, active status catalogue,
session/navigation invalidation and receipt refresh, including late-response
and unverified-intent safeguards, before enabling the workflow. Historical
range and monthly lifecycle parity remain separate. Both exact-head Windows
workflows and four-package acceptance/SHA-256 gates must pass; manual Windows
acceptance is still pending.

## Phase 10 — single closed-day correction in native monthly WPF

Base: `6193e13669669ffa59818f39d5b27f651bedc9e2` (merged #63).

The selected authenticated historical cell now opens the guarded single-day
review using the original active status catalogue and correction service.
The WPF detail view shows the reason, canonical before/after preview and
explicit confirmation. The parent period/site/search/page and navigation
remain frozen during review, including an uncertain commit. The same
preview token and operation ID are retained for explicit replay; closing a
verified receipt refreshes the current archive scope. Logout/session reset
revokes local review state and late status results. The canonical service
still decides whether the policy is enabled and the day is actually closed;
visibility of the candidate button is not authorization to write.
Two new monthly integration regressions and strengthened static preflight.
No SQL, schema, canonical policy, legacy response or packaging changes.
Historical range correction and monthly lifecycle parity remain pending,
as does manual acceptance on clean Windows. Both exact-head workflows and
all four package acceptance/SHA-256 gates must pass before merge.

## Phase 10 — historical range atomicity prerequisite

Base: `218dcbb28e6063648392d2ec67fe790b8e3d3ce4` (merged #64).

Before exposing historical range correction in native WPF, inspection found
that the original ClosedDayCorrectionService consumed its token and wrote
individual days before validating subsequent days. ExecuteInWriteTransactionAsync
commits a normally returned BridgeResponse, even an error response, so a late
stale day could commit preceding days while reporting failure. This slice
validates every sealed day within the same write transaction before token
consumption and any mutation. It checks chronological uniqueness, bounds,
revision/existence, assignment/site and status snapshots. The subsequent
mutation loop uses the verified states; concurrency write failures still
throw and roll back. No changes to the original allowed range, roles,
policy, SQL statements, schema, wire format, legacy UI or package workflow.
Two SQLCipher integration cases exercise a changed final-day revision or
status snapshot, proving the expected rejection without earlier inserts,
audit events, receipts or consumed token, followed by fresh successful
preview/commit. The existing fatal Windows preflight protects validation
ordering. These tests require CI confirmation; native range UI and monthly
lifecycle integration remain next, after the prerequisite passes.
Manual Windows acceptance is still pending. Both exact-head workflows and
all four package acceptance/SHA-256 checks are mandatory before merge.

## Phase 10 — native historical range contract and canonical adapter

Base: `9852c8bf87256e93ee3f64dbd11d8de8ad5feb36` (merged #65).

Add a distinct typed native historical range boundary for the existing
ClosedDayCorrectionService range mode, preserving the original inclusive
62-day limit, allowed end date (today), and canonical role/transaction rules.
The native request requires a clear reason; the preview validates person,
range, target status, reason, ordered unique actual changed days, previous
revision/existence, historical assignment/site and revision checksum. Days
omitted by the canonical service remain omitted, not treated as writes.
The original range receipt carries the start date and the final changed
day's revision (including revision 1 for a newly prepared day), not the
number of changed days. The adapter validates that exact receipt and passes
only the original token and operation ID to the canonical writer.
Four test-first contract regressions and strengthened existing fatal guards.
No SQL, schema, canonical service, legacy response or workflow changes.
This slice does not expose the range operation to the WPF page yet: the
next slice wires reviewed period selection, per-day preview, same-intent
retry, session revocation and refresh. Manual Windows acceptance remains
pending; both exact-head workflows and all four package gates are required.

## Phase 10 — native historical range review UI

Base `e4498fb7fcae7c77be7137595e3b159e4b4ffce3` (merged #68).
Connect the existing range contract and canonical adapter to the monthly cell details panel. The distinct period review supports 1–62 days up to today, including unprepared days when the canonical service permits them. Immutable PersonId and live authenticated identity anchor the operation; historical name/number changes across dates are allowed and canonical preview identity is displayed. Preview lists only eligible changed days with dates, historical sites, before/after status and omitted-day count. Explicit confirmation, same-operation retry on uncertain outcome, cancellation/late-response isolation, mutual exclusion with single-day correction, frozen scope/navigation and one refresh after verified receipt. No separate database writer, policy/schema change, legacy UI change or package workflow change. Four lifecycle tests plus one monthly integration test and strengthened fatal static checks. Default development build must pass both workflows, real x86 Portable acceptance and independent SHA; full four-package acceptance and physical Windows UAT remain required for final release.

## Phase 10 — administrative seven-day missing-attendance catch-up foundation

Base `c9240727f71cfbffc84584a1cf428b6ec8d2567c` (merged #69). Verified the historical range missing-record sentinel against the actual SQL projection: `__UNPREPARED__`.
The original administration settings expose a separate catch-up command for one of the seven previous local dates. It is distinct from per-person correction and daily submission; it considers all eligible historical assignments and must never overwrite committed attendance. This slice adds typed request, preview and receipt validation plus an explicit native adapter to the existing AdministrativeAttendanceCatchUpService. Both manifest actions require the actual authenticated administration identity and session revocation checks before and after the service call. The request requires a trimmed 10–400 character reason. The preview must echo date/reason, exact token and fingerprint, eligible and pending counts, explicit confirmation. Commit submits only approved token and stable operation ID; receipt must match date and pending count, with 0–pending actual applied records and canonical summary. Preserve canonical exact idempotency replay after date expiry; no local policy substitute or database writer. Four unit regressions and extended existing real SQLCipher preview/receipt/replay assertions, plus fatal static guard. No UI entry point yet: next wire an independently guarded review and authorized administration/settings control, including all-sites scope warning and session/uncertain-receipt handling. No new schema, dependencies, workflow or release distribution changes. Both development workflows and real x86 Portable acceptance/SHA remain required. Full release and physical Windows acceptance are not established.

## Phase 10 — administrative catch-up native review and settings entry

Base `18dd7285db216a8b35cafa18465854d69409af63` (merged #70). Enable the original administrative seven-day catch-up in the native settings page with an explicit all-sites scope notice, date and reason, authoritative pending/eligible preview, separate confirmation and actual applied count/summary. The same approved operation ID is retained for uncertain receipts; logout/session changes clear and cancel the review; navigation is blocked until it closes. Only real administration roles may open the review; the adapter still revalidates manifest authorization and canonical SQLCipher service responses. Other settings remain disabled. Refresh dashboard on return so prior counters are not silently reused. Four unit regressions and fatal static checks, no schema, service, dependency or CI changes. Native real-machine acceptance and full four-package release remain outstanding.

## Phase 10 — explicit closed-day correction permission review foundation

Base `062ee00730754e0ab0f86e1ce45fbccc6ba950b8` (merged #71). The original administrator settings include a separate enable/disable action for exceptional correction of closed days. Add a typed native setting request and local two-step intent review, not a server preview. Preserve optional trimmed reason (when nonempty, 10–400 characters), canonical manifest authorization, explicit target and operation ID, verified boolean receipt, frozen uncertain intent and exact retry, cancellation and session isolation. The adapter calls the existing ClosedDayCorrectionService.SetPolicyAsync only after explicit confirmation, with real principal and revocation checks; the existing audit and SQLCipher receipt remain authoritative. A receipt describes the last confirmed operation, not proof of current setting state. Four unit regressions plus strengthened real SQLCipher replay receipt test and fatal static guard. No automatic policy change, new SQL, schema, dependencies or CI change. No UI entry yet; next bind enable/disable controls and keep policy review mutually exclusive with catch-up, navigation and logout safeguards. Development Core/Release and x86 Portable acceptance remain required; full release and physical Windows acceptance remain outstanding.

## Phase 10 — closed-day policy controls in native settings

Base `c5be8ce595f98f1c1b120732029646d796cb7c56` (merged #72). Connect explicit enable and disable commands to the existing reviewed policy intent and canonical authorized adapter. Show optional reason, explicit local review and confirmation, actual receipt for the last confirmed operation (not a live policy status), and exact-intent retry when outcome is uncertain. Prevent opening either settings review while the other is open; block navigation out of settings while a review remains open; clear both on logout/session revocation. Two new settings integration unit regressions and strengthened fatal contract guard. No automatic change to the setting, no additional database writer, schema, dependencies or CI change. Other settings remain pending. Require green exact-head development Core and Release, actual x86 Portable acceptance and independent SHA256; full four-package and physical Windows acceptance remain separate.

## Phase 10 — distinguish live current month from explicit current-month archive

Base `3835630104a41ab7f49ef971ce041a620ff124a0` (merged #73). The original monthly UI has distinct live-current and explicitly selected archive modes, even for the same current month. The canonical SQL query excludes externally moved/separated personnel only in live-current mode. Native WPF previously inferred archive mode solely from year/month, losing access to the same-month historical population. This slice adds an immutable explicit archive request flag, separate current-live/current-archive commands and visible mode label; selecting other catalogue periods is archive mode, and returning to live month is explicit. Preserve selected site/search, reset page, invalidate prior reads/overlays, reject stale session, freeze during correction and active Excel export. Existing historical export semantics remain unchanged (canonical export always uses archive scope). Two new unit regressions and strengthened mandatory static guard. No SQL, schema, dependency, service or CI changes. Phase 10 still requires review and successful CI before closure; do not start Phase 11 without notifying the operator. Full customer release remains gated separately.

## Phase 10 — preserve historical monthly cell color and movement annotation

Base `1ecbbc9e43ae05856511ab95fda7913a761c0dd8` (merged #74). During final monthly parity review, the native grid was found to discard historical color snapshots and the canonical movement kind. Keep the original SQLCipher grid response; preserve valid opaque RGB status colors with contrast-aware foreground, expose return/internal transfer/external transfer/separation markers from the explicit canonical movementKind only, retain description and detail click, and leave unrecorded cells empty. Do not use today's mutable status catalogue or infer movement from labels. Reject invalid color as neutral display. Test-first theory cases and a grid projection regression, strengthened existing fatal preflight guard. No write path, schema, services, dependency or package workflow changes. Development acceptance remains required on the exact head. Phase 10 closure must be reported to the operator before Phase 11 starts; full parity and physical Windows acceptance remain separate final gates.

## Phase 10 — direct historical person/period correction foundation

Base `64baaa2c4a8ed947c9a03002b4b8e5282c5bc1fe` (merged #75). Legacy administration supports direct name/service-number lookup and reading an inclusive period before historical correction. Native correction previously required a monthly grid cell. Add a bounded historical person search (up to 50 canonical candidates, including archived persons), a typed complete 1–62-day read-only period projection from the existing individual report service and strict identity/date/filter/count/revision/duplicate validation. A missing saved record is explicitly displayed as absence of evidence, not eligibility for mutation. Extend the same existing reviewed range operation with a separate reference-bound period context; preserve existing monthly-cell entry, freeze read dates for the new context, reject stale or foreign evidence, keep explicit canonical preview, confirmation and exact operation replay. Test-first coverage for search/request/evidence and context-bound confirmation; strengthen mandatory static guard. No new SQL, writer, schema, dependency, workflow or policy change. UI search/results and settings entry are the next slice; Phase 10 is not yet closed. Full parity, physical Windows acceptance and four customer packages remain final gates. Stop before Phase 11 and notify the operator.

## Phase 10 — direct historical search and period review lifecycle

Base `028169204ba624acb191330752608d3b295d9689` (merged #76). Add a native read-only review view model for the already-authorized canonical historical reader. Require a live administrative identity, explicit search of 1–100 trimmed characters, at most 50 distinct valid candidates, reference-bound selection, strict ISO dates and a complete 1–62-day evidence response. Invalidate evidence on search, candidate or date edits; reject stale, malformed or mismatched responses; clear and cancel on session teardown. Missing attendance remains evidence absence, not authorization to correct. Test-first lifecycle coverage and stronger mandatory static guard. No additional writer, SQL schema, dependency, authorization policy or CI workflow changes. This is an intermediate presentation slice: settings entry, WPF search/results controls, mutually exclusive review coordination, and connection to the existing range-review confirmation remain to be completed in the next slice. Require exact-head Windows Core and exhaustive Release, real development x86 Portable acceptance and independent SHA-256. Do not start Phase 11 before Phase 10 closure is reported. Four final customer packages and physical Windows acceptance remain separate final gates.

## Phase 10 — direct historical correction in native WPF settings

Base `9c64ccb8eec06a2f661fdc404158d08fa999e058` (merged #77). Connect the canonical historical reader, correction catalogue and existing range operation to the authenticated native settings page. Add explicit name/service-number search, archived-person candidates, inclusive 1–62 day evidence table, frozen selected identity and dates during correction, status and mandatory reason, canonical per-day preview and distinct confirmation. Missing saved records are not eligibility. Prevent concurrent catch-up, policy and historical reviews; preserve existing shell navigation freeze and clear both reviews on logout or session revocation. Keep uncertain commits locked for exact-operation retry; invalidate old evidence and reread only after a verified receipt when closing correction. Test-first settings integration coverage and strengthened fatal static guard. No duplicate SQL writer, schema, dependency, role policy or CI workflow change. Require exact-head Windows Core and exhaustive Release, real development x86 Portable acceptance and independent SHA-256. Review remaining Phase 10 parity and notify the operator before starting Phase 11. Final four customer packages and physical Windows acceptance remain distinct final gates.

## Phase 10 — direct historical evidence freshness before correction

Base `9634c8217abadd93115a0a8b90027095d490ed1c` (merged #78). Final parity/safety review identified that a direct historical period read and the later canonical correction preview can observe different saved attendance revisions. Compare the saved-record presence and previous revision of each actually proposed day with the exact read evidence. Do not infer eligibility for omitted or missing days, and do not alter the monthly-cell entry or canonical authorization, transaction, token or idempotency rules. A mismatch discards the preview, blocks confirmation and further preview of that stale evidence, and asks the operator to close the review to reread the period; old evidence is invalidated before reload. Exact successful receipt refresh and uncertain-operation retry remain unchanged. Two theory cases and one settings integration regression, stronger mandatory static guard. No new database writer, schema, dependency or CI workflow change. Require exact-head Windows Core, exhaustive Release, real development x86 Portable acceptance and independent SHA-256. Phase 10 closure remains subject to parity review; report to operator before Phase 11. Four final customer packages and physical Windows acceptance remain distinct final gates.

## Phase 10 — direct single saved closed-day correction

Base `909c60d1f97f7ca13d47fcf70c9aef34d3e8ece6` (merged #79). Comparing the original `admin-settings.js` closedCorrection screen found a distinct direct single-day preview/commit action in addition to the historical range operation. Restore that action in native settings for exactly one past date with a saved record, using the existing NativeClosedDayCorrectionReview and canonical single-day service, not the range writer. Bind the exact period evidence and live identity, verify canonical preview request, saved revision, historical site and status, display canonical name/number and before/after state, require explicit confirmation and retain same-operation replay. Missing records and today cannot enter single-day correction; the original service still enforces policy, closed-day and movement eligibility. Freeze the read context, prevent overlapping range/catch-up/policy reviews and navigation, clear on logout, and invalidate/reload after verified receipt or stale-preview rejection. Three test cases and strengthened fatal static guard. Existing monthly cell path, SQL schema, dependencies, authorization policy and CI workflows unchanged. Require exact-head Windows Core and exhaustive Release, development x86 Portable acceptance and independent SHA-256. Administrative navigation parity and physical native acceptance remain to be reviewed; no Phase 11 until operator notification. Final four customer packages remain a separate gate.

## Phase 10 — administrative navigation to historical correction

Base `8e41af0bbdc28842ea567ecdf79af68fcaec082b` (merged #80). The original administration toolbar includes direct "تعديل تمام سابق". Restore an equivalent entry in native administration, routed through the shell's existing navigation and the same settings historical review, not a second correction screen or writer. Require the live authorized administration role, current administration page, connected historical services, no open personnel review or unresolved identity commit, and existing shell navigation guards. Requery on review, page and authentication changes; deny supervisors and unauthenticated sessions. Historical review still freezes navigation, clears on logout, and uses exact evidence and separate single-day/range confirmation. Test-first coverage for both administration roles and supervisor denial, logout, profile and group selection protection. Existing mandatory static guard strengthened. No SQL, schema, dependencies, role policy, legacy UI or CI workflow changes. Both exact-head Windows workflows, real x86 development Portable acceptance and independent SHA-256 required before merge. This addresses the identified administrative entry-point gap; manual native Windows parity/visual acceptance and final four-package release remain outstanding. Report Phase 10 status to operator before starting Phase 11.

## Phase 11 — audit log read foundation (screen_050)

Base `30a7f29bf400539a41e3027cec7d6f66f9a5e1c0` (merged #81). Operator approved continuation from Phase 10. Start with the canonical UserAuditService read path, matching the original Phase 11 audit-history acceptance suite and action manifest. The native audit filter supports general search, actor, target, operation, inclusive UTC date filters, and explicit hidden-entry visibility. Preserve original role policy: GA/OA/Supervisor may read visible entries; only GA/OA can include tombstoned entries. Use the existing AuditOverlayService with the verified durable principal before and after query; no new SQL, no overlay writes, no replacement evidence. Preserve canonical actor snapshot, visible/raw descriptions, operation, target, timestamps, outcome, hash and overlay version. Enforce fixed 50-row pages, bounded requests, echoed page/count, sequence, uniqueness, total and hidden-row validation; inconsistent concurrent counts fail closed rather than silently display partial results. Test-first six cases (including three role cases); strengthen the existing mandatory Windows monthly/audit contract guard. No schema, dependency, policy, workflow or legacy UI changes. This is the typed read adapter only; next wire authenticated read-only list, search/filter/paging state and WPF navigation, then entry details/hash-chain and overlay history, then separately authorized preview/confirmation for presentation edits and hiding, plus remaining original account-management operations. No change to 69-route migration matrix until full parity and physical acceptance. Both exact-head Windows workflows, exhaustive tests, development x86 Portable acceptance and independent SHA256 required before merge; four final packages and manual Windows acceptance remain separate.

## Phase 11 — native audit list, explicit filters and navigation (screen_050)

Base `0706f0a5318a0aa4e2e970e4726fbe6804a32da9` (merged #82). Add a seventh native pilot navigation section for the original audit list; existing six section identities and numeric values remain unchanged. Authenticated GA/OA/Supervisor may read visible entries; only the two administration roles can opt in to hidden entries. Bind the existing INativeAuditReader, no independent SQL or writer. Provide RTL read-only WPF table, canonical sequence, operation, actor snapshot, target, visible description, execution timestamp, outcome and hidden marker; search by term, actor, target, operation and UTC date bounds. Draft changes cancel reads and invalidate rows; explicit search resets to page one, paging uses validated 50-row canonical pages, malformed responses fail closed, and late responses cannot repaint after filter, session or navigation changes. Clear audit rows and filters on leaving page, logout or revoked session. The audit shortcut respects existing shell navigation guards and does not leave an open personnel review. Test-first five list cases and two shell navigation cases; extend the existing mandatory guard. No SQL, mutation, schema, dependency, authorization, CI workflow or legacy route changes. The 69-route matrix remains unchanged pending full parity. Next: read-only entry detail and verified hash-chain evidence, then authorized overlay history and separate edit/hide confirmation. Require exact-head Windows Core and exhaustive Release, x86 development Portable acceptance and independent SHA-256 before merge; four final packages and manual acceptance remain separate.

## Phase 11 — canonical audit entry details (screen_050)

Base `f8198f7dbf3576519830c049b9be61281bd08836` (merged #83). Add a typed read-only detail contract over the existing AuditOverlayService.GetAuditEntryDetailsAsync. Verify the durable session before and after reading, reject hidden entries for supervisors even if the list row was formerly visible, validate contiguous overlay revisions and exact selected row identity, immutable event hash, operation, target, original description, reason, timestamps and presentation revision. Do not expose full overlay history yet. Preserve canonical before/after JSON, actor and target identifiers, correlation ID, current/prior hashes (including GENESIS), and the canonical chainValid result; false is explicitly a warning, never reported as successful verification. Require the reference-bound current row; discard stale detail results after filter edits, paging, close, navigation or session change. Show an RTL read-only detail panel without changing the parent list, with explicit close. Test-first five cases and stronger mandatory Windows guard. No writes, new SQL, schema, dependency, authorization policy, CI workflow or legacy route changes. Next: separately authorized overlay history, reviewed edit and hide operations and remaining account management. Both exact-head Windows workflows, exhaustive tests, development x86 Portable acceptance and independent SHA256 required before merge. Physical Windows parity, full migration matrix and four final customer packages remain separate gates.

## Phase 11 — reviewed visible audit description edit (screen_051)

Base `61a899dc1e6feab10fd61e1818a6421ccb3ca3dc` (merged #84). Restore authorized visible-description editing from the exact current audit detail. GA/OA only; supervisor denied. In this slice only visible entries with a valid canonical chain result are editable; hiding and hidden-entry history are separate slices. Require changed trimmed description (up to 2000 chars), mandatory trimmed reason (up to 400 chars), local explicit preview of exact before/after evidence, then distinct confirmation. The original service has no preview endpoint; canonical expected overlay version is checked in its transaction. Invoke AuditOverlayService.UpdateAuditPresentationAsync with the original version and frozen operation ID; no duplicate SQL or audit writer. Verify target ID, distinct immutable meta-audit ID, exact before/after states, next version and rawEvidenceUntouched receipt. On ambiguous response freeze intent and retry only the same operation; definitive matching stale-version response requires reload. Block navigation, filter edits and detail closure during review; clear session-local state on teardown and reload list after verified receipt or stale rejection. Five test-first cases including page integration and strengthened mandatory guard. No schema, dependency, role policy, legacy or CI workflow change. Next: separately reviewed hide action and authorized presentation history, then account management. Both exact-head Windows workflows, all test shards, real x86 development Portable acceptance and independent SHA256 required before merge. Four final customer packages and physical native acceptance remain separate.

## Phase 11 — reviewed audit entry hiding (screen_052)

Base `7941d610a318489b70876236c95714aa11562ba5` (merged #85). Restore the distinct authorized hide action using the existing shared native overlay review rather than duplicate state or database writers. GA/OA only, exact current visible row with verified canonical chain and expected overlay revision. Require a reason and explicit local preview then confirmation; the description cannot be edited during hide. Invoke the canonical AuditOverlayService.DeleteAuditPresentationAsync with the same frozen operation ID and expected revision, omitting correctedDescription; verify exact before and after state, unchanged description, next revision, tombstone flag, distinct meta-audit receipt and rawEvidenceUntouched. Preserve immutable AuditEvents. Prevent overlapping edits, navigation, filter changes and closing during unverified commits; retry only the exact original operation, clear on session teardown, reload after verified success or definitive stale rejection. Three added regression tests and stronger mandatory guard. No schema, dependency, SQL, authorization policy, legacy or CI workflow changes. Next: dedicated authorized overlay history (screen_053) with original and before/after states, then account management and full parity review. Both exact-head Windows workflows, exhaustive test shards, real development x86 Portable acceptance and independent SHA256 required before merge. Four final customer packages and physical acceptance remain separate.

## Phase 11 — native modified and hidden audit history (screen_053)

Base `5462367103393589513136a16c77c94b31d58ae6` (merged #86). Read all immutable presentation overlay versions through canonical AuditOverlayService.QueryAuditPresentationChangesAsync. General and Ordinary Administration only, real session verified before and after query. Global history and exact selected-entry history, 50-row pages, original description, before/after description and visibility, version, operation, editor identity, UTC timestamp and reason. Validate page/count, request scope, unique entry/version keys and exact before/after state shape, including unchanged description on hide. Read-only RTL grid, no writes or duplicate SQL. Cancel and scrub pending history on close, navigation or session teardown; prevent overlapping mutation review and list filter changes. Original endpoint does not supply total/hasNext: a full page permits probing next page, which may be empty; no invented total. Five new regression cases and expanded mandatory guard including XAML parse. No schema, dependency, authorization policy, legacy or CI workflow changes. Next: native account management and permissions, then comprehensive parity review. Both exact-head Windows workflows, exhaustive test shards, real development x86 Portable acceptance and independent SHA256 required before merge. Four final customer packages and physical acceptance remain separate.

## Phase 11.08a — native account list and permission foundation

Base `94153c40b56d71971f26d34a7f74385519904f0c` (merged #87). Add a dedicated eighth native navigation section for user accounts, preserving existing seven enum values and canonical legacy routes. Use UserAccountManagementService.ListAsync and real durable principal checks before/after read. General Administration sees its permitted account list; Ordinary Administration sees supervisors only; Supervisor cannot navigate or query. Validate exact page, total/count, distinct IDs, ordinal indexes, role scope, immutable founder/person linkage, row revisions and exact canModify against policy (including self restriction). Show role, identity, active flag, creator, creation timestamp, allowed creation roles from the central policy and 50-row pagination. Search by name, username or service number. Cancel and scrub stale data on query changes, navigation and logout. No password, hash or credential fields in the read contract. Read-only Arabic WPF grid and scrollable navigation. Six regression cases plus three navigation role cases and mandatory guard. No new SQL, schema, dependency, role policy or workflow changes. Next: 11.08b eligible active-person search and reviewed account creation with PasswordBox, then 11.08c password change and deactivation/reactivation with optimistic revisions, canonical idempotency and session revocation. This is a partial implementation of 11.08, not full native account lifecycle parity. Both exact-head Windows workflows, exhaustive tests, x86 development Portable acceptance and independent SHA256 required before merge; physical parity and four final customer packages remain separate.

## Phase 11.08b — native reviewed account creation

Base `c932b88440c78026373002f2797a61d1b2b34b6c` (merged #88). Reuse the canonical active-person search (activeOnly:true, at most 50 results) and UserAccountManagementService.CreateAsync. Require a reference-bound current active roster selection, stable PersonId, username, role from central authorization policy, explicit non-secret review, and matching passwords validated by PasswordPolicyValidator. General Administration can create Ordinary Administration and Supervisor; Ordinary Administration can create Supervisor only. Never create another founder. Use unbound WPF PasswordBox controls, clear them before awaiting the service, on hiding/unload and on completion; no password fields in the ViewModel or reviewed intent. Pass credentials only to the canonical call and remove password fields from the transient request afterward. Existing canonical service checks active roster eligibility and username/person uniqueness transactionally, performs Argon2id hashing, and appends audit and idempotency receipt. Verify exact target, username and role on response. On ambiguous result preserve the operation ID and non-secret intent, require re-entry of the original password for canonical replay; a changed payload cannot be treated as a success. Block navigation and conflicting queries during review; scrub data on session teardown and reload the list on verified success. Six added regression cases and mandatory guard with XAML parsing. No schema, SQL, dependency, role policy or CI workflow changes. The search candidate revision is displayed as evidence in the typed model; the canonical create service revalidates active eligibility and stable linkage, not a compare-and-swap on the roster revision. Next: 11.08c password change and deactivate/reactivate with optimistic account revisions and session revocation. Physical acceptance and final four customer packages remain separate; require both exact-head Windows workflows, all test shards, development x86 Portable and independent SHA256 before merge.

## Phase 11.08c — reviewed native account lifecycle

Base `29e7f39222693ab4eae3d319a982129f1dfc4e8b` (merged #89). Connect ChangeUserPassword, DeactivateUserAccount and ReactivateUserAccount through canonical UserAccountManagementService; no new SQL, credential storage, schema, dependency or authorization policy. Bind each action to the exact current account row, live principal, expected revision and role, prohibit self mutation, and protect the unique founding administration from activation changes. Change password only for active accounts, using unbound PasswordBox with confirmation and existing password policy; clear controls before awaiting, on hide/unload and completion. Disable active accounts; reactivate inactive linked accounts only after explicit reason (5–400 characters) and canonical active roster/assignment and uniqueness validation. Preview then confirm; canonical services audit changes and revoke prior sessions for password change/deactivation, without restoring revoked sessions on reactivation. Verify exact user ID, next revision and activation state. Freeze the operation ID and intent for retry on ambiguous outcome; a rejection after an uncertain attempt is not proof of no commit (reactivation eligibility can be checked before replay). Review remains locked until a verified receipt or session teardown; after logout review the account and audit history before any new action. Refresh current list after verified success or first canonical rejection. Seven regression cases and mandatory guard, including XAML and password isolation. No changes to CI workflow. 11.09 remains comprehensive audit/account parity and regression review; physical Windows native acceptance, final four customer packages and formal route migration remain separate gates. Require exact-head Core and exhaustive Release, real x86 development Portable and independent SHA256 before merge.

## Phase 11.09 — audit and account parity review

Base `886dcdc30417e4006c4db28df3f6018d4eba9476` (merged #90). Exact-head Core run 45736503607 and Release run 45736503508 succeeded, including nine disjoint test shards, independent TRX evidence, real Windows integration, x64 compilation and development x86 Portable acceptance/hash. This is development evidence, not owner acceptance.

| Legacy route | Native implementation reviewed | Canonical operation |
|---|---|---|
| screen_046 Users | NativeAccountsPageViewModel and accounts grid | ListUserAccounts |
| screen_047 AddUser | NativeAccountCreateReview / PasswordBox | SearchActivePersonsForAccount, CreateUserAccount |
| screen_048 ChangePassword | NativeAccountLifecycleReview | ChangeUserPassword |
| screen_049 DeleteAccount | NativeAccountLifecycleReview | DeactivateUserAccount, ReactivateUserAccount |
| screen_050 AuditLog | NativeAuditPageViewModel list and details | QueryAuditLog, GetAuditEntryDetails |
| screen_051 EditAuditEntry | NativeAuditEditReview | UpdateAuditPresentation |
| screen_052 DeleteAuditEntry | NativeAuditEditReview hide mode | DeleteAuditPresentation |
| screen_053 ModifiedDeletedAuditEntries | NativeAuditHistoryViewModel | QueryAuditPresentationChanges |

Review: role policy remains centralized, exact actor/session and row revision gate reads/writes, active roster eligibility and uniqueness stay canonical, account passwords remain unbound controls, deactivation/password change revoke sessions, audit edits/hides are append-only and require explicit review. Audit history is administrator-only and is read-only. Found and corrected an inherited presentation mismatch: a hidden overlay has a NULL corrected description, so the list, search and mutation before-state formerly lost the latest prior corrected description. Select the latest non-null corrected description independently of the latest visibility/version; original immutable evidence remains unchanged. Use a deterministic audit history tie-breaker. Native detail validates contiguous before/after presentation states and exact final description/version/visibility against the selected row. Added real SQLCipher regression and unit evidence plus mandatory guard. No schema change, no new dependency, no new role or write path. Existing historical immutable audit events are never rewritten.

Limitations: the eight routes have implementation evidence, but the formal SCREEN_MIGRATION_MATRIX remains unchanged pending owner visual/functional acceptance. The native pilot deliberately disallows editing an already hidden entry; the existing authorized legacy service permits a later edit, tested here only for canonical consistency. History pagination uses the original bounded endpoint without total count. The final four release packages and clean-machine/physical Windows acceptance are still separate gates. Do not claim production replacement of WebView2. Require successful exact-head Core and Release before approving this review.

## Phase 12.01 — native settings overview (read-only)

Base `f516289b5d89f6c38f3062f15e0a07dc34473d73` (merged #91, Phase 11 implementation complete). Reuse SettingsReadBackupService.OverviewAsync for all three standard authenticated roles, with exact durable principal checks before and after reading. Display the persisted account identity, role, backup directory and current directory usability, saved daily backup time and enabled flag, retention count, last recorded automatic backup success date/failure and saved theme. Validate exact actor, canonical time/date, retention and theme. Cancel and discard obsolete reads on refresh, navigation and session teardown. No new SQL, storage, configuration writes or scheduler. The enabled flag is a saved setting, not proof that the backup scheduler runs in the native host. Existing historical correction controls are preserved. Seven regression cases and mandatory guard. Native settings overview is a partial slice of screen_054, not formal route acceptance.

Proposed remaining Phase 12 order: 12.02 verified manual backup and destination selection; 12.03 automatic backup settings and reuse of original durable scheduler; 12.04 encrypted backup inspection and restore preview; 12.05 atomic restore and recovery/session lifecycle; 12.06 reset rescue and exclusive General Administration reset; 12.07 program information, appearance and diagnostics; 12.08 parity, regression and clean-machine evidence. Keep canonical authorization, encryption, retention and recovery gates. Do not switch production packages or mark the formal migration matrix complete without the required acceptance. No change to CI workflow; both exact-head Windows workflows and development x86 Portable/hash must pass before merge, with four final customer packages a separate release gate.

## Phase 12.02 — reviewed native manual encrypted backups and output folder

Base `46bafde4c4451e5a5a1edf82b452c928f1bb64cc` (merged #92). All three authenticated standard roles retain canonical permission to select an output folder and create a verified manual backup. Use the native Windows OpenFolderDialog and explicit confirmation; cancel does not write. Clearly disclose that saving a folder changes the shared backup/Excel location and enables the persisted automatic-backup flag, as in the original service. Reuse OutputDirectorySettings.SaveAsync and reread the persisted path. The original local/existing/writable directory restrictions remain canonical. Create via SettingsReadBackupService.BackupAsync with an optional expectedDirectory check before any file creation, preserving legacy callers. Reuse BackupInspectionService.VerifyAsync after creation: require the registered catalog entry, compatible schema, encrypted verification, exact output directory/ID, positive file size and matching SHA-256. No success shown for incomplete or inconsistent evidence. Review blocks conflicting settings actions and duplicate submissions; suppress stale receipts after session teardown. Because the existing canonical manual-backup service does not expose a durable replay receipt, an ambiguous result is NOT retried; log out and inspect the folder and audit log before creating another backup. No new database schema, storage engine, dependency, or CI workflow. Six focused unit cases and one real SQLCipher integration regression (destination stale rejection, encrypted file and catalog/hash verification); mandatory structural guard. Next: automatic backup configuration and original durable scheduler reuse (12.03). Do not claim active native automatic backups or restoration until separately implemented and tested. Require both exact-head Windows workflows and development x86 Portable/hash before merge; final four packages and physical acceptance remain separate gates.

## Phase 12.03a — reviewed automatic backup configuration

Base `4c2f81d69b23f4aa57fb6342f91b5d071ac2b0a6` (merged #93). Use the existing SettingsReadBackupService.ConfigureBackupScheduleAsync / AutomaticBackupService.ConfigureAsync, not a separate settings store. General and Ordinary Administration only. Exact live session and reference-bound overview; verify reviewed destination against persisted folder before invoking canonical service. Validate HH:mm, retention 0 (all) or 2–365 and usable destination when enabling; allow disabling with unavailable destination. Explicit before/after preview and retention warning; no immediate pruning or backup execution on opening the view. Confirm sends stable operationId and verifies enabled/time/count receipt. Ambiguous outcome freezes the intent; rejection on retry does not prove earlier failure. No duplicate settings workflows, refresh while review is open, or late receipt after session teardown. Five regression tests and mandatory contract guard. Canonical configuration has no optimistic settings revision; this is a reviewed snapshot and directory precheck, not an atomic compare-and-swap. No change to database schema, scheduler lifecycle, dependencies or CI. The saved setting is not proof of a running scheduler. Next 12.03b: link original AutomaticBackupSchedulerService and validate single start and stop/drain before disposing SQLCipher, including retention safety. Both exact-head Windows workflows and development x86 Portable/hash required before merge; four final packages remain a later gate.
