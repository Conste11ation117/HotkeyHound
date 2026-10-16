# HotkeyHound — 工程设计说明（SPEC）

> 本文件是实现的唯一契约。任何实现决定与本文冲突时，以本文为准；确有必要偏离时，先更新本文再改代码。

## 1. 目标与交付物

一个 Windows 程序，用来排查全局快捷键冲突。交付：

- `dist\HotkeyHound.exe` — 单文件，中文图形界面；命令行模式可用；内嵌原生 hook DLL（运行时自动提取到 `%LOCALAPPDATA%\HotkeyHound\hooks\x64\`）。
- `report-*.html` — 自包含 HTML 报告（内联 CSS/JS，无外链、无 CDN）。
- `build.ps1` — 一键构建；`README.md` — 中文使用说明。

四个引擎：

| 代号 | 名称 | 作用 | 是否需要管理员 |
|---|---|---|---|
| A | 占用扫描 | 试探全部「修饰键 × 按键」组合，产出占用/空闲地图 | 否 |
| B | 按键归属 | 注入 hook DLL，用户按下热键后报出是哪个进程持有 | 建议（否则看不到提权进程，如 PowerToys） |
| C | 钩子窃取检测 | 用 LL 键盘钩子判断「按键被上层钩子吞掉」+ 对照排除工作流 | 建议 |
| D | 配置聚合 | 解析已知软件的热键配置，与 A/B 实测结果做 diff，产出真相表 | 否 |

## 2. 环境约束（这台机器）

- **没有 .NET SDK**。C# 用 VS 18 的 Roslyn 编译器：
  `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe`
- **没有 .NET Framework 引用程序集**（`C:\Program Files (x86)\Reference Assemblies` 缺失）。
  因此必须 `-nostdlib+` 并显式引用运行时程序集（在 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`）：
  `mscorlib.dll, System.dll, System.Core.dll, System.Drawing.dll, System.Windows.Forms.dll, System.Xml.dll`
- 原生 DLL 用 MSVC 14.51：先 `call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"`，
  cl.exe 在 `...\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\`。Windows SDK 10.0.26100.0。
- 目标框架：**.NET Framework 4.8**（Windows 11 自带，用户无需装运行时）。`-langversion:7.3`、`-platform:x64`。
- **禁止 nuget/联网下载**。JSON 解析自己写（`src/Core/JsonLite.cs`，约 200 行，够用即可）。
- 构建必须能在 PowerShell 里以 `powershell -ExecutionPolicy Bypass -File build.ps1` 一条命令跑通。

## 3. 工程目录

```
HotkeyHound\
  SPEC.md  README.md  build.ps1
  src\Program.cs                 入口；CLI 解析；无参数时启动 GUI
  src\Core\KeyCombo.cs           组合键模型 + VK 表 + 解析/格式化
  src\Core\JsonLite.cs           极简 JSON 解析
  src\Core\OccupancyScanner.cs   引擎 A
  src\Core\ConfigCollector.cs    引擎 D 调度
  src\Core\Adapters\*.cs         引擎 D 各适配器（见 §5）
  src\Core\Reconciler.cs         A×D 真相表
  src\Core\ReportBuilder.cs      HTML 报告
  src\Core\AttributionEngine.cs  引擎 B 控制器（chunk 2）
  src\Core\HookSniffer.cs        引擎 C（chunk 2）
  src\Core\SharedState.cs        全局状态/日志（线程安全，供 UI 订阅）
  src\Hook\HookDll.cpp           原生 hook DLL（chunk 2）
  src\Ui\MainForm.cs             WinForms 主界面
  src\Ui\*Tab.cs                 各标签页
  tests\SelfTest.cs              --selftest 的实现
  build\  dist\                  产物（build.ps1 生成）
