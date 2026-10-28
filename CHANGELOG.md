# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning adheres to [Semantic Versioning](https://semver.org/).

## [2.1.0] — 2026-10-29 “Hurricane”

A bug-fix release for what daily use of 2.0.0 turned up: an undiagnosable crash dialog when a hook
was installed, an unusable "stop monitoring" button, and remark columns that were cut off with a
blank strip beside them.

### Fixed: "Parameter count mismatch" dialog when installing a hook

- Every background-to-UI post used `Control.BeginInvoke(Delegate, params object[])`, whose
  marshalling path calls `Delegate.DynamicInvoke`. When the argument list did not line up with the
  delegate signature, WinForms raised `TargetParameterCountException` inside the message pump —
  no application frame in the stack, so the user saw a bare "Parameter count mismatch" dialog and
  the error log could not name the culprit. All posts now go through one helper (`UiPost`) that
  uses `MethodInvoker` (a direct call, no reflection) and, if the posted action itself fails,
  records the source method name with the stack trace instead of showing a dialog.
- Verified by reproduction: starting the key monitor and pressing a key used to write an entry to
  `error.log` immediately; with the fix the same sequence writes nothing.

### Fixed: "Stop monitoring" could not be clicked

- The key-monitor toggle was disabled when starting and never re-enabled on success, so the button
  already read *Stop monitoring (unload hook)* but was greyed out — there was no way to stop.
  It is now re-enabled as soon as the monitor starts. Verified end to end: start → button becomes
  *Stop monitoring* → click → status shows "Stopped monitoring; hook unloaded".

### Fixed: the remark column was cut off, with a blank column beside it

- The last column now takes the **larger** of "measured content width" and "remaining window
  width": a long remark widens the column and the table scrolls horizontally, while short remarks
  still fill the viewport. No more ellipsis-only column, and no blank strip on the right.
- The viewport is measured from `ClientSize` (which already excludes the vertical scrollbar); the
  old extra subtraction of the scrollbar width was exactly what produced that blank strip.
- Remark is now the **last** column on every page: the hook-suspects page lists the config path
  before it, and the attribution page lists the elevation flag before the process path.
- Fixed columns are no longer compressed to make room, so their text stops turning into "已…" /
  "实测…"; if the table does not fit, it scrolls.

### Fixed: small cleanup

- `%LOCALAPPDATA%\HotkeyHound\error.log` now records the source of a UI-callback failure
  (`ui:<method>`), so a future report of this kind is diagnosable from the log alone.


## [2.0.0] — 2026-10-28 “Hurricane”

The biggest interface change since 1.0 — a sidebar instead of tabs, a collapsible log drawer and
DPI-aware lists — together with four correctness fixes (Engine C's verdicts, the numpad virtual-key
table, Windows-reserved shortcuts, and crash reporting) and an exception backstop inside the
injected hook DLL. The first launch now asks for a language, the hook-detection page can capture a
combination instead of asking you to type it, and the README was rewritten around what you see.

### Fixed: Engine C false alarms

- **Verdicts asserted what the prerequisites could not support.** "Swallowed before the hotkey" was
  reported even when Engine B was not listening — with no attribution channel, an occupied key that the
  sniffer saw but that produced no hotkey firing *looks* swallowed. `HookVerdictLogic.Decide` now takes
  two prerequisites (`attributionActive`, `scanDone`) and returns a new **Inconclusive** verdict naming
  the missing prerequisite instead of guessing. Arming while Engine B is off asks once for consent to
  co-start it (still one explicit user action; no hook is ever installed silently).
- **A session where nothing was pressed now says so.** A timeout with no key down used to be judged
  from an empty observation set; it is now its own `noInput` evidence and explanation.
- **`Stop()` could leak the hook thread and wedge the page.** `WM_QUIT` posted before the thread created
  its message queue was simply lost. The post is now retried until the thread exits, re-posted once more
  after a join timeout, and a final failure is logged instead of leaving a zombie. The thread id is
  cleared on exit, so a stale id can never post quit into an unrelated thread.

### Fixed: the numpad virtual-key table was off by one

