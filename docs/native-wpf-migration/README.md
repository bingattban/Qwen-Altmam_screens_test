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