```

## 4. 数据模型（字段名固定，两块实现共用）

```csharp
public sealed class KeyCombo {
  public bool Win, Ctrl, Alt, Shift;   // 修饰键
  public uint Vk;                      // 虚拟键码
  public string Display();             // "Ctrl+Alt+M"（固定顺序 Win+Ctrl+Alt+Shift）
  public string Normalize();           // Display() 的规范形式，用作字典键
  public static KeyCombo Parse(string s);          // "Ctrl+Alt+M" / "Win+Shift+F23"
  public static KeyCombo FromVkList(string s);     // PowerToys 的 "162;164;66" 形式（VK 十进制列表）
}

public sealed class OccupancyResult { public KeyCombo Combo; public bool Occupied; public uint Win32Error; }

public sealed class DeclaredHotkey {
  public string Software;      // "PowerToys"
  public string Module;        // "Keyboard Manager" / "Peek" / "配色器"
  public string Purpose;       // 人类可读用途："打开记事本" / "快速预览文件"
  public KeyCombo Combo;
  public string RawValue;      // 原始配置值原文
  public string SourcePath;    // 配置文件绝对路径
  public bool IsDefaultValue;  // 无法判断时填 false
}

public enum TruthKind { MatchesDeclared, DeclaredButNotRegistered, RegisteredButUnowned, FreeAndUndeclared }
public sealed class TruthRow {
  public KeyCombo Combo; public TruthKind Kind;
  public string DeclaredBy;    // 声明来源（软件/模块），可能为空
  public string OwnerHint;     // 已知归属提示（如系统保留、NVIDIA 默认键）
  public string Evidence;      // "配置文件" / "实测注册" / "两者"
  public string Note;
}
```

## 5. 各引擎规格

### 引擎 A — 占用扫描（`OccupancyScanner.cs`）

- 修饰键组合取 15 种：Alt、Ctrl、Ctrl+Alt、Shift、Shift+Alt、Shift+Ctrl、Shift+Ctrl+Alt、Win、Win+Alt、Win+Ctrl、Win+Ctrl+Alt、Win+Shift、Win+Shift+Alt、Win+Shift+Ctrl、Win+Shift+Ctrl+Alt。
- 按键集合：A–Z、0–9、F1–F24、Backspace、Tab、Enter、Esc、Space、Home、End、PgUp、PgDn、Left/Up/Right/Down、Ins、Del、PrintScreen、Pause、Help、Win 左右、Apps、`,/<`、`.>`、`/?`、`;:`、`'`、`` `~ ``、`[{`、`]}`、`\|`、`-_`、`=+`、Num*、Num+、Num-、Num/、NumLock、ScrollLock、CapsLock（约 110 个）。
- 对每个组合调用 `RegisterHotKey(IntPtr.Zero, id, mods | MOD_NOREPEAT, vk)`：成功 → 立即 `UnregisterHotKey` → 记为**空闲**；失败且 `GetLastError()==1409`（ERROR_HOTKEY_ALREADY_REGISTERED）→ 记为**已占用**；其他错误码 → 记为已占用并在结果里带上错误码。
- 结果按「已占用 / 空闲」两组返回；统计数字要与实测一致（本机基线：约 229 个占用）。
- 必须无副作用：不得改动系统设置，不得长期持有任何注册（注册成功必须立刻注销）。
- 进度回调：`Action<int,int>`（已完成数、总数），供 UI 进度条使用。

### 引擎 D — 配置聚合（`ConfigCollector.cs` + `Adapters\*.cs`）

适配器接口：`interface IConfigAdapter { string Software { get; } IEnumerable<DeclaredHotkey> Collect(); }`

必须实现的适配器：

