# 分发用的回答稿（outreach）

两条原则，别违反：

1. **回答本身必须独立成立**——就算把工具链接删掉，读者照着做也能解决问题。
2. **必须披露作者身份**（"Full disclosure: I wrote this"）。Super User / Stack Overflow 对
   未披露的自我推广非常敏感，一次踩雷会让账号被降权。宁可少一个链接，也不要硬广。

下面两稿针对两个常年高流量的入口问题。**不要原样复制到两个站**——同一段文字出现在两处会被
判定为跨站灌水；第二稿是给开发者看的，角度完全不同。

---

## 一、Super User（终端用户视角）

问题：*How do I find out which programs have registered global hotkeys in Windows 10?*

> Windows does not offer any way to list registered hotkeys, and that is not an oversight — the list
> lives inside win32k and is not exposed through any documented API. So there are only two practical
> routes, plus one case that neither route can see.
>
> **1. Probe every combination.** A global hotkey is a claim: `RegisterHotKey` fails with
> `ERROR_HOTKEY_ALREADY_REGISTERED` (1409) if somebody already holds that combination. So you can
> walk all modifier × key combinations (about 1650 of them), record which ones refuse to register,
> and immediately unregister the ones that succeed. That gives you the occupancy list without asking
> any application for permission. Two caveats: it is *session-scoped* (hotkeys registered in another
> logon session are invisible), and it tells you the combination is taken, not who took it.
>
> **2. Press the key and watch who receives it.** Registering a hotkey makes Windows post `WM_HOTKEY`
> (0x0312) to the owning thread. If you install a global `WH_GETMESSAGE` hook, the OS injects your DLL
> into every process, and when that message is retrieved you can report the process id. This gives you
> the answer "explorer.exe holds Win+R". It needs administrator rights to see elevated processes, and
> it only observes the key you actually press.
>
> **3. The case neither route covers: keyboard hooks.** PowerToys Keyboard Manager, input methods,
> AutoHotkey and several game utilities implement shortcuts with a low-level keyboard hook. They never
> call `RegisterHotKey`, so route 1 reports the combination as *free* — while in reality the key is
> swallowed before it ever reaches the system. If a key does nothing and nothing shows up as occupied,
> check those applications' own settings first, and try a tool that installs its own low-level hook to
> see whether your keypress reaches the system at all.
>
> **Practical order to check on a machine like yours** (most common culprits first): PowerToys Keyboard
> Manager remaps → the input method → GPU overlay (NVIDIA `Alt+Z`, `Alt+F1`, `Alt+F9`) → the vendor's
> control centre utility → chat apps' screenshot hotkeys.
>
> Full disclosure: I wrote a small open-source tool that does all three — [HotkeyHound](https://github.com/Conste11ation117/HotkeyHound)
> (MIT, single executable). The method above works equally well with any equivalent utility; you can
> reproduce route 1 with a twenty-line script if you prefer.

**发布位置**：找到该问题下最新的回答（2016 年的问题，仍有访问量）。回答时**只讲方法**，工具链接
放在最后并带披露句。不要提"star"、不要要求投票。

---

## 二、Stack Overflow（开发者视角）

问题：*Find out what process registered a global hotkey? (Windows API)*

> There is no supported API for this. The hotkey table lives in win32k and is not exposed; the
> documented surface only lets you *register* or *unregister* your own hotkeys. Two techniques are
> used in practice, and they answer different questions.
>
> **Probing for occupancy.** `RegisterHotKey` is exclusive: if the call fails with
> `ERROR_HOTKEY_ALREADY_REGISTERED` (1409), someone else holds that combination. So enumerate the
> combinations you care about, register with `MOD_NOREPEAT`, and unregister immediately when it
> succeeds — the whole sweep is a few milliseconds and has no side effects:
>
> ```csharp
> foreach (var vk in keys)
> foreach (var mods in modifierSets) {
>     if (RegisterHotKey(IntPtr.Zero, id, mods | MOD_NOREPEAT, vk)) {
>         UnregisterHotKey(IntPtr.Zero, id);              // free
>     } else if (GetLastError() == 1409) {
>         // taken — but you still don't know by whom
>     }
> }
> ```
>
> **Observing delivery.** To learn the owner, hook message retrieval: a global
> `SetWindowsHookEx(WH_GETMESSAGE, proc, hModule, 0)` makes Windows load your DLL into every process
> in the session; when a process retrieves `WM_HOTKEY` (0x0312), the `lParam` carries the modifiers and
> virtual key, and `GetCurrentProcessId()` inside the hook is your answer. Install it on a thread with
> a message loop, and never swallow the message (`CallNextHookEx`).
>
> **The pitfall that will bite you.** The hook procedure must be an address *inside the injected DLL*.
> In .NET it is tempting to write `SetWindowsHookEx(WH_GETMESSAGE, new HookProc(...), hMod, 0)` — the
> marshalled delegate works fine for a thread-local hook because the stub lives in your own process.
> For a **global** hook it is a wild pointer in every other process, and the failure mode is not an
> exception: every GUI process that retrieves a message dies at once (`0xc0000409`, faulting module
> `unknown`). Always `GetProcAddress` the exported procedure and pass that `IntPtr`, and keep
> `DllMain` empty (no loader work, no critical sections, no allocation).
>
> Two more constraints worth knowing: a 64-bit hook DLL covers only 64-bit processes (ship an x86
> build of both sides if you need 32-bit coverage), and hooks cannot inject into processes of higher
> integrity than yours (UIPI) — run elevated if you need elevated targets.
>
> Full disclosure: this is the mechanism behind [HotkeyHound](https://github.com/Conste11ation117/HotkeyHound),
> which I wrote; `src/Hook/HookDll.cpp` and `src/Core/AttributionEngine.cs` are a readable reference
> implementation of both techniques (MIT).

**发布位置**：那是 2009 年的问题，已有多个答案。新增回答的价值全在"探测法 + 托管委托陷阱"这两段
——直接写"看我的项目"必被删。发之前先读一遍现有答案，确认你给的信息确实没被说过。

---

## 三、其它渠道的写法（不做硬广）

- **Reddit**（r/Windows11 / r/software / r/PowerToys）：帖子里先讲你遇到的真实症状与排查过程，
  工具作为"我最后写了个东西解决它"出现。标题用症状，不要用产品名。
- **Show HN**：标题 *Show HN: A hotkey conflict inspector that doesn't pretend Windows can enumerate hotkeys*；
  正文讲"为什么系统 API 做不到、我们怎么绕、踩了什么坑（托管委托那次全机崩溃）"。HN 喜欢踩坑，
  讨厌营销。
- **V2EX / 少数派 / 知乎**：中文搜索词是「全局热键 冲突」「快捷键被占用」「按了没反应」。
  先给可操作步骤（PowerToys 键盘管理器 → 输入法 → 显卡覆盖层 → 厂商工具），再给工具。
- **awesome-* 清单**：按清单的贡献指南提 PR，一行描述 + 链接，不要写宣传语。