- `VK_SEPARATOR (0x6C)`, `VK_SUBTRACT (0x6D)`, `VK_DECIMAL (0x6E)` and `VK_DIVIDE (0x6F)` were mapped
  one slot early (0x6C..0x6E), so `Num-` / `Num.` / `Num/` were reported under the wrong names and the
  scanner probed the wrong keys. The name table, the `Separator` / `Subtract` / `Decimal` / `Divide`
  aliases and the probe list now agree; T1 asserts every name round-trips through format and parse.

### Fixed: "Restart as administrator" never restarted

- The old instance still held the single-instance mutex while it showed a *modal* "we are restarting"
  dialog, so the elevated process saw "already running" and exited without a window. The mutex is now
  released **before** handing off, reacquired if the user cancels or elevation fails, and a successful
  handoff shows no modal at all (log line, hide, close).
- `BringExistingToFront` searched for an exact window title, but the real title is formatted
  ("HotkeyHound — ..."), so it never matched: it now enumerates top-level windows by title prefix.
- A second launch waits up to 1.5 s for an abandoned mutex before concluding another instance is alive.

### Fixed: Windows-reserved shortcuts are their own row type

- The truth table used to file Alt+F4, Alt+Tab, Win+L and friends under *occupied, nobody claims it*,
  which reads like a mystery process is holding them. They now get a fifth state,
  **Windows built-in shortcut**, with its own explanation, note and filter in the truth table, and the
  explanation says explicitly that this is Windows behaviour and not a third-party application.
- The built-in table is now also asserted end to end (99 of 99 entries parse), and the CLI
  `--attribute-all` batch includes reserved combinations instead of silently skipping them.

### Hardened: the injected hook procedure can no longer take a host process down

- The hook procedure runs inside foreign processes. Any access violation there (our own bug, or a
  corrupted `MSG*` left by an upstream hook in the chain) used to take that process down with it. The
  whole hook body is now wrapped in `__try/__except`: on a fault it drops that one message and still
  calls `CallNextHookEx`, so the host process is unaffected.
- The shared-memory `INIT_ONCE` callback gets the same treatment and returns `FALSE` on a fault, which
  leaves the one-shot initialisation retryable instead of stranding other threads of that process in
  the init wait.

### Added

- **First-run language picker.** On the first launch (no `settings.ini` yet) the application asks for
  the interface language before it builds the main window, defaulting to **English**; the choice is
  written to `%LOCALAPPDATA%\HotkeyHound\settings.ini` and can be changed later on the Report & About
  page. Command-line modes and screenshot mode never show it.
- **A crash log instead of a raw CLR dialog.** Unhandled UI and background exceptions are appended to
  `%LOCALAPPDATA%\HotkeyHound\error.log` with version and timestamp, and the user sees one short,
  bilingual message pointing at that file instead of a stack trace.
- **Capture a combination.** The hook-detection page can install a temporary low-level hook, read the
  key you press, fill the field and unload it again (and unloading also happens when the page is left
  or the window closes), so the tested combination no longer has to be typed by hand.
- **README rewritten around what you see and click**: a 30-second start, a symptom → page → button →
  result table, and a page-by-page summary, before the engine/comparison material.

### Interface

- Seven pages are now reached from a **sidebar** instead of a tab strip; the selected page is
  highlighted and the sidebar widens for English labels instead of clipping them.
- The log is a **collapsible drawer** (one click, 36 px when folded), so the main content keeps the
  vertical space and the log is still one click away.
- Header carries the application icon, product name, one-line description and a Help button; the status
  bar keeps the version and the current language.
- List columns are **DPI-aware**: fixed widths scale with the display, headers are measured so that
  "Process running" / "Config state" are never cut, and when the window is too narrow the fixed
  columns shrink proportionally instead of pushing the last column off-screen.
- Buttons size themselves to their text, so "生成 HTML 报告" / "Arm (installs hook)" are never truncated.
- Overview counters sit on rounded cards with the value and label in separate rows (no overlap at high
  DPI); palette refreshed for both light and dark themes; lists are double-buffered.

### Tests

- T1 asserts the virtual-key table end to end, including the numpad names and aliases.
- T4 asserts the fifth truth-table state, the Windows-reserved classification (`Alt+F4`) and that all
  99 built-in entries parse.
