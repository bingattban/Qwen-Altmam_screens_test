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