| 适配器 | 数据源 | 要点 |
|---|---|---|
| PowerToysModules | `%LOCALAPPDATA%\Microsoft\PowerToys\*\settings.json`（含根 settings.json） | 递归遍历 JSON，凡出现 `{win,ctrl,alt,shift,code}` 形状的对象即视为一个热键；`code` 为 VK；用途取 JSON 键名（如 `open_powerlauncher`、`ActivationShortcut`）；`%...%\settings.json` 的 `enabled` 字段决定模块是否启用，未启用要在 Note/IsDefaultValue 之外单独标注（用 Purpose 前缀 `[已禁用]`） |
| PowerToysKeyboardManager | `...\Keyboard Manager\default.json` | `remapShortcuts.global[]` 每条：`originalKeys`（如 `162;164;66` = Ctrl+Alt+B）、`operationType`（0=改键到 newRemapKeys，1=运行程序 runProgramFilePath）。用途写成 `重映射 → <目标>` 或 `运行 <exe文件名>`；这是本机最高优先级的冲突源，必须完整还原 |
| Everything | `%APPDATA%\Everything\Everything.ini` | `new_window_key`、`show_window_key`、`toggle_window_key`；值为 `0` 表示未设置，不要产出该条 |
| ObsStudio | `%APPDATA%\obs-studio\global.ini`、`%APPDATA%\obs-studio\basic\profiles\*\basic.ini` | `[Hotkeys]` 段，键=`OBSBasic.XXX`，值形如 `Ctrl+Alt+Shift+F9`；值为空/`None` 跳过 |
| NvidiaShadowPlay | 注册表 `HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS` | 键名如 `GFEOverlayHKeyV2x`、`ScreenshotHKey0/1`、`ManualHKey`、`PMOCOverlayHKey`、`AnselUIHKey`、`FSUIHKeyV1`；值形如 `0x12;0x5A`（十六进制 VK 列表），`0x12`=Alt。解析为组合键 |
| WallpaperEngine | 安装目录 `config.json`（从 `%APPDATA%` 或 `D:\steam\steamapps\common\wallpaper_engine\` 找）+ `HKCU\Software\WallpaperEngine` | 只取含 `hotkey` 的键；找不到就产出 0 条，不报错 |
| MsiAfterburner | `C:\Program Files (x86)\MSI Afterburner\Profiles\MSIAfterburner.cfg` | `*Hotkey=` 行；值为 `00000000h` 视为未设置 |
| SystemReserved | 内置静态表 | Windows 自身占用的常用组合（Win+L、Alt+Tab、Win+数字 等）作为「已知归属提示」提供给真相表，**不**算「某个软件声明」 |

通用要求：所有适配器**只读**，任何异常都要吞掉并记录到日志，不能因为一个配置文件损坏导致整轮失败；不存在的路径静默跳过。

### 真相表（`Reconciler.cs`）

以 A 的扫描结果（全部约 1650 个组合）为底表，逐条对 D 的声明做匹配：

- 已占用 + 有声明 → `MatchesDeclared`
- 空闲 + 有声明 → `DeclaredButNotRegistered`（典型原因：该软件用键盘钩子、或当前未运行；Note 里写这两种可能性）
- 已占用 + 无声明 → `RegisteredButUnowned`（Note 提示可能是 IM/输入法等配置不可读的软件，并列出当前运行的候选进程名，来自静态候选表：QQ、Weixin、WeChatAppEx、wetype_*、Doubao、cloudmusic、mailmaster、ToDesk、ldplayer、UU、讯飞、zhipu 等）
- 空闲 + 无声明 → `FreeAndUndeclared`（报告里只展示统计数字 + 抽样前 30 个，供用户挑键位）

匹配规则用 `KeyCombo.Normalize()` 精确匹配；额外做一次「忽略 Shift 差异」的模糊匹配，把结果写进 Note（例如声明 `Ctrl+Space`、实测占用 `Ctrl+Shift+Space` 时提示"可能有 Shift 变体"）。

### 引擎 B — 按键归属（chunk 2）

- 原生 DLL `HookDll.cpp`，导出：
  `extern "C" __declspec(dllexport) LRESULT CALLBACK HotkeyHoundHookProc(int nCode, WPARAM wParam, LPARAM lParam);`
- DLL 在同目录 `HotkeyHound.Ctl` 共享内存（`Local\HotkeyHound.Ctl`，结构 `{ DWORD magic; HWND ctl; DWORD arm; }`，magic=`0x484B5354`）里读控制窗口句柄；在 hook 里遇到 `nCode == HC_ACTION && wParam == PM_REMOVE` 且 `((MSG*)lParam)->message == WM_HOTKEY(0x0312)` 时，
  `PostMessage(ctl, WM_APP+1, (WPARAM)GetCurrentProcessId(), (LPARAM)((MSG*)lParam)->lParam)`，然后照常 `CallNextHookEx`（绝不吞键）。
- 控制器 `AttributionEngine.cs`：把 DLL 释放到 `%LOCALAPPDATA%\HotkeyHound\hooks\x64\HotkeyHound.Hook.dll`，`LoadLibrary` → `GetProcAddress` → `SetWindowsHookEx(WH_GETMESSAGE=3, proc, hMod, 0)`，在独立线程上跑消息循环（该线程必须 pump 消息，否则全局钩子会挂死系统）。
- 收到 `WM_APP+1` 后解析 pid 与 lParam（lParam 低位=修饰键、高位=VK，映射成 `KeyCombo`），解析进程名/路径/是否提权，写入日志与结果表，并回调 UI。
- 关闭时必须 `UnhookWindowsHookEx`；DLL 句柄在程序退出时随进程结束。
- 已知限制（要写进报告与 UI 提示）：x64 DLL 覆盖不到 32 位进程；非管理员看不到提权进程（PowerToys 就属于此类，UI 要提供「以管理员身份重启」按钮）。

### 引擎 C — 钩子窃取检测（chunk 2）

- `HookSniffer.cs` 在独立线程装 `WH_KEYBOARD_LL(13)`（本进程内，托管代码即可），记录每次按键的 combo 与时间戳。
- 判定规则（对同一个组合 X，在「布防后 3 秒内」）：
  1. 我们的 LL 钩子**根本没看到** X 按下 → `SwallowedUpstream`：「按键被你机器上某个更上层的钩子吞掉了，没能到达本程序」。
  2. 看到了 X，且 X 在 A 的结果里是已占用，但 1.5 秒内**没有** B 的归属报告 → `SwallowedBeforeHotkey`：「按键到达了系统，但持有者没收到命令——疑似被钩子截胡」。
  3. 看到了 X 且收到 B 报告 → `Delivered`：归属明确。
  4. 看到了 X，X 不在 A 的已占用表里 → `NotGlobal`：「这不是全局热键，只是当前窗口自己处理的按键」。
- 对照排除工作流：静态候选表（PowerToys.KeyboardManagerEngine、微信输入法 wetype_*、ToDesk、豆包、讯飞、UU、雷电模拟器、NVIDIA Overlay 等）列出候选，界面给出分步指引：「先记录当前结果 → 退出该软件 → 再测一次 → 记录对比」；工具只记录与对比，**不自动结束或暂停任何进程**（安全考虑，v1 明确不做）。

## 6. CLI 契约

```
HotkeyHound.exe                     启动图形界面
HotkeyHound.exe --scan [--report <path.html>] [--json <path.json>]
                                    跑引擎 A+D，生成报告（默认 report-yyyyMMdd-HHmmss.html 于当前目录）