- T7 walks all seven verdict branches, including the two prerequisite-missing ones that must yield
  Inconclusive.
- T3 and T4 no longer assume PowerToys is installed: T3 parses a synthetic Keyboard Manager document
  on every machine and only cross-checks the real config when one exists; T4 skips the
  "declared must match the truth table" assertion when no adapter source is present.

## [1.3.1] — 2026-10-19 Hardening

An adversarial audit of the two layers a normal test cannot reach — the injected native DLL and the
32-bit companion process — found four defects that could damage or crash a machine and nine that failed
silently. All are fixed. No UI or report-format changes.

### Fixed: the injected hook DLL

- **A 64 GiB memory request per injected process.** `CreateFileMappingW` takes `dwMaximumSizeHigh`
  before `dwMaximumSizeLow`; the call passed `sizeof(HksControl), 0` — i.e. 16 in the *high* dword, a
  16 × 4 GiB = 64 GiB pagefile-backed section. On most machines that just failed, so the DLL's
  "create the mapping myself" fallback never worked; on a machine with a large pagefile it would have
  pinned 64 GiB of commit charge in **every** process the hook injected into. Fixed to `0, 64` — and 64
  is not arbitrary: a named section keeps the size its first creator asked for, and the controller maps
  64 bytes, so a 16-byte section would have broken the controller instead.
- **Permanent deafness after one failed initialisation.** The `INIT_ONCE` callback returned `TRUE` even
  on failure, marking initialisation complete: that process would never report again, even after the
  controller started. Failures now return `FALSE` (retry), capped at 8 attempts so a machine without a
  controller does not re-map on every keystroke.
- **The DLL could zero an armed control block.** It opened the shared memory read-write and wrote
  `magic/arm/ctl*` when the magic did not match; the controller writes those four fields one at a time,
  so an interleaving could clear `arm` just after it was set — attribution goes silent globally with
  nothing in any log. The DLL now opens it **read-only** and never writes.
- **Torn reads.** Four independent non-volatile reads could pair a new low dword with an old high dword
  and produce a nonsense window handle. Fields are `volatile` now; the DLL reads `arm → magic → handle`
  and the controller writes `ctlLo → ctlHi → magic → memory barrier → arm=1`.
- **Layout regressions were unprovable.** `static_assert(sizeof == 16)` still passes when two fields are
  swapped. Four `offsetof` assertions now pin each field — and immediately paid for themselves: the
  first build failed because `offsetof` needs `<cstddef>`.
- **Elevated runs received nothing.** UIPI drops messages from lower-integrity processes to a
  higher-integrity window, so running as administrator left attribution silently dead. The control
  window now lets exactly one message through (`ChangeWindowMessageFilterEx`).
- `MapViewOfFile` maps the whole section (size 0) instead of exactly 16 bytes.

### Fixed: the 32-bit companion process

- Restarting attribution leaked an orphan helper holding a live hook (the launcher never reaped the old
  one).
- The forced-exit fallback could never run: `RequestQuit` took the stdout lock before starting the
  1.5-second watchdog, so a blocked `Print` (full pipe) blocked the watchdog too.
- stderr was redirected and never read — an unhandled .NET exception in the helper would fill the 4 KB
  pipe buffer and hang the process.
- Failures never signalled the ready event, so the caller waited out the full 4-second timeout instead
  of getting a fast, explicit failure.
- The stop/start handshake could leak a stale "ready" into the next session.
- A stale stdout reader could publish state into a newer session (TOCTOU).
- A `File.Delete` failure (a process still holding the old DLL) was swallowed, silently switching to a
  second copy in another directory — two versions in play, with no warning.
- The helper now lives in a **Job Object with `KILL_ON_JOB_CLOSE`**, so Windows reaps it even if the
  controller is killed outright; the parent-PID watchdog stays as the second line.
- Window handles were printed as signed decimals and parsed by a digits-only parser, so a handle with
  bit 31 set arrived as 0 (and made the 32-bit self-test flaky).
- `LoadLibrary` → `LoadLibraryEx(..., LOAD_WITH_ALTERED_SEARCH_PATH)` so the hook DLL's dependencies
  cannot be planted next to the helper executable.

### Fixed: robustness against malformed configuration

