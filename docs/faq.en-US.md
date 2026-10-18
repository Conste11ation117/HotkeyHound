# FAQ — HotkeyHound

Answers to the questions people actually search for. If you are looking for the two long-form
write-ups aimed at Super User and Stack Overflow, see [`outreach.md`](outreach.md).

## My shortcut does nothing. How do I find out why?

Four steps, cheapest first:

1. **Is the combination taken at all?** Run `HotkeyHound.exe --scan`. Engine A probes ~1650
   combinations with `RegisterHotKey` and reports which ones are already claimed. A successful probe
   is released in the same instant, so the scan has no side effects.
2. **Who claims it?** Still in the report, the truth table says whether the combination comes from a
   software config it could read (PowerToys, Everything, OBS, NVIDIA, Wallpaper Engine, MSI
   Afterburner) — and prints that config file's path.
3. **Is it eaten before it reaches Windows?** If the scan says *free* but the key still does nothing,
   a keyboard hook is the likely culprit. `--attribute-all --dry-run` shows what a batch test would
   press; `--attribute-all --yes` presses each occupied combination and records the owner. The
   valuable outcome is **"pressed but nobody responded"** — that means something swallowed the key.
   `--monitor` (or the Key monitor tab) then tells you which layer swallowed it.
4. **Is it the keyboard, the driver, or the software?** The key monitor compares three layers for the
   same press: Raw Input (did the key leave the device?), the low-level hook chain, and the window
   message layer. A raw event with no hook event means an upstream hook ate it; no raw event at all
   means the hardware, firmware or a keyboard filter driver never sent it.

## Why does the scan say "free" when the key clearly does something?

Because that something does not register a hotkey. PowerToys Keyboard Manager, input methods,
AutoHotkey and several vendor utilities use a **low-level keyboard hook**: the key never reaches
Windows' hotkey dispatch, so there is nothing for `RegisterHotKey` to collide with. This is the
single most common source of confusion, and it is why HotkeyHound ships a hook detector and a
config catalog instead of only a scan.

## Why can't Windows itself tell me who holds a hotkey?

It can, internally — the hotkey table lives in win32k — but no documented API exposes it.
`RegisterHotKey` has no counterpart that reads back the owner; the only public signal is the failure
code `ERROR_HOTKEY_ALREADY_REGISTERED` (1409). Everything else is inference: probe, observe delivery,
or read the owner's configuration.

## So how do other tools do it?

- **Hotkey Screener** installs a global message hook, *synthesises* a press of the hotkey, and watches
  which application reacts. Effective, but detection really executes those shortcuts.
- **Hotkey Detective** installs hooks into every process and waits for you to press the key.
- **Kernel anti-rootkit tools** (PCHunter, OpenArk) read win32k's table directly, but they need an
  unsigned driver, are blocked by Secure Boot/HVCI on current Windows, and still cannot see hooks.
- **HotkeyHound** does all of it in user mode and, unlike the others, also reads the suspect's config
  so the report can say *why* a combination is taken, with an evidence level per conclusion.

## Does it log my keystrokes, or send anything anywhere?

No network code at all — nothing is uploaded, there is no telemetry, no analytics, no update check.
The hook procedures observe events and pass them on; they never store what you type. The key monitor
stores only the keys you explicitly press while the monitor is running, and only in memory (plus
whatever you export), not to disk.

## Do I need administrator rights?

Engine A, engine D and the report: no. Attribution of hotkeys held by **elevated** processes
(PowerToys is one) needs HotkeyHound to run elevated, because Windows blocks hook injection from a
lower integrity level into a higher one (UIPI). The interface detects this and offers a restart
button; the command line has `--elevate`.

## Which processes can it see?

- **64-bit processes**: always (as far as integrity allows).
- **32-bit processes**: supported since 1.3.0. Windows only lets a hook inject into processes of the
  same bitness, so HotkeyHound starts a small 32-bit companion process that installs a 32-bit hook.
  Both report into the same result table, and each row says which bitness it came from.
- **Processes of a different logon session**: not visible — hotkeys are session-scoped.

## Is it safe? It installs a hook DLL into every process.

The injected DLL does as little as possible: one exported hook procedure that compares a message
against `WM_HOTKEY`, posts one message to the controller, and passes the event on with
`CallNextHookEx`. `DllMain` performs no work beyond `DisableThreadLibraryCalls`; the shared-memory
handshake is initialised once. Hooks are installed **only** when you press a button that says so.

This project is also honest about its own history: an early development build passed a **managed
delegate** where the native hook procedure belongs, which crashed every GUI process at once. The fix,
the hardening and the rule that prevents a recurrence are all documented in
[CHANGELOG.md](../CHANGELOG.md) and [AGENTS.md](../AGENTS.md).

## How do I get rid of it completely?

Close it and delete the executable — it is a single file with no installer. Two leftovers are
possible: `%LOCALAPPDATA%\HotkeyHound\` (extracted hook DLLs and settings) and the hook DLL still
being loaded inside processes that were injected during a session. Both disappear on reboot; the
folder can be deleted by hand afterwards.

## Why not use a kernel driver? It could enumerate everything.

A driver can read the hotkey table but **cannot see hooks**, which is the hardest and most common
category. Paying for an unsigned driver (blocked by Secure Boot and HVCI, flagged by anti-cheat and
antivirus) while still missing the main case is a bad trade. Reading configuration is a plain,
auditable alternative that also tells the user which file to edit.

## The tool says "known default" — what does that mean?

That the hotkey comes from the software's documented defaults, because its configuration could not be
read on this machine (encrypted, binary, or locked by a running process). It is marked as such
instead of being presented as measured fact. The four levels are: measured, read from config, known
default, reference only.
