# Security policy

## What this project is (and is not)

HotkeyHound is a **diagnostic** tool for global hotkey conflicts on Windows. It reads what the
system and other applications declare about hotkeys, and — only when you explicitly ask — observes
the Windows input pipeline to find out which process is holding a key.

It is **not** a keylogger, and it is not a security boundary. Specifically:

- Engine A and D **never install anything**. They register hotkeys with `RegisterHotKey` and
  release them in the same instant, and they read configuration files. Nothing is written: no
  configuration file, no registry value, no scheduled task, no service.
- Engines B and C install hooks **only after the user presses a button that says so**. Loading the
  application installs nothing.
- The hook procedures **never swallow, remap, delay, or inject input**. They observe the event and
  pass it on with `CallNextHookEx`.
- The tool **never terminates, suspends, or modifies another process**. The comparison workflow in
  engine C asks the user to quit a suspect application; it does not do it for them.
- There is **no network code**. Nothing is uploaded, nothing phones home, no telemetry.

## The injected DLL

Engine B works by installing a global `WH_GETMESSAGE` hook. Windows therefore loads a small native
DLL (`HotkeyHound.Hook.dll`, built from `src/Hook/HookDll.cpp`) into every process of the session.
What it does there is deliberately minimal:

- it exports exactly one hook procedure and, inside it, only compares the retrieved message
  against `WM_HOTKEY`, posts one message to the controller when armed, and returns
  `CallNextHookEx`;
- `DllMain` performs no work beyond `DisableThreadLibraryCalls` — no allocation, no loader work, no
  handles;
- the shared-memory handshake with the controller is initialised once
  (`InitOnceExecuteOnce`), never on the hot path.

After a session the DLL remains mapped in the processes it was injected into until those processes
exit; a reboot clears it completely.

### Historical incident (fixed)

An early build passed a **managed delegate** to `SetWindowsHookEx` where the native procedure was
expected. Because the delegate's address is only valid inside the hosting process, every GUI
process crashed as soon as it retrieved a message (`0xc0000409`, faulting module `unknown`) — a
machine-wide crash. The fix and the hardening above are recorded in
[CHANGELOG.md](CHANGELOG.md) and guarded by a written rule in [AGENTS.md](AGENTS.md) so it cannot
be reintroduced by accident.

## Reporting a vulnerability

Open an issue, or if the report is sensitive, use GitHub's private vulnerability reporting on this
repository. Please include:

- what you ran (command line or UI steps), and whether the tool was elevated;
- the affected Windows version;
- what happened (screenshots, event log entries such as `Application Error` with event ID 1000,
  crash dumps if available).

Reports about the hook path are especially valuable — that is where the risk of this design lives.

## Verifying the tool yourself

- `HotkeyHound.exe --selftest --no-hooks` exercises everything except the hooks.
- `HotkeyHound.exe --selftest` includes the hook tests (T6/T7) and prints explicit evidence
  (hook handles, the attribution it received, verdict branches).
- Nothing in the tool requires network access. If you see it trying to talk to the network, that
  is a bug — please report it.