- The hand-written JSON parser had no recursion limit. A corrupt or truncated app config (a few
  thousand `[`) overflowed the stack — and `StackOverflowException` is the one .NET exception that
  cannot be caught, so the process simply vanished. Depth is capped at 64 (real configs stay under 10);
  exceeding it raises a normal, catchable exception. Verified: 200,000 `[` now returns "no data"
  instead of killing the process.
- Attribution records, hook-check records and comparison snapshots were unbounded. A hotkey registered
  without `MOD_NOREPEAT` produces dozens of `WM_HOTKEY` messages per second while held, so an overnight
  session could pile up six figures of records — growing memory and freezing the list view. They are
  capped now (20,000 / 20,000 / 500) and the oldest are dropped with one log line.

### Changed: CI proves the English build is English

The smoke test used to look for one particular Chinese word in the English report. It now counts
**every** CJK character in the generated English report and fails on a single one, printing the
surrounding context.

### Documentation

- [`docs/code-review.md`](docs/code-review.md) restructured around this audit: what was checked, what
  was found (with severity), what was fixed, and the risks that were accepted with reasons.
- README now links the FAQ and the audit.

## [1.3.0] — 2026-10-18 Companion

### Added: attribution for 32-bit (WOW64) processes

Windows only lets a hook inject into processes whose **bitness matches the process that installed it**.
The x64 build was therefore blind to every 32-bit process's `WM_HOTKEY` — the one comparison-table item
where this project still trailed. A 64-bit process cannot work around that rule from the inside, so 1.3.0
ships a second, tiny process:

- `HotkeyHound.Hook32.dll` — the same `src/Hook/HookDll.cpp` compiled a second time with
  `vcvars32 + cl /LD` (`PE Machine=0x014C`). It installs `WH_GETMESSAGE` and reports the owner's PID
  exactly like the x64 build.
- `HotkeyHound.Helper32.exe` — a 32-bit console host that releases the DLL, calls `LoadLibrary` →
  `GetProcAddress("HotkeyHoundHookProc")` → `SetWindowsHookEx(WH_GETMESSAGE, proc, hMod, 0)`, and pumps
  messages. It never hands a managed delegate to `SetWindowsHookEx`: a delegate address is only valid in
  the current process and injecting it would crash every GUI process on the machine (see AGENTS.md).
- `AttributionEngine.Start()` starts the companion in parallel with the x64 hook. If extraction or
  startup fails, the failure is logged and only 32-bit coverage is lost — the x64 path keeps working.
  `Stop()` sends `q` on the companion's stdin, waits two seconds, then terminates it as a last resort.
  The companion also watches its parent with `OpenProcess(SYNCHRONIZE)` and exits the moment the parent
  dies, so a crashed HotkeyHound cannot leave a zombie behind.
- `AttributionRecord.Bitness` (`x64` / `x86`) is derived from the target process and shown in the
  attribution list and the report, so every row says which injection path produced it. `--listen` prints
  the same column.

### Fixed: the shared-memory control block was bitness-dependent

The handshake block used to be `struct { DWORD magic; HWND ctl; DWORD arm; }`. `HWND` is 4 bytes in a
32-bit process and 8 bytes in a 64-bit one, so the x86 hook looked for `arm` four bytes away from where
the x64 controller wrote it (inside the handle field) and never reported anything. The layout is now
**four 4-byte fields** on both sides:

```c
struct { DWORD magic; DWORD arm; DWORD ctlLo; DWORD ctlHi; }
```

and the DLL reconstructs the window with `(HWND)(((ULONGLONG)ctlHi << 32) | ctlLo)`, while the
controller splits `(long)hwnd` into lo/hi. One mapping now works for both bitnesses.

While fixing this, artifact extraction was also tightened: it used to treat a same-length file as
"already up to date", which silently kept a stale x64 DLL after the layout change. It now compares the
bytes and rewrites on any difference.

### Tests and docs

- Self-test gains **T10 (32-bit chain)**: it starts the companion in `--selftest-target` mode, posts
  `WM_HOTKEY` to its window, and asserts that a report with the correct PID and `Bitness=x86` arrives
  within 3 seconds. If it cannot, the test is INCONCLUSIVE (not a failure) and the message names the
  step that broke. `--no-hooks` skips T10 along with T6/T7.
