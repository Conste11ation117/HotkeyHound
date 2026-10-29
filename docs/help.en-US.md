# HotkeyHound Help (English)

The program only reads and probes: it never changes your configuration, never writes the registry, and never ends or pauses any process.

Quick start: the first launch asks you to choose the interface language (change it later under "Report & about"); then click "Scan overview → Start scan" to see which combinations are occupied; use "Key attribution" to find the owner; use "Hook detection" when a key press does nothing.

For each engine below: what it does / how to use it / its limits. Page names, button labels and command-line flags below match the actual interface and CLI.

---

## 1. The four engines

### Engine A — occupancy scan

- **What it does**: for every "modifier combination × key" (~1650) it calls `RegisterHotKey`: on success it immediately calls `UnregisterHotKey` and records the combination as **free**; on failure with error 1409 (`ERROR_HOTKEY_ALREADY_REGISTERED`) it records it as **occupied**.
- **How to use**: click **Start scan** on the Scan overview page, or run `HotkeyHound.exe --scan --report report.html`.
- **Limits**: read-only, no administrator needed; covers the current session only; cannot see hotkeys implemented with keyboard hooks (see section 3).

### Engine B — key attribution

- **What it does**: installs a global message hook (`WH_GETMESSAGE`, implemented by a native DLL) that intercepts `WM_HOTKEY`. When you press a global hotkey, the process that owns it receives the message and the hook reports back "owner PID + combination".
- **How to use**: click **Start listening (installs hook)** on the Key attribution page, then press the hotkey, or run `HotkeyHound.exe --listen --seconds 30`.
- **Limits**: it installs a hook and injects the native DLL into every process in the session. The hook is installed **only when you explicitly click the button**, and **Stop listening (unload hook)** unloads it immediately; loading the UI installs nothing. Run as administrator where possible, otherwise elevated processes (e.g. PowerToys run as admin) are invisible.
- **32-bit processes**: this program is 64-bit, so starting the listener also launches a 32-bit companion `HotkeyHound.Helper32.exe` that installs the 32-bit hook. Records from both paths land in the same table, and every row labels the target process's bitness (x64 / x86). A failed companion only costs 32-bit coverage — the reason goes to the log and the 64-bit path keeps working.

### Engine C — hook interception detection

- **What it does**: installs a low-level keyboard hook `WH_KEYBOARD_LL` to observe whether keys reach the system. Combined with engine A's occupancy table and engine B's attribution reports, it returns one of these verdicts:

| Verdict | Meaning |
|---|---|
| Delivered normally | The key reached the system and a clear attribution report arrived |
| Likely intercepted by a hook | The key reached the system and engine B was listening, but the owner did not get the command |
| Swallowed by an upstream hook | This program's hook never saw the key — an upstream hook swallowed it |
| Not a global hotkey | Seen, but engine A does not list it as occupied — only the current window handles it |
| Inconclusive (prerequisite missing) | Engine B was not listening, or engine A has not scanned — not enough evidence, no claim is made |

- **How to use**: on the Hook detection page, enter a combination and click **Arm (install hook)** (**only then is the hook installed**). If engine B is not running yet, one confirmation dialog offers to start it along with the sniffer (without engine B, "intercepted" cannot be proven). Then immediately press the key and read the verdict.
- **Limits**: this also installs a hook. This page **only observes; it never swallows or alters keys, and never ends or pauses any process.**

### Engine D — config aggregation

- **What it does**: parses hotkey declarations from the config files of known software (PowerToys, Everything, OBS, NVIDIA ShadowPlay, Wallpaper Engine, MSI Afterburner, etc.), diffs them against engine A, and produces the "truth table".
- **How to use**: it runs automatically alongside `--scan` / **Start scan**; results appear on the Config truth table page and in the report.
- **Limits**: none — read-only; broken or missing config files are skipped.

---

## 2. Why "the scan says free" may still be occupied

- **Keyboard-hook hotkeys are invisible.** PowerToys Keyboard Manager, AutoHotkey, IMEs and some game tools implement hotkeys with low-level keyboard hooks. They do not call `RegisterHotKey`, so engine A reports the key as "free" — yet when pressed, they grab it first.
- **Only the current session is visible.** `RegisterHotKey` is session/window-station scoped; hotkeys registered in other login sessions cannot be probed here.
- **"Registration failed" is not the same as "software occupancy".** A few combinations return errors other than 1409 (system reservations or special limits); the report lists the error code.

In short, engine A saying "free" only means "nobody registered it via RegisterHotKey". To confirm whether anything actually responds when pressed, use engine B and engine C.

