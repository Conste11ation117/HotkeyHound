# HotkeyHound Help (English)

HotkeyHound is a Windows global-hotkey conflict troubleshooting tool. It helps you see **which hotkeys are
occupied, who occupies them, why a key you press does nothing, and which keys are free to use.**

It only reads and probes: it never changes your configuration, never writes the registry, and never ends any process.

---

## 1. What problem this solves

HotkeyHound addresses the everyday annoyances below:

- You want to assign a global hotkey but the app says "already in use", and you cannot tell who took it.
- A key press does nothing — is it grabbed by some other software, or swallowed by a hook?
- After a new PC or a new install, a batch of old shortcuts behave differently and you want to find the culprit.
- You want to know whether a particular combination (e.g. `Ctrl+Alt+F13`) is free and usable.

It is built from four engines, each answering a different part of the question.

---

## 2. The four engines

### Engine A — occupancy scan (read-only, no admin needed)

**What it does**: for every "modifier combination x key" (~1650 combinations) it calls the Win32
`RegisterHotKey`. On success it immediately calls `UnregisterHotKey` and records the combination as **free**;
on failure with error 1409 (`ERROR_HOTKEY_ALREADY_REGISTERED`) it records it as **occupied**.

**How to use**: click Start scan on the "Scan overview" page, or from the command line:

```
HotkeyHound.exe --scan --report my-report.html
```

**Risk**: essentially none. It changes no settings, writes no registry, and undoes every registration instantly.

### Engine B — key attribution (installs a hook; administrator recommended)

**What it does**: it installs a global message hook (`WH_GETMESSAGE`, implemented by a native DLL) that
intercepts `WM_HOTKEY` messages. When you press a global hotkey, the process that owns it receives the
message, and the hook reports back "owner PID + combination" — so you learn **whose hotkey it actually is**.

**How to use**: click Start listening on the "Key attribution" page, then press the hotkey you want to
investigate; or from the command line:

```
HotkeyHound.exe --listen --seconds 30
```

**32-bit processes**: Windows only lets a hook inject into processes of the **same bitness** as the
installer. This program is 64-bit, so when listening starts it also launches a tiny 32-bit companion
(`HotkeyHound.Helper32.exe`) that installs the 32-bit hook. Records from both paths land in the same table,
and every row labels the target process's bitness (x64 / x86). A failed companion only costs 32-bit
coverage — the reason is written to the log and the 64-bit path keeps working.

**Risk**: this one installs a global hook and injects the native DLL into every process in the session.
Therefore:

- It is installed **only when you explicitly click Start listening**, and unloaded the moment you click Stop;
- Loading the UI installs **no** hook;
- Run as administrator where possible, otherwise elevated processes (e.g. PowerToys run as admin) are invisible.

### Engine C — hook interception detection (installs a hook)

**What it does**: it installs a low-level keyboard hook `WH_KEYBOARD_LL` to observe whether keys reach the
system. Combined with engine A's occupancy table and engine B's attribution reports, it produces one of four
verdicts:

| Verdict | Meaning |
|---|---|
| Delivered normally | The key reached the system and a clear attribution report arrived |
| Likely intercepted by a hook | The key reached the system but the owner did not get the command — a hook grabbed it |
| Swallowed by an upstream hook | This program's hook never saw the key, so an upstream hook swallowed it |
| Not a global hotkey | Seen, but engine A does not list it as occupied — it is handled only by the current window |

**How to use**: on the "Hook detection" page, enter a combination and click Arm (install hook) (**only then is
the hook installed**), immediately press the key, and read the verdict. When the verdict is "swallowed", use
the comparison workflow on the right to record, manually exit suspect software, and compare.

**Risk**: it also installs a hook. This page **only observes; it never swallows or alters keys, and never ends
or pauses any process.**

### Engine D — config aggregation (read-only, no admin needed)

**What it does**: it parses hotkey declarations from the config files of known software (PowerToys, Everything,
OBS, NVIDIA ShadowPlay, Wallpaper Engine, MSI Afterburner, etc.), diffs them against engine A's measurements,
and produces a "truth table".

**How to use**: it runs automatically alongside `--scan` / "Start scan"; results appear on the "Config truth
table" page and in the report.

**Risk**: none. All reads are read-only; broken or missing config files are skipped.

---

## 3. Why "the scan says free" may still be occupied

This is the most commonly misunderstood point. Engine A can only see hotkeys registered through
`RegisterHotKey`; a whole class of hotkeys does **not** go through it:

- **Keyboard-hook hotkeys are invisible.** PowerToys Keyboard Manager, AutoHotkey, IMEs and some game tools
  implement hotkeys with low-level keyboard hooks. They do not call `RegisterHotKey`, so engine A reports the
  key as "free" — yet when pressed, they grab it first.
- **Only the current session is visible.** `RegisterHotKey` is session/window-station scoped; hotkeys
  registered in other login sessions cannot be probed here.