- Help documents (zh/en, Markdown and HTML) now say 32-bit processes are covered and explain the
  companion process. README is intentionally unchanged in this release.

## [1.2.0] — 2026-10-18 Sentinel

### Added: the key monitor (seventh tab, `--monitor`)

HotkeyScan's **Key monitor** was the one place where this project clearly lagged. A scan answers "who
registered this combination", attribution answers "who received it", but neither answers the question
users actually ask when a key does nothing: **is the keyboard broken, is a driver filtering it, or is
some software eating it?** This release observes the same keystroke at three layers, each with its own
timestamp, virtual-key code, scan code and event flags:

1. **Raw Input** (`RegisterRawInputDevices` with `RIDEV_INPUTSINK`, `usUsagePage=0x01, usUsage=0x06`,
   `WM_INPUT`) — did the key leave the device and reach Windows?
2. **`WH_KEYBOARD_LL`** — did the key reach this program's low-level keyboard hook?
3. **Window messages** — did this program's window receive `WM_KEYDOWN`/`WM_KEYUP` (only possible while
   it has focus).

`KeyMonitorLogic` is a pure function so the four verdicts are unit-testable: **never reached the system**
(no raw input → hardware/firmware/filter driver), **swallowed before the hook chain** (raw but no hook),
**swallowed after the hook chain** (raw + hook, a global hotkey, but engine B reported no owner), and
**delivered** (all three layers, or a global hotkey with a confirmed owner). If the combination is a
global hotkey but attribution is not listening, the verdict is honestly **inconclusive** instead of
pretending the missing owner is proof of interception. The page also compares left/right
Ctrl/Alt/Shift/Win from `GetAsyncKeyState` against what the hook saw, to surface stuck modifiers.

### Read-only: remaps and filter drivers, never unloaded

The monitor parses `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout\Scancode Map` into human-readable
"press X → system produces Y" rows, and lists keyboard-class filter drivers (`Type=1`, `Start≠4`,
keyboard-like group/driver name). It deliberately **only lists** them. A driver in that list is not proof
of interception — `kbdclass.sys`, `kbdhid.sys` and `i8042prt.sys` are the normal input stack. Stopping or
unloading a keyboard driver can break input for the whole session or bugcheck the machine, and this tool's
contract is read-only: no configuration writes, no registry changes, no process or driver termination.
The list is evidence to discuss with Device Manager or vendor documentation, not an action list.

### UI: dark mode, one palette, a real icon

- The app follows the system theme (`HKCU\...\Themes\Personalize\AppsUseLightTheme`) and adds a
  **System / Light / Dark** switch on the Report & about page, persisted next to the language in
  `%LOCALAPPDATA%\HotkeyHound\settings.ini` (restart applies it).
- Colours, fonts and spacing now come from `src/Ui/UiTheme.cs`; pages no longer hard-code colours.
  Truth-table, hook-suspect and attribution rows are tinted by status using the palette's
  success/warning/error colours.
- A generated 256×256 icon (`assets/hotkeyhound.ico`, magnifier + key outline) is embedded with
  `/win32icon:` and used as the window icon. `--screenshot` renders all **seven** pages, in both light
  and dark themes.

### Tests and docs

- Self-test gains **T9**: the four verdict branches, the extra delivered/inconclusive/not-global semantics,
  a constructed `Scancode Map` sample (including an `E0` extended scan code), and stuck-modifier states.
  T9 installs no hooks and runs under `--no-hooks`.
- Help documents (zh/en, Markdown and HTML) gain a "how to use the key monitor and read its verdicts"
  section. README is intentionally unchanged in this release.

## [1.1.0] — 2026-10-17 Tracker

### Added: auto-attribution (engines A x B)

Engine A can say *which* combinations are occupied but not *who* owns them; engine B can name the owner but
only after the user presses the key. This release connects the two: `--attribute-all` runs engine A, then
simulates pressing each occupied combination and uses engine B's attribution reports to identify the owner.
Results fall into three classes: **Attributed**, **No response** (pressed, but the owner never received it —
very likely swallowed by a hook), and **Skipped** (blacklist, timeout, abort). That last class is precisely
where this tool goes beyond Hotkey Screener's "Detect application": a commercial tool only answers "who
responded", while "nothing responded" is itself the most valuable clue.

