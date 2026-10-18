# HotkeyHound

**English** · [简体中文](README.zh-CN.md)

# From the author: what does this software do?

**Find out who stole your keyboard shortcut — and prove it.**

HotkeyHound is a Windows tool for diagnosing global hotkey conflicts. It answers three questions
that Windows itself refuses to answer: *which combinations are already taken*, *which process is
holding them*, and *why a key you pressed did nothing at all*.

*Co-developed with Kimi Code.*

![HotkeyHound — a five-page tour](docs/demo.gif)

*A five-page tour: occupancy scan → press attribution → hook detection → truth table → report.*

---

## How it compares

Four tools in this space are alive today, and they answer different questions. This table is meant
to be honest — where a competitor does something better, it says so.

| | HotkeyScan | Hotkey Screener | Hotkey Detective | **HotkeyHound** |
|---|---|---|---|---|
| Enumerate occupied combinations | 761 | lists registered hotkeys | — | **1650** |
| Name the owner of a registered hotkey | infers it from what changes on screen | global hook + synthetic press | global hook, you press | **global hook, you press — or an opt-in batch press** |
| Keys eaten by a **keyboard hook** | only when the press causes something visible | documented blind spot | documented blind spot | **dedicated detector + config catalog, with evidence levels** |
| Explain *why* a combination is taken | reads PowerToys / ShareX / OBS / AHK configs | — | — | **reads six app families, and prints the config path it used** |
| Key monitor (stuck modifiers, remaps, filter drivers) | **yes** | — | — | not yet |
| 32-bit processes | 64-bit only | **32 + 64** | ships x86 and x64 | x64 only (roadmap) |
| Report you can hand to someone | — | — | — | **self-contained HTML, bilingual** |
| Licence | MIT | closed source | GPL-3.0 | MIT |

In practice: **HotkeyScan is the broader keyboard-health toolkit** (its key monitor is genuinely
useful), **Hotkey Screener is the most direct attributor for registered hotkeys**, and
**HotkeyHound is the one that separates the three reasons a shortcut does nothing** — claimed by a
registration, eaten by a hook before it ever reaches the system, or simply not a global shortcut at
all — and shows its evidence for every conclusion instead of asserting one.

Two boundaries nobody in this space can cross: **hook-based shortcuts and in-app shortcuts cannot be
listed**, because Windows keeps them out of every table it has. A tool that claims otherwise is
guessing; this one tells you the difference between "measured", "read from a config" and "known
default".

Older options exist too — NirSoft's HotKeysList (lists registrations, names no owners, listed up to
Windows 8) and kernel-level anti-rootkit tools like PCHunter or OpenArk, which can read win32k's
hotkey table but need an unsigned driver, are blocked by Secure Boot / HVCI on current Windows, and
still cannot see hooks.

---


## The problem it was built for

A machine with a fair amount of software installed — PowerToys, an input method, two or three chat
apps, an OEM control centre, a GPU overlay — has dozens of global hotkeys registered. Push one of
them and sometimes nothing happens; sometimes the wrong thing happens. Windows gives you no way to
see the list, and no way to see who owns an entry.

Existing tools (like the well-known Hotkey Detective) require you to press the key manually and
show you a process — but they cannot tell you *which combinations are taken* in the first place,
and they are blind to hotkeys implemented with **keyboard hooks** rather than `RegisterHotKey`,
which is exactly the category that swallows keys silently.

## The four engines

| Engine | What it does | Installs a hook? | Needs admin? |
|---|---|---|---|
| **A — Occupancy scan** | Probes every modifier × key combination (~1650 of them) with `RegisterHotKey` and reports which ones are taken. Successful registrations are released immediately, so the scan has no side effects. | No | No |
| **B — Press attribution** | Installs a global message hook; when a hotkey owner receives its command, the injected DLL reports *which process* received it. | **Yes** | Recommended |
| **C — Hook-swallow detection** | Installs a low-level keyboard hook, then judges whether a key you pressed never reached the system (swallowed by an upstream hook) or reached it but was intercepted before the owner saw it. | **Yes** | Recommended |
| **D — Config aggregation** | Reads hotkey settings from software that stores them in plain files (PowerToys, Everything, OBS, NVIDIA ShadowPlay registry, Wallpaper Engine, MSI Afterburner) and diffs them against the measured results. | No | No |

Engine A and D together produce a **truth table** with four kinds of rows:
- declared **and** measured (the software really holds it)
- declared but **not** registered (the software is not running, or it uses a hook — the tool says so)
- registered but **nobody claims it** (configs that cannot be read: input methods, chat apps, drivers)
- free (safe to claim — the report lists concrete candidates)

On top of that sits a **hook-suspect catalog**. For software that implements shortcuts with a
keyboard hook — PowerToys Keyboard Manager, input methods, QQ/WeChat, AutoHotkey scripts, vendor
control centres, screen-mirroring tools — no system table will ever list the keys. HotkeyHound reads
their configuration where it can, checks whether the software is running, and reports a *suspect*
with an explicit evidence level (measured / read from config / known default / reference only) and
the config path it used. It never presents a guess as a fact.


## Quick start

1. Download `HotkeyHound.exe` from [Releases](../../releases) and run it. No installer, no .NET
   installation required (.NET Framework 4.8 ships with Windows 10/11).