- **"Registration failed" is not the same as "software occupancy".** A few combinations return errors other
  than 1409 (system reservations or special limits); the report lists the error code.

In short: engine A saying "free" only means "nobody registered it via RegisterHotKey". To confirm whether
anything actually responds when pressed, use engine B to listen and engine C to detect hooks.

---

## 4. Using engine B to find "keys that do nothing when pressed"

Suppose a key does nothing and you want to know what handled it:

1. Open the "Key attribution" page (or `--listen`) and click Start listening.
2. Press the key and watch for an attribution record.
3. Two cases:
   - **A record appears**: it names the process that owns the key. That software grabbed it.
   - **No record**: either it is not a global hotkey (only the current window handles it), or an upstream hook
     swallowed it. In that case, use engine C (the "Hook detection" page) to dig further.

Tip: combine `--listen` with engine C's comparison workflow — record "now", exit one suspect software, test
again, and compare the two results to pin down the culprit.

---

## 5. Auto-attribution: asking who owns the occupied combinations

Engine A tells you which combinations are occupied but not who owns them. Auto-attribution connects the
two: it **simulates pressing** each occupied combination and uses engine B's attribution reports to ask
for the owner.

Command line (dry-run by default — it only prints what would be pressed, what is skipped and why, and
**never presses anything**):

```
HotkeyHound.exe --attribute-all --dry-run
HotkeyHound.exe --attribute-all --yes --report attribution.html --max 30
```

- Only `--yes` actually presses keys; without it the run is always dry-run.
- The blacklist is **hard-coded and cannot be relaxed**: anything containing `Win`, plus `Alt+F4`,
  `Alt+Tab`, `Alt+Esc`, `Alt+Space`, `Ctrl+Esc`, `Ctrl+Alt+Del`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Tab`.
  Those switch windows, close programs or open system UI.
- At least 400 ms between key presses, 1.2 s of attribution wait, at most 30 combinations by default
  (`--max`), and **Esc aborts immediately**.
- Three outcomes: **Attributed** (owner identified), **No response** (pressed, but the owner did not
  receive it within 1.2 s — very likely swallowed by a hook), **Skipped** (blacklist, over the limit,
  aborted, input failure). Aborts, timeouts and skips are all counted honestly and written to the report.

**Risk (read this first)**: simulated presses really trigger those hotkeys — they may start recording,
open panels, switch the input method, pop up windows or even launch programs. The GUI button
"Auto-attribute occupied combinations" shows a risk confirmation first and only then runs; progress is
visible and you can press Esc or click Abort at any time. If unsure, start with `--dry-run`.

---

## 6. How to read hook suspects

The sixth tab, "Hook suspects", and the `#hooksuspects` section of the report come from a read-only
hook-hotkey catalog (embedded `data/hook-catalog.json` at build time; you can also put a file with the
same name at `%LOCALAPPDATA%\HotkeyHound\catalog.json` to override or append entries). It does exactly
three things: reads the software's config, checks whether the process is running, and compares against
engine A's measurement.

Read the evidence levels exactly as defined; the tool never turns a guess into a fact:

| Level | Meaning |
|---|---|
| Confirmed | The config explicitly declares the combination and the software is running |
| Highly suspected | Only one side of the evidence: declared in config but not running, or unreadable config while the process runs and is known to use hook hotkeys |
| Known default | The software's factory-default shortcut; the local config could not be read |
| Reference only | The software definitely uses hooks, but the exact keys cannot be determined |

Usage: scan first; open the "Hook suspects" page, filter by evidence level; double-click a row or click
"Open config folder" to jump to its configuration. Loading the page **installs no hook**.

Important: **"not in the list" does not mean "no hook"**. The catalog only covers known software; to
confirm whether a key is really swallowed, use engine C ("Hook detection" page). If the report's advice
section says "Nothing happens when you press them? They are very likely swallowed by its hook", the
combination was free in engine A while that software has a matching hook suspicion — change it in that
software's settings.

---

## 7. How to use the key monitor and read its verdicts

A scan and an attribution report cannot answer "at which layer did this key get lost?". The
**Key monitor** page watches three layers at once:

1. **Raw Input (device layer)** — did the keyboard deliver the key to Windows at all?
2. **WH_KEYBOARD_LL (hook chain)** — did the key reach this program's low-level keyboard hook?
3. **Window messages** — did this program's window receive `WM_KEYDOWN` / `WM_KEYUP` (only possible
   while that window has focus)?

Usage: open **Key monitor** → **Start monitoring (installs keyboard hook)** → press the problematic
key → read the **Verdict** card. The button says it installs a hook; **Stop monitoring** calls
`UnhookWindowsHookEx` and unregisters raw input immediately. **Loading the page installs no hook.**