The other half of the release is the safety boundary: **dry-run by default** — without `--yes` it only prints
the plan and presses nothing; the GUI button goes through an explicit risk confirmation; anything containing
`Win`, plus `Alt+F4`, `Alt+Tab`, `Alt+Esc`, `Alt+Space`, `Ctrl+Esc`, `Ctrl+Alt+Del`, `Ctrl+Shift+Esc`,
`Ctrl+Alt+Tab`, `Alt+Shift+Tab` and the GPU driver's display-rotation keys (`Ctrl+Alt+arrows`) is a
hard-coded exclusion that cannot be relaxed; key presses are at least 400 ms apart, the
attribution wait is 1.2 s, Esc aborts immediately, and aborts and skips are counted honestly in the report.

### Added: hook-hotkey catalog (sixth tab, "Hook suspects")

A read-only catalog at `data/hook-catalog.json` (embedded at build time, overridable/appended via
`%LOCALAPPDATA%\HotkeyHound\catalog.json`). For every known software it reads the config, checks whether
the process is running, and compares against engine A's measurement, producing hook suspects with an
**evidence level** (Confirmed / Highly suspected / Known default / Reference only). Configs that engine D
can already parse (PowerToys Keyboard Manager, Peek) reuse those adapters instead of a second parser.

### Why not a kernel driver

Hook-based hotkeys (Keyboard Manager, IMEs, AutoHotkey, vendor macros) never call `RegisterHotKey`; their
keys are handled by low-level hooks or drivers before reaching system dispatch. A kernel driver can read
win32k's hotkey table, **but it cannot read hooks** — so the driver approach does not solve the very class
of problem this tool exists for. The cost is very real: unsigned drivers, Secure Boot / HVCI blocking,
antivirus and anti-cheat false positives, all for troubleshooting a shortcut. So this release deliberately
takes the other road:

> read its config + check whether it runs + compare against measured occupancy → produce *highly suspected*
> conclusions and tell the user where to change them.

The evidence levels make that road's limits visible: only "Confirmed" means the config explicitly declares
the key and the process runs; every other level carries uncertainty. The tool would rather print
"reference only" than present a guess as a fact.

### Documentation and UI

- Sixth tab, "Hook suspects": combination / software / module / evidence level / process running / config
  readable / explanation / source path, with an evidence filter and "Open config folder"; loading the page
  installs **no** hook.
- The report gains `#hooksuspects` (grouped by evidence level) and `#autoprobe` (three-class counts plus one
  row per result). Both sections are emitted even with no data, with instructions on how to collect it.
  Advice now says "Nothing happens when you press them? They are very likely swallowed by its hook — change
  it in that software's settings" when a Confirmed/Highly-suspected suspect hits a combination that engine A
  measured as free.
- Help documents (zh/en, Markdown and HTML) gain sections on auto-attribution risk/usage and on reading hook
  suspects.
- Self-test gains T8: catalog loading, evidence-level output, and the blacklist/dry-run semantics.

## [1.0.0] — 2026-10-16 Watchtower

First public release. Everything below was built and verified on a single day, including the
incident recorded under 0.2.0 — which is the reason this project documents its own failures.

### Four engines

- **Engine A — occupancy scan.** Probes roughly 1650 modifier × key combinations with
  `RegisterHotKey`; a successful registration is released in the same instant, a failure with
  error 1409 counts as occupied. The scan has no side effects: no configuration, no registry,
  no lingering registrations.
- **Engine B — press attribution.** A native hook DLL is injected through a global
  `WH_GETMESSAGE` hook; when a hotkey owner retrieves its `WM_HOTKEY`, the DLL reports the owning
  process id through a shared-memory handshake. Attribution completes in ~330 ms in the self-test.
- **Engine C — hook-swallow detection.** A low-level keyboard hook plus a four-way verdict:
  *never reached this process* (swallowed upstream), *reached the system but the owner never saw
  it*, *delivered*, *not a global hotkey at all*. Includes a guided comparison workflow that only
  records observations — it never terminates or suspends another process.
