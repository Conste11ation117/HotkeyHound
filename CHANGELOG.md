# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versioning adheres to [Semantic Versioning](https://semver.org/).

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