---

## 3. Using engine B to find "keys that do nothing when pressed"

1. Open the Key attribution page (or `--listen`) and click **Start listening (installs hook)**.
2. Press the key and watch for an attribution record.
3. **A record appears**: it names the process that owns the key — that software grabbed it. **No record**: either it is not a global hotkey, or an upstream hook swallowed it; use engine C (the Hook detection page) to dig further.

Tip: combine `--listen` with engine C's comparison workflow — record "now", exit one suspect software, test again, and compare the two results.

---

## 4. Auto-attribution: asking who owns the occupied combinations

Engine A tells you which combinations are occupied but not who owns them. Auto-attribution connects the two: it **simulates pressing** each occupied combination and uses engine B's attribution reports to ask for the owner.

```
HotkeyHound.exe --attribute-all --dry-run
HotkeyHound.exe --attribute-all --yes --report attribution.html --max 30
```

- **Dry-run by default**: it only prints what would be pressed, what is skipped and why, and never presses anything. Only `--yes` actually presses keys.
- **The blacklist is hard-coded and cannot be relaxed**: anything containing `Win`, plus `Alt+F4`, `Alt+Tab`, `Alt+Esc`, `Alt+Space`, `Ctrl+Esc`, `Ctrl+Alt+Del`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Tab`, `Alt+Shift+Tab`, and the GPU driver's display-rotation keys (`Ctrl+Alt+arrows`). Those switch windows, close programs or open system UI.
- At least 400 ms between key presses, 1.2 s of attribution wait, at most 30 combinations by default (`--max`), and **Esc aborts immediately**.
- Three outcomes: **Attributed** (owner identified), **No response** (pressed, but the owner did not receive it within 1.2 s — very likely swallowed by a hook), **Skipped** (blacklist, over the limit, aborted, input failure). Aborts, timeouts and skips are all counted honestly and written to the report.

**Risk**: simulated presses really trigger those hotkeys — they may start recording, open panels, switch the input method, pop up windows or even launch programs. The GUI button **Auto-attribute occupied combinations (these hotkeys are really pressed)** shows a risk confirmation first and only then runs; progress is visible and you can press Esc or click **Abort auto-attribution (Esc)** at any time. If unsure, start with `--dry-run`.

---

## 5. How to read hook suspects

The sixth tab, "Hook suspects", and the `#hooksuspects` section of the report come from a read-only hook-hotkey catalog (embedded `data/hook-catalog.json` at build time; you can also put a file with the same name at `%LOCALAPPDATA%\HotkeyHound\catalog.json` to override or append entries). It does exactly three things: reads the software's config, checks whether the process is running, and compares against engine A's measurement.

Read the evidence levels exactly as defined; the tool never turns a guess into a fact:

| Level | Meaning |
|---|---|
| Confirmed | The config explicitly declares the combination and the software is running |
| Highly suspected | Only one side of the evidence: declared in config but not running, or unreadable config while the process runs and is known to use hook hotkeys |
| Known default | The software's factory-default shortcut; the local config could not be read |
| Reference only | The software definitely uses hooks, but the exact keys cannot be determined |

Usage: scan first; open the "Hook suspects" page, filter by evidence level; double-click a row or click "Open config folder" to jump to its configuration. Loading the page **installs no hook**.

**"Not in the list" does not mean "no hook".** The catalog only covers known software; to confirm whether a key is really swallowed, use engine C (the Hook detection page). If the report's advice section says "Nothing happens when you press them? They are very likely swallowed by its hook", the combination was free in engine A while that software has a matching hook suspicion — change it in that software's settings.

---

## 6. How to use the key monitor and read its verdicts

A scan and an attribution report cannot answer "at which layer did this key get lost?". The **Key monitor** page watches three layers at once:

1. **Raw Input (device layer)** — did the keyboard deliver the key to Windows at all?
2. **WH_KEYBOARD_LL (hook chain)** — did the key reach this program's low-level keyboard hook?
3. **Window messages** — did this program's window receive `WM_KEYDOWN`/`WM_KEYUP` (only possible while that window has focus)?

Usage: open **Key monitor** → **Start monitoring (installs keyboard hook)** → press the problematic key → read the **Verdict** card. The button says it installs a hook; **Stop monitoring (unload hook)** calls `UnhookWindowsHookEx` and unregisters raw input immediately. **Loading the page installs no hook.**

