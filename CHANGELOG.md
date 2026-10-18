# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning adheres to [Semantic Versioning](https://semver.org/).

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