| Verdict | Meaning | What to try next |
|---|---|---|
| Never reached the system | Raw input never saw the key | Try another keyboard/port; review the keyboard-class driver list below (read-only, never judged or unloaded) |
| Swallowed before the hook chain | Raw input saw it, the low-level hook did not | Something upstream (IME, vendor driver, macro tool, remote-control software) intercepts it after the device layer and before the hook chain |
| Swallowed after the hook chain | Raw + hook saw it, it is a global hotkey, but engine B reported no owner | Start **Key attribution** and re-test; if there is still no owner, use the comparison workflow on **Hook detection** and exit suspect software one by one |
| Delivered | All three layers saw it, or the global hotkey got an owner | The input path is fine; the problem is in the receiving software |
| Inconclusive | It is a global hotkey but the attribution engine was not running | This is not "no problem" — it means there is not enough evidence; start Key attribution and re-test |

**Stuck modifiers**: left/right Ctrl/Alt/Shift/Win are checked with `GetAsyncKeyState` and compared
with what the hook saw. "OS says down, hook saw no press" may simply mean the key was already held
when monitoring started — release and press it again to confirm.

**Remaps and drivers (read-only)**: the `Scancode Map` value under
`HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout` is parsed into a "press X → system produces Y"
list. Services under `HKLM\SYSTEM\CurrentControlSet\Services` that match `Type=1`, `Start≠4` and have a
keyboard-like group/driver name are **listed only**. The tool never judges, stops or unloads a driver.
## 8. Permissions and integrity

- **Administrator (UAC)**: when not run as administrator, attribution for processes running as administrator
  (e.g. PowerToys) is invisible. The top banner and the "Scan overview" page both offer a "Relaunch as
  administrator" button; from the command line use `--elevate` to relaunch elevated (original command-line
  arguments are preserved). If the user clicks No in UAC, the program recognizes it as "cancelled" and returns
  normally — it does not crash.
- **Low integrity (Low IL)**: if the exe sits in a directory carrying the `Mandatory Label\Low` label (e.g.
  some sync-drive workspaces), the process runs at low integrity. Consequences: writes to `%TEMP%` /
  `%LOCALAPPDATA%` are denied, and **UIPI blocks hook injection**, so engines B/C are largely useless.
  **Copy the exe to an ordinary directory (e.g. `C:\Tools\`) and run it from there.**
- **UIPI (User Interface Privilege Isolation)**: a low-integrity or non-elevated process cannot inject hooks
  into higher-integrity processes. This is a Windows security mechanism, not a bug in this tool.

---

## 9. Known limitations

- **32-bit coverage depends on a separate 32-bit host**: Windows does not let a 64-bit hook inject into a
  32-bit process, so 32-bit coverage comes from a companion started automatically when listening begins.
  If security software blocks it, or it cannot be extracted/started, that coverage is missing — the reason
  is in this program's log. Every attribution row and report row labels the target's bitness (x64 / x86).
- **Hook-based hotkeys do not go through WM_HOTKEY**: hotkeys implemented with keyboard hooks (Keyboard
  Manager, IMEs, AutoHotkey) neither appear in engine A's occupancy table nor trigger engine B's reports.
- **Only the current session is covered**: hotkeys in other login sessions are invisible.
- **Some config encodings are unverified**: the hotkey encodings of MSI Afterburner and Everything need a
  manual check.
- **Engines B/C install hooks**: this is by design, and only on explicit action.

---

## 10. Troubleshooting

**Q: The scan finds nothing / the numbers look too low?**
A: Make sure another program is not registering a mass of hotkeys at the same time; re-run from an ordinary
directory (low integrity affects both writing and probing).

**Q: "Start listening" errors out, or no process is ever visible?**
A: Most likely low integrity (copy the exe to an ordinary directory) or not being an administrator (use
"Relaunch as administrator" / `--elevate`).

**Q: The report cannot be written / access denied?**
A: Point the output path at a writable directory (e.g. the program's own folder). A low-integrity process
cannot write to `%TEMP%` or `%LOCALAPPDATA%`.

**Q: A key is free in engine A, but pressing it still does nothing?**
A: It is likely taken by a hook-based hotkey. Use engine B to listen and engine C to detect hooks.

**Q: How do I switch the UI to Chinese?**
A: Add `--lang zh` on the command line, or switch the language on the "Report & about" page / in the help
window (restart the program for it to take effect).

---

## 11. Uninstall / cleanup

HotkeyHound is a portable single-file program with **no installer and no registry writes**:

- Just delete `HotkeyHound.exe`.
- The program extracts the native DLL used by engine B (`HotkeyHound.Hook.dll`), the two 32-bit artifacts
  (`hooks\x86\HotkeyHound.Hook32.dll` and `HotkeyHound.Helper32.exe`) and `settings.ini` under
  `%LOCALAPPDATA%\HotkeyHound\` (or next to the exe). Deleting that directory is enough.
- Engine B's DLL is injected into processes in the current session; those injections **disappear naturally
  when the processes restart**, and **rebooting clears them completely**. When you stop listening, this program
  also calls `UnhookWindowsHookEx` to unload the hook.