HotkeyHound.exe --listen [--seconds N]
                                    控制台模式下做按键归属监听（引擎 B），N 默认 30 秒
HotkeyHound.exe --selftest          自测，全部通过退出码 0，否则非 0 并打印失败项
HotkeyHound.exe --help
```

## 7. HTML 报告规格

单文件、UTF-8、内联 CSS/JS、中文。必须包含（用 `id` 锚点，便于自测断言）：

1. 头部：生成时间、机器名、是否管理员、HotkeyHound 版本、四个引擎的结果统计。
2. `#occupancy` 占用地图：表格（组合键 / 状态 / 归属或声明来源 / 证据 / 备注），带搜索框与「只看已占用」开关（纯 JS）。
3. `#truth` 真相表：按 `TruthKind` 分四组展示。
4. `#declared` 配置声明清单：按软件分组。
5. `#advice` 建议：依据真实发现自动生成（例如检测到 PowerToys 键盘管理器的重映射 → 输出对应条目；检测到 Peek 占 `Ctrl+Space` → 提示与输入法/IDE 冲突），没有发现也要有兜底文案。
6. `#method` 方法与局限：说明探测原理、为什么钩子型热键看不到、为什么 32 位进程与提权进程可能缺失。
7. 所有动态文本必须做 HTML 转义（`&<>"'`）。