2. **Scan tab → 开始扫描 / Scan.** Read the truth table, then export an HTML report.
3. If a key misbehaves, use **Press attribution**: click *Start listening*, press the key, and read
   which process received it. Run as administrator to also see elevated apps (PowerToys, for example).
4. If the key does not show up at all, use **Hook detection**: it tells you whether the key was
   swallowed before it reached the system.

### Command line

```
HotkeyHound.exe                                   # graphical interface
HotkeyHound.exe --scan --report out.html          # engines A + D, HTML report
HotkeyHound.exe --listen --seconds 60             # attribution console (engine B)
HotkeyHound.exe --lang en --scan --report en.html # English output
HotkeyHound.exe --elevate --listen                # restart as administrator, then listen
HotkeyHound.exe --selftest                        # self-test (installs hooks; see safety)
HotkeyHound.exe --selftest --no-hooks             # self-test without touching hooks
HotkeyHound.exe --attribute-all --dry-run         # plan a batch attribution, press nothing
HotkeyHound.exe --attribute-all --yes             # press each occupied hotkey, record the owner
```

## Safety model

This tool touches input handling, so its behaviour is deliberately narrow:

- **Engine A and D never install anything.** They register hotkeys and release them in the same
  instant; they read configuration files. They never write configuration or the registry.
- **Engines B and C install hooks only when you explicitly ask.** The pages say so in plain
  language, and the buttons are labelled with *（安装钩子）* / *(installs a hook)*. Loading the
  application installs nothing.
- **Hooks never swallow keys.** The hook procedures observe and pass everything through
  (`CallNextHookEx`); the tool does not remap, block, or delay input.
- **The tool never ends, suspends, or modifies other processes.** The "comparison" workflow only
  records observations and asks you to quit a suspect application yourself.

### About the injected DLL, and an incident worth disclosing

Engine B works by installing a global `WH_GETMESSAGE` hook, which means Windows loads a small
native DLL into every process in your session. During development, an early build passed a
**.NET managed delegate** where the hook procedure was expected. The address of that delegate is
only valid inside our own process; once injected elsewhere it is a wild pointer, and the result was
every GUI process crashing at once (`0xc0000409`, faulting module `unknown`) — a machine-wide crash.

The fix (locating the native export with `GetProcAddress` and passing *that* pointer) plus the
hardening applied afterwards — an empty `DllMain`, one-shot initialisation instead of a critical
section on the hot path, no allocation inside the hook procedure — were verified on a real machine:
hooks installed and uninstalled repeatedly, **zero crash events**, and a genuine attribution
(`Win+R → explorer.exe`) captured end to end. The incident is documented in the changelog rather
than quietly dropped, because anyone auditing a hook-based tool deserves to know its history.

After a hook session, a copy of the DLL stays loaded in processes that were injected until those
processes exit; a reboot clears it. The tool tells you this in the interface.

## Known limitations

- **Elevated processes require running HotkeyHound as administrator.** Otherwise their hotkeys are
  attributed to nobody (the UI explains this and offers a restart button).
- **32-bit processes are not covered by attribution**: the injected DLL is x64. Engine A and D are
  unaffected.
- **Hook-based hotkeys are invisible to the occupancy scan** (they never call `RegisterHotKey`).
  That is precisely why engines B and C exist; the report always prints this caveat.
- Probes are **per session**: hotkeys registered in another logon session are not visible.
- Verification was done on Windows 11 26100 and Windows 10 19045; other versions should work but
  have not been tested.

## Build from source

The build is deliberately dependency-free: the development machine has **no .NET SDK**, so the
project compiles straight from the Visual Studio toolchain.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` compiles the native hook DLL with MSVC (`cl /LD /MT`), then compiles the C# sources
with the Roslyn `csc.exe` that ships with Visual Studio (`.NET Framework 4.8`, `-langversion:7.3`,
`-nostdlib+` with explicit runtime references), and embeds the DLL as a resource. Output:
`dist\HotkeyHound.exe`, a single file. No NuGet packages, no downloads.

Layout:

```
src\Program.cs            entry point, CLI parsing
src\Core\                 engines, models, report builder, settings
src\Core\Adapters\        per-software config readers (engine D)
src\Ui\                   WinForms pages
src\Hook\HookDll.cpp      native hook DLL (engine B)
tests\SelfTest.cs         T1–T7, driven by --selftest
docs\                     help documents (Chinese and English)
```

## Screenshots

<p align="center">
  <img src="docs/screenshot-overview.png" width="46%" alt="Occupancy scan: 206 occupied combinations, 54 declared, 1651 truth-table rows" />
  <img src="docs/screenshot-truth.png" width="46%" alt="Truth table grouped by declaration status" />
</p>

## Documentation

- [`docs/help.zh-CN.md`](docs/help.zh-CN.md) / [`docs/help.en-US.md`](docs/help.en-US.md) — in-depth
  guides: what each engine does, how to read the report, how to chase a "key does nothing" problem,
  permissions and integrity levels, troubleshooting.
- [`CHANGELOG.md`](CHANGELOG.md) — version history.
- In the application: press **F1**.

## License

[MIT](LICENSE) © 2026 @Conste11ation117