- **Engine D — config aggregation.** Readers for PowerToys (all modules plus Keyboard Manager
  remaps), Everything, OBS Studio, NVIDIA ShadowPlay (registry), Wallpaper Engine and MSI
  Afterburner, diffed against engine A to produce the four-kind truth table.

### Reporting

- Self-contained HTML report (inline CSS/JS, no CDN) with an occupancy map, the truth table,
  declared hotkeys per software, attribution records, hook-check verdicts, generated advice and a
  method/limitations section.
- Advice is aggregated per module and only ever quotes exact matches; fuzzy "Shift variant"
  hints stay in the notes where they belong. (An earlier build printed keys that were never
  declared — `Ctrl+K` for a `Ctrl+Shift+K` remap — which is exactly the kind of confidently wrong
  output this tool exists to eliminate.)

### Interface

- Five pages: overview, press attribution, hook detection, truth table, report & about.
- Hooks are **never** installed by loading the application; each hook page states the contract in
  plain language and labels its button with *（安装钩子）* / *(installs a hook)*.
- Elevated restart button, an integrity/elevation banner, and a one-click **F1** help window.

### Added in this release

- **Elevation interface**: `--elevate` restarts the tool as administrator with the original
  arguments plus a loop guard; the UI offers the same from the banner and the overview page, and
  reports clearly when the user declines the UAC prompt.
- **Crisp text on scaled displays**: an embedded manifest with `PerMonitorV2` DPI awareness
  (previously the window was bitmap-scaled by Windows and looked blurry at 150–200 %), the UI font
  pinned to Microsoft YaHei UI, and a main window clamped to the working area.
- **Help**: `docs/help.zh-CN.md` and `docs/help.en-US.md`, plus embedded HTML renditions shown in
  the in-app help window.
- **English version**: every user-visible string — interface, CLI help and messages, HTML report,
  help documents — is served from a `zh-CN` / `en-US` string table. Language selection follows
  `--lang`, then the saved setting, then the system UI language.

### Changed

- **Renamed the project to HotkeyHound** before the first public release. The former name
  `HotkeyScope` collides with an identifier that UI frameworks use everywhere
  (`HotkeyScope.system`, `HotKeyScope.inapp` in Flutter/Avalonia projects), which buries a
  repository in search results. Nothing about the tool itself changed.

## [0.2.0] — 2026-10-16

Engines B and C, the interface pages that drive them, and the two self-tests that exercise the
hook path on a real machine.

### Fixed — machine-wide crash caused by a managed hook procedure

An early build passed a **managed delegate** to `SetWindowsHookEx(WH_GETMESSAGE, …, 0)` instead of
the native export obtained from `GetProcAddress`. The delegate's address is valid only inside our
own process; Windows injects the DLL into every process and calls the stored address there, so
every GUI process that retrieved a message crashed at once (`0xc0000409`, faulting module
`unknown`). Observed on the development machine as eight unrelated processes faulting inside the
same second, including `explorer.exe`, `sihost.exe` and `svchost.exe`.

The controller now installs the hook with the native export pointer, and the injected DLL was
hardened: an empty `DllMain` (no critical-section lifecycle under the loader lock), one-shot
`InitOnceExecuteOnce` initialisation instead of a critical section on the hot path, and a hook
procedure that only reads memory and posts one message before passing the event through.

### Added

- `--listen` console attribution with a bounded duration, `--helper` (internal), and
  `--selftest` extended with T6 (attribution chain: hook installation, shared-memory handshake,
  injected interception, callback, pid resolution) and T7 (low-level hook verdicts plus a real
  swallowed-key test).
- `--selftest --no-hooks` for machines where installing hooks is not acceptable.

## [0.1.0] — 2026-10-16

First working build: engine A, engine D with its per-software adapters, the reconciler, the HTML
report, the WinForms shell and the command line (`--scan`, `--report`, `--json`, `--help`,
`--selftest` with T1–T5).

Measured on the development machine: 1650 combinations probed in milliseconds, ~207 registered
combinations, 54 declared hotkeys (PowerToys modules, eight Keyboard Manager remaps, NVIDIA
overlay), and a 1651-row truth table.