## 8. 图形界面规格

WinForms，约 1080×720，中文字体（微软雅黑），标签页：

- **扫描总览**：大按钮「开始扫描」+ 进度条 + 统计卡片（占用数/空闲数/声明数/真相表条数）+ 结果列表（占用组合、归属、来源、证据等级）+ 「生成 HTML 报告」按钮 + 「以管理员身份重启」按钮（非管理员时可见）。
- **按键归属**（chunk 2）：开关按钮 + 说明文字 + 实时结果列表（时间、进程、组合键、路径）+ 管理员状态提示。
- **钩子检测**（chunk 2）：布防按钮 + 引导步骤 + 结果列表 + 对照排除工作流。
- **配置真相表**：四类分组的列表 + 双击打开来源配置文件。
- **报告与关于**：报告路径、打开报告、版本、已知限制说明。

要求：非管理员可用（缺失的能力用灰字说明而不是报错）；所有耗时操作放后台线程、不冻结界面；异常一律捕获并显示在日志区，不弹崩溃对话框。

## 9. 自测（`--selftest`）

| 编号 | 内容 | 通过标准 | 归属 |
|---|---|---|---|
| T1 | VK 表与组合键格式化/解析往返 | `Parse(x.Display()) == x` 对抽样组合成立 | chunk 1 |
| T2 | 引擎 A 扫描 | 组合总数 ≥ 1500；占用数 ≥ 150；连续两次扫描结果重合度 ≥ 90% | chunk 1 |
| T3 | 引擎 D 解析 PowerToys | 找到键盘管理器 8 条重映射（Ctrl+Alt+B/F/M/N/P/R/T 与 Win+Shift+F23） | chunk 1 |
| T4 | 真相表 | 行数 ≥ 1000；四类 bucket 均有解释文本；PowerToys 声明能对到 A 的结果上 | chunk 1 |
| T5 | 报告生成 | 生成到临时目录，包含 `#occupancy`、`#truth`、`#advice` 锚点，无 `&amp;amp;` 类双重转义 | chunk 1 |
| T6 | 引擎 B 归属链路 | 装钩子成功；启动注册了 `Ctrl+Alt+F13` 的辅助进程，`SendInput` 触发后 3 秒内收到归属报告且 pid 正确。**若会话已锁定/无交互桌面，判为 INCONCLUSIVE 并打印原因，不算失败** | chunk 2 |
| T7 | 引擎 C 吞键检测 | 合成一个未被吞的按键 → 判定为 `Delivered/NotGlobal` 分支正确；用一个测试用 LL 钩子吞掉 `Ctrl+Alt+Shift+F14` → 判定为 `SwallowedUpstream`。同样允许 INCONCLUSIVE | chunk 2 |

自测只允许在临时目录创建文件、只允许注册/注销自己的热键，结束后必须清理干净（注销热键、卸载钩子、删除临时文件、结束辅助进程）。

## 10. 实现纪律