| Verdict | Meaning | What to try next |
|---|---|---|
| Never reached the system | Raw input never saw the key | Try another keyboard/port; review the keyboard-class driver list below (read-only, never judged or unloaded) |
| Swallowed before the hook chain | Raw input saw it, the low-level hook did not | Something upstream (IME, vendor driver, macro tool, remote-control software) intercepts it after the device layer and before the hook chain |
| Swallowed after the hook chain | Raw + hook saw it, it is a global hotkey, but engine B reported no owner | Start **Key attribution** and re-test; if there is still no owner, use the comparison workflow on **Hook detection** and exit suspect software one by one |
| Delivered | All three layers saw it, or the global hotkey got an owner | The input path is fine; the problem is in the receiving software |
| Inconclusive | It is a global hotkey but the attribution engine was not running | This is not "no problem" — it means there is not enough evidence; start Key attribution and re-test |

**Stuck modifiers**: left/right Ctrl/Alt/Shift/Win are checked with `GetAsyncKeyState` and compared with what the hook saw. "OS says down, hook saw no press" may simply mean the key was already held when monitoring started — release and press it again to confirm.

**Remaps and drivers (read-only)**: the `Scancode Map` value under `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout` is parsed into a "press X → system produces Y" list. Services under `HKLM\SYSTEM\CurrentControlSet\Services` that match `Type=1`, `Start≠4` and have a keyboard-like group/driver name are **listed only**. The tool never judges, stops or unloads a driver.

---

## 7. Permissions and integrity

- **Administrator (UAC)**: when not run as administrator, attribution for processes running as administrator (e.g. PowerToys) is invisible. The top banner and the Scan overview page both offer a "Relaunch as administrator" button; from the command line use `--elevate` (original arguments are preserved). If the user clicks No in UAC, the program recognizes it as "cancelled" and returns normally — it does not crash.
- **Low integrity (Low IL)**: if the exe sits in a directory carrying the `Mandatory Label\Low` label, the process runs at low integrity. Consequences: writes to `%TEMP%` / `%LOCALAPPDATA%` are denied, and **UIPI blocks hook injection**, so engines B/C are largely useless. **Copy the exe to an ordinary directory (e.g. `C:\Tools\`) and run it from there.**
- **UIPI (User Interface Privilege Isolation)**: a low-integrity or non-elevated process cannot inject hooks into higher-integrity processes. This is a Windows security mechanism, not a bug in this tool.

---

## 8. Known limitations

- **32-bit coverage depends on a separate 32-bit host**: Windows does not let a 64-bit hook inject into a 32-bit process, so 32-bit coverage comes from a companion started automatically when listening begins. If security software blocks it, or it cannot be extracted/started, that coverage is missing (the reason is in the log). Every attribution row and report row labels the target's bitness (x64 / x86).
- **Hook-based hotkeys do not go through WM_HOTKEY**: they neither appear in engine A's occupancy table nor trigger engine B's reports.
- **Only the current session is covered**: hotkeys in other login sessions are invisible.
- **Some config encodings are unverified**: the hotkey encodings of MSI Afterburner and Everything need a manual check.
- **Engines B/C install hooks**: this is by design, and only on explicit action.

---

## 9. Troubleshooting

- **Scan finds nothing / numbers too low?** Make sure another program is not registering a mass of hotkeys at the same time; re-run from an ordinary directory (low integrity affects both writing and probing).
- **"Start listening (installs hook)" errors out, or no process is ever visible?** Most likely low integrity (copy the exe to an ordinary directory) or not being an administrator ("Relaunch as administrator" / `--elevate`).
- **Cannot write the report?** Point the output path at a writable directory (e.g. the program's own folder). A low-integrity process cannot write to `%TEMP%` or `%LOCALAPPDATA%`.
- **Free in engine A, but pressing it still does nothing?** It is likely taken by a hook-based hotkey. Use engine B to listen and engine C to detect hooks.
- **How do I switch the UI to Chinese?** Add `--lang zh` on the command line, or switch the language in this help window / on the Report & about page (restart to take effect).

---

## 10. Uninstall / cleanup

- Portable single file: just delete `HotkeyHound.exe` — no installer, no registry writes.
- The program extracts the native DLL used by engine B (`HotkeyHound.Hook.dll`), the two 32-bit artifacts (`hooks\x86\HotkeyHound.Hook32.dll` and `HotkeyHound.Helper32.exe`) and `settings.ini` under `%LOCALAPPDATA%\HotkeyHound\` (or next to the exe). Deleting that directory is enough.
- Engine B's DLL is injected into processes in the current session; those injections **disappear naturally when the processes restart**, and **rebooting clears them completely**. When you stop listening, this program also calls `UnhookWindowsHookEx` to unload the hook.