- **绝不修改用户配置**：所有配置读取只读；不得写注册表（除自测里自己创建并删除的临时键，且需可证明已清理）。
- 不下载、不安装任何东西；不用 NuGet。
- 不改动 `Source Code` 目录下已有的其他项目（CAScreenGuard、Constellation）。
- 代码注释：只在解释「为什么」时写；不要写「这段代码做了什么」的注释。
- C# 7.3 语法以内（不用 `record`、不用 `using var`、不用 `switch` 表达式、不用 `is not`、不用范围运算符）。
- 所有面向用户的文字用中文；日志可中英混排但不得出现乱码（源文件一律 UTF-8 with BOM，csc 用 `/codepage:65001` 或确保 BOM 存在）。

---

# 附：第二块实施增补（chunk 2 deltas）

## 11. 环境实测发现（必须处理）

工作区目录（`...\iCloudDrive\Source Code\`）及其中的文件带 `Mandatory Label\Low Mandatory Level` 标签。**从这些文件启动的进程会以低完整性（Low IL）运行**，后果：

- 写 `%TEMP%`、`%LOCALAPPDATA%`、`C:\ProgramData` 会被拒绝（错误 5）——chunk 1 已用「候选目录逐个试探」缓解；
- **钩子注入到普通完整性进程会静默失败**（UIPI），引擎 B/C 在低完整性下几乎无效。

因此第二块必须：

1. 启动时探测自身完整性级别（`GetTokenInformation`/`ProcessToken` → `TokenIntegrityLevel`，取 RID：<0x2000 低、0x2000 中、0x3000 高）。
2. 完整性 < 中 时，在 GUI 顶部显示醒目提示条，并在报告 `#method` 里写明：
   「当前程序以低完整性运行（exe 位于带 Low 标签的目录）。钩子注入能力受限，请把 exe 复制到普通目录（如 `C:\Tools\`）后再运行。」
3. hook DLL 释放目录按可写性依次尝试：`%LOCALAPPDATA%\HotkeyHound\hooks\x64\` → exe 同目录 `hooks\x64\` → `%TEMP%\HotkeyHound\hooks\x64\`；把最终使用的路径写进日志与报告。
4. 非管理员时同样显示提示条（提权后能看到 PowerToys 这类高完整性进程）。

## 12. 引擎 B / C 的界面与报告

- GUI 增加可用的「按键归属」与「钩子检测」两个标签页（替换占位页）：
  - 按键归属：状态说明、启动/停止监听按钮、实时结果表（时间 / 进程名 / PID / 组合键 / 路径 / 是否提权进程）、清空按钮、未提权与低完整性提示。
  - 钩子检测：布防按钮 + 三步引导（①布防 ②按下有问题的键 ③看结论）、结论表（组合键 / 判定 / 证据 / 说明）、对照排除工作流（候选进程列表 + 分步指引 + 「记录本次对照」按钮），**绝不自动结束或暂停用户进程**。
- 报告新增两个区块：`#attribution`（本次会话的归属记录）与 `#hookcheck`（钩子检测结论）；没有数据时也要输出该区块并写明「本次未采集」。
- `--listen [--seconds N]` 在控制台输出归属记录（表格文本），N 默认 30 秒，需要管理员才有完整覆盖。

## 13. 第一块自审查已确认的两个缺陷（第二块一并修掉）

1. `ReportBuilder` 的建议文本把「忽略 Shift 的模糊匹配」当成事实写了出来，产生了配置里不存在的组合键（例：`EditorShortcut` 实际是 `Ctrl+Shift+K`，建议里写成 `Ctrl+K`；还出现了 `Ctrl+Alt+Shift+B` 这种不存在的键位）。模糊匹配只能出现在 `Note` 里，且必须带「（Shift 变体）」字样；建议文本只允许引用精确匹配的事实。
2. 建议条目按热键逐条重复同一段话。改为按「软件/模块」聚合：一条建议里列出受影响的全部组合键（逗号分隔，超过 8 个用「等 N 个」）。
