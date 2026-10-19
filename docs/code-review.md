# 代码审查

两轮，方法不同，结论也不同。**先读第五节**（第二轮，2026-10-19）——那一轮才真正挖出了危险的东西；
第一轮（2026-10-18，1.2.0 → 1.3.0 期间）作为历史记录保留在后面。

| 轮次 | 方法 | 结果 |
|---|---|---|
| 第一轮 | 通读关键路径 + 危险 API 扫描 + 2 个实证测试 | 没有崩溃/数据损坏/安全问题；1 个中等问题（本地化泄漏）、4 个低风险 |
| **第二轮** | **两路独立代理只读对抗审查**（原生 DLL、32 位宿主）+ 结构性审查 + 3 个实证测试 | **4 个高危 + 9 个中危**，全部集中在钩子/跨进程/跨位数这条链路上；均已修复或有理由地接受 |

第一轮为什么漏了：审查的是"C# 代码写得对不对"，而这两处高危都在**原生/跨进程**那一层——
一个是 Win32 API 参数序写反（`CreateFileMappingW` 的高低位），一个是手写解析器没有递归上限。
两者都不会被编译器、单元测试或界面操作发现，只会在特定条件下静默失效或直接杀进程。

---

# 第一轮（2026-10-18，1.2.0 → 1.3.0）

审查范围：`src/` 全部源码、`src/Hook/HookDll.cpp`、`tests/SelfTest.cs`、`build.ps1`。
方法：通读关键路径 + 危险 API 扫描 + 两个实证测试（报告 HTML 注入、英文模式的本地化泄漏）+ 1.3.0 增量复核。
结论先行：**没有发现会导致崩溃、数据损坏或安全问题的缺陷**；发现 1 个中等问题、4 个低风险问题、
1 个风格问题，以及 1 个在复核 32 位增量时发现并**已修复**的并发隐患（见 M2）。逐条如下。

## 一、实证测试

### 1) 报告 HTML 注入 —— 通过

把一段 `<script>alert(2)</script>` 与 `<img src=x onerror=alert(3)>` 写进
`%LOCALAPPDATA%\HotkeyHound\catalog.json`（用户覆盖知识库），再跑 `--scan --report`：

```
has raw <img src=x        : False
has raw <script>alert(2)  : False
has escaped &lt;img       : True
has escaped &lt;script&gt;: True
```

报告里所有数据驱动字段（软件名、模块名、备注、配置路径、进程名）都经过 `Html.Escape`，
共 143 处转义调用。**结论：没有 HTML 注入面。** 顺带验证了用户覆盖机制（18 条 = 内嵌 17 + 覆盖 1）。

### 2) 英文模式的本地化泄漏 —— 未通过（见 M1）

`--lang en --scan` 的英文报告里，引擎顶层的日志是英文，但**适配器日志仍是中文**：

```
Engine A: probing all key combinations, 1650 in total        ← 英文
[PowerToys] 共解析出 32 条热键声明                            ← 中文（泄漏）
引擎 D：PowerToys 贡献 32 条声明                              ← 中文（泄漏）
```

## 二、问题清单

### M1（中等）适配器/引擎日志未走 L10n，英文模式会漏中文

- **位置**：`src/Core/Adapters/*.cs`、`AttributionEngine.cs`、`HookSniffer.cs`、`ConfigCollector.cs`、
  `SelfTest.cs` 里的日志与 `LastError` 文案。全仓非注释行中的中文字面量共 **401 处**，`L10n.cs` 之外。
- **影响**：英文用户看到的界面日志、`#method` 区块的运行日志、部分错误提示会混入中文；
  报告整体观感受损（功能不受影响）。
- **建议**：把这些文案抽进 `L10n`；优先级：`LastError`（用户会看到）> `Say()`（进报告）> 自测明细。

### L1（低）提权重启的参数引用不够健壮

- **位置**：`Elevation.AppendQuoted` / `BuildRelaunchArguments`。
- **问题**：只在"含空格且不含引号"时才加引号；不处理内嵌引号，也不处理**以反斜杠结尾**的参数
  （`"C:\dir\"` 是 Windows 命令行解析的经典陷阱）。`extraArgs` 直接拼接，未做引用。
- **影响**：极少数参数形态下，提权重启后的参数会被错误切分。当前调用方只传 `--listen` 这类简单开关，无实际风险。
- **建议**：按 MSVCRT 规则做完整引用（转义尾部反斜杠与内嵌引号）。

### L2（低）少量 GDI/进程对象未释放

- `HookCheckTab` 里 `new Font(...)` 赋给控件后未释放（一次性，影响可忽略）。
- `HookSuspectsTab` / `OverviewTab` 里 `Process.Start(...)` 的返回对象未释放。
- `KeyMonitor.HandleRawInput` 每个 `WM_INPUT` 都 `AllocHGlobal` 一次；可用复用缓冲区降低 GC 压力。

### L3（低）钩子路径的分配与同步事件

- **位置**：`KeyMonitor.LowLevelProc` → `Observe()`。
- **问题**：每个按键事件分配一个对象，并在**钩子线程**上同步触发事件。
- **现状**：所有订阅方都用 `BeginInvoke`（已核对 `AttributionTab`/`HookCheckTab`/`KeyMonitorTab`/`MainForm`），
  临界区也短，所以当前**没有**输入延迟风险；但这是一条纪律，未来新增订阅者若同步阻塞就会拖慢全局输入。
- **建议**：在 `ILogSink`/事件注释里写明"订阅者必须异步"，或改成内部队列 + 消费线程。

### L4（低）`Scancode Map` 与过滤驱动清单是启发式

- 解析已做边界检查（长度、条目数上限、零终止符），畸形数据不会越界；但"键盘类驱动"的判定是
  **按组名/驱动名启发式**（`kbd`/`i8042`/`keyboard`/`Group=Keyboard`），会漏掉改名驱动。
- 界面上已写明"只列出，不判定、不卸载"，属于可接受的设计取舍。

### S1（风格）空 `catch (Exception) { }` 共 112 处

- 钩子热路径、UI 最佳努力路径里是刻意的（不应让日志/界面异常影响输入）；
- 但 `MainForm`（14 处）里有几处会掩盖"报告写盘失败"这类真实错误。
- **建议**：写盘、注册表读取、配置解析这类路径改成"记录到日志 + 继续"。

## 三、审查过的关键路径（结论：正确）

| 路径 | 检查点 | 结论 |
|---|---|---|
| `HookDll.cpp` | 导出地址装钩子、`DllMain` 空、`CallNextHookEx` 放行、共享内存一次性初始化 | 符合 AGENTS 硬规矩 |
| `KeyMonitor` | 装/卸顺序、`RIDEV_REMOVE`、`WM_INPUT` 交给 `DefWindowProc`、线程 join 超时 | 正确，无残留钩子 |
| `AutoProbe` | 默认 dry-run、硬编码黑名单（含屏幕旋转键）、Esc 中止、确认框默认"否"、`INPUT` 结构体尺寸 | 正确 |
| UI 事件编组 | 全部 `BeginInvoke` | 正确，钩子线程不被界面拖住 |
| 报告生成 | 143 处 `Html.Escape`、实证注入测试 | 通过 |
| 只读约束 | 适配器/知识库无 `WriteAllText`/`SetValue`/`CreateSubKey` | 通过 |
| 提权 | `ERROR_CANCELLED` 识别、`--elevated` 防循环 | 正确（见 L1） |
| 自测 | 9 项、`--no-hooks` 不装钩子、T9 覆盖判定与 Scancode Map | 通过 |

## 五、1.3.0（32 位覆盖）增量复核

### M2（中等，已修复）两个实例会互相抢控制窗口

- **位置**：`AttributionEngine.MapName`（固定名 `Local\HotkeyHound.Ctl`）+ 程序无单实例约束。
- **原因**：注入到别人进程里的 hook DLL 只能靠固定名去找控制窗口——它没法知道"该跟哪个实例说话"。
  于是同时跑两个实例时：后启动的会覆盖共享内存里的 `arm/ctl`，前一个实例的归属记录被送到后者的窗口；
  任意一方退出时把 `arm` 置 0，会把另一方仍在运行的上报一起关掉。
- **修复**：GUI 入口加命名互斥体 `Local\HotkeyHound.SingleInstance`；已有实例时把它切到前台
  （`FindWindow` + `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`）并安静退出（exit 0）。
  实测：A 存活时启动 B，**B 立即退出（exit 0）且进程数保持 1**。
- **为什么不用"每实例唯一映射名"**：随机名无法被注入的 DLL 可靠发现（DLL 在别人的进程里，拿不到我们的启动参数），
  代价远大于收益。

### 已接受的设计取舍（记录在案）

- 共享内存位于 `Local\` 命名空间，同一会话内的其它进程可以打开它并读到"哪个热键被触发"。
  泄露的是**热键事件**（不是按键内容），且需要同会话、同用户的本地进程；没有网络通道。
  若要收紧需给映射加 DACL 或改用一次性的随机名 + 额外发现机制，成本与收益不匹配，暂不处理。
- 位数判定用 `IsWow64Process`（不是 `IsWow64Process2`）：在 x64 主机上结论正确；
  ARM64 主机上的 x86 模拟进程会被判成 x86，行为仍正确。

### 复核结论（正确项）

- 控制块两侧完全一致：C++ `struct { DWORD magic, arm, ctlLo, ctlHi; }` + `static_assert(sizeof==16)`，
  C# 按偏移 `0/4/8/12` 写入；`ctl` 由 `ctlHi<<32|ctlLo` 还原。
- 启停顺序正确：`Start()` 先起 x64 钩子线程、再拉 32 位宿主并 `WaitReady`；
  `Stop()` 先停宿主、再 `UnhookWindowsHookEx`、最后 `arm=0`。
- 宿主不残留：父进程被强杀时 32 位宿主经 `OpenProcess(SYNCHRONIZE)+WaitForSingleObject` 在 **229 ms** 内自退；
  正常退出 `Stop()` 后 `Get-Process` 计数为 0（本次复核复现）。
- 产物释放的旧 bug 已修：原来只比较文件长度，新旧 DLL 等长时旧文件不覆盖（升级后 x64 链路会静默失效）；
  现在逐字节比较（`SameBytes`）。
- 两个位数都验证到位：普通目录下 `--selftest` **10/10 PASS**（T6 x64 归属、T7 吞键检测、T10 32 位链路）。

## 四、建议的处理顺序

1. M1（本地化）：影响所有英文用户的第一印象，值得单独一版。
2. L1/L2：顺手修，成本很低。
3. S1 里的报告写盘路径：加日志，避免"报告没生成但界面没提示"。
4. L3/L4：写进代码注释与文档，作为后续约束。

---

# 第二轮审查（2026-10-19，v1.3.1）—— 三路对抗性审查

上一轮是通读 + 两个实证测试。这一轮换了方法：**两路独立代理只读审查**（原生 DLL、32 位宿主生命周期），
加上我自己对引擎/适配器/UI/互操作/构建/CI 的结构性审查与 3 个实证测试。
重点是找"现场不会报错、只会静默失效"的那类缺陷。**结果：找到 1 个高危 + 3 个高危 + 9 个中危**，
全部与"钩子/跨进程/跨位数"这条最危险的链路有关。

## 五、发现与处置

| 编号 | 严重度 | 位置 | 问题 | 处置 |
|---|---|---|---|---|
| H1 | **高** | `HookDll.cpp` 初始化 | `CreateFileMappingW` 的高低 32 位参数写反（`sizeof, 0`）→ 申请 **64 GiB** pagefile 段：多数机器直接失败，回退路径形同不存在；万一成功，每个被注入进程钉住 64 GiB 提交 | **已修**：改 `0, HKS_MAP_SIZE`，且大小必须是 **64**（与 `ControlMapSize` 一致）——命名段先创建者定死大小，只建 16 字节会让控制器 `MapViewOfFile(64)` 失败 |
| H2 | **高** | `Helper32Host.Start` | 不回收仍在运行的旧宿主 → 旧 helper32 成孤儿，且旧 reader 停止抽干其 stdout | 已修（代理） |
| H3 | **高** | `Helper32Main.RequestQuit` | 先抢输出锁再起兜底退出线程 → 主线程阻塞在 `Print` 时 `Environment.Exit` 永不可达，"绝不留僵尸"失效 | 已修（代理） |
| H4 | **高** | `Helper32Host` 重定向 | `RedirectStandardError=true` 却从不读 stderr → 子进程异常写满 4 KB 管道即永久挂住 | 已修（代理） |
| M1 | 中 | `HookDll.cpp` 初始化 | 失败也 `return TRUE` → `INIT_ONCE` 判定完成，`g_pCtl` 永远 NULL，该进程**永久失聪** | **已修**：失败 `return FALSE` 允许重试，`InterlockedIncrement` 限 8 次 |
| M2 | 中 | `HookDll.cpp` 控制块 | DLL 持可写视图并在 magic 不符时写控制块 → 可能把控制器刚 `arm=1` 的块清零，全局静默丢报 | **已修**：改 `FILE_MAP_READ`，删除整个写分支（这块内存归控制器独占） |
| M3 | 中 | `HookDll.cpp` 热路径 | 4 次独立、非 volatile 的读 → 可能读到「新 lo + 旧 hi」的撕裂句柄 | **已修**：字段 `volatile`；读序 `arm → magic → 句柄`，控制器写序 `ctlLo → ctlHi → magic → MemoryBarrier → arm=1` |
| M4 | 中 | `HookDll.cpp` 断言 | `static_assert(sizeof==16)` 挡不住字段互换（换完仍是 16 字节），C# 侧按 0/4/8/12 硬写 → 静默丢归属 | **已修**：补 4 条 `offsetof` 断言。**这条当场把编译打红**（缺 `<cstddef>`），正说明"改完必须编译" |
| M5 | 中 | `AttributionEngine` 控制窗口 | 提权运行时，低完整性进程里注入的 DLL 投给我们的 `WM_APP+1` 被 UIPI 拦掉 → 提权后归属彻底失灵且不报错 | **已修**：`ChangeWindowMessageFilterEx` 只放行这一条上报消息 |
| M6 | 中 | `JsonLite` | 手写解析器互递归**无深度上限** → 畸形/截断的 JSON（几万个 `[`）触发 `StackOverflowException`，这是 .NET 里唯一 catch 不住的异常 | **已修**：`MaxDepth=64`，超限抛可捕获的 `JsonLiteException`；**实证**：20 万个 `[` → 受控返回 null，60 层正常解析，正常 JSON 无回归 |
| M7 | 中 | `AttributionEngine` / `HookSniffer` | 记录列表**无上限**（而 `KeyMonitor` 限 2000、`_recentKeys` 限 200）：别人注册的热键若没带 `MOD_NOREPEAT`，按住不放每秒几十条 `WM_HOTKEY`，挂一晚上就十几万条 → 内存涨、列表卡死 | **已修**：归属 20000 / 吞键 20000 / 对比快照 500，超限丢最早并在日志提示一次 |
| M8 | 中 | `.github/workflows/build.yml` | 英文报告只匹配「真相表」一个词，等于没有防线 | **已修**：全篇 CJK 计数必须为 0，失败打印命中上下文 |
| M9 | 中 | `Helper32Host` 释放产物 | `File.Delete` 失败被静默吞 → 静默换目录，留下两份不同版本副本 | 已修（代理） |

（M9 之后还有若干中低危的宿主问题——失败路径不 `Set()`、`_readyEvent` 跨 Stop/Start 的假阴性、
reader 的 TOCTOU、没有 Job Object 导致 PID 复用僵尸、HWND 符号扩展导致父端拿到 0——一并由代理修完，
详见 `CHANGELOG.md` 的 1.3.1 小节。）

## 六、已接受的风险（有理由，不是遗漏）

| 风险 | 为什么不修 |
|---|---|
| 控制块不校验上报者身份：控制器被杀（`arm` 仍为 1）或句柄复用后，DLL 会把 `WM_APP+1` 投给无关窗口 | 影响仅为一条杂散消息（程序会忽略未知消息）；加心跳/世代字段会给每次热键增加两次读与一个字段，成本大于收益 |
| 首次 `WM_HOTKEY` 时才做文件映射（`InitOnce` 会让并发线程互等） | `WM_HOTKEY` 是稀有事件，且现在有 8 次重试上限；改成"每条消息都初始化"会把它变成热路径上的常规开销，更糟。`DllMain` 里做映射更是直接违反加载器锁纪律 |
| 共享内存在 `Local\` 命名空间，同会话进程可打开读取 | 泄露的是**热键事件**（不是按键内容），且仅限本地同用户；要收紧需要 DACL + 随机命名发现机制，与本工具的定位不匹配 |
| 每轮「卸钩 → 再布防」会在每个被注入进程里多留 1 个句柄 + 16 字节视图 | 这是注入模型的固有代价（DLL 映像被卸载后数据段归零）；不能在 `DllMain` 里清理（那会在加载器锁下开句柄）。文档已注明避免频繁重装 |
| 调试版（`/DHKS_DEBUG`）每次 `WM_HOTKEY` 都写文件 | 发布版 `HksDbg` 展开为空，零影响 |
| 非 1409 的注册失败仍记为「已占用」 | 已在报告中显式列出错误码并声明「注册失败不等于软件占用」，不做猜测性剔除 |

## 七、已核对正确（本轮覆盖，无需改动）

- `DllMain` 只有 `DLL_PROCESS_ATTACH` 下的 `DisableThreadLibraryCalls`，无临界区/分配/句柄/`LoadLibrary`。
- 钩子过程**唯一出口** `CallNextHookEx`：`nCode < 0` 与 `HC_NOREMOVE` 原样透传，从不改写消息、从不吞键。
- x86/x64 导出名经 `/DEF` 均不带 `@12` 修饰，`GetProcAddress("HotkeyHoundHookProc")` 两个位数都成立。
- `Html.Escape` 覆盖 `& < > " '`，文本与属性上下文都安全（143 处转义调用 + 注入实证）。
- 引擎 A 单点探针：注册成功立即注销、中间无异常点、`Marshal.GetLastWin32Error()` 用法正确、带 `MOD_NOREPEAT`。
- 真相表 2×2 分类完备且互斥；本地化键齐备。
- 适配器：`AdapterBase` 集中 try/catch、容错读取、注册表只读、解析全用 `TryParse`。
- 引擎 C 低层钩子：唯一返回路径在 try 外且总是 `CallNextHookEx`、委托有字段持有防 GC、`Stop()` 投 `WM_QUIT` + `Join(4000)` 有界。
- UI：全项目仅一个自定义 `OnPaint`，4 个 GDI 对象全在 `using` 里；跨线程一律 `BeginInvoke`。
- `L10n`：缺键回退链 en→zh→键名；`F()` 的 `string.Format` 包了 try/catch（占位符不匹配不会崩）。
- `build.ps1`：四处工具链缺失各自 `throw` 并给出可操作提示；编译前清空 `build\`；每个产物都断言 PE 位数。
- 单实例约束：A 存活时启动 B，B 立即退出（exit 0）且进程数保持 1。

## 八、回归防护

- **CI 零 CJK 断言**（M8）：英文报告出现任何中文字符即失败，并打印上下文。
- **T11（待加）**：`L10n.SelfCheck()`——遍历全部键，断言中英两侧都存在、`{n}` 占位符集合一致、
  英文条目里不含 CJK。本地化改动量大（当时统计 440 行非注释中文、22 个文件），这类回归只能靠遍历挡。
- **T12（待加）**：`JsonLite` 深度上限的回归测试（20 万层不崩、60 层正常）。

## 九、我在本轮修正的一个错误判断

上一轮我把 `UiTheme` 的字体工厂判成"死代码"。复核后：`MonoFont` 被 `MainForm` 使用，
`TitleFont` 有 12 处调用点——**没有死代码**，该条撤回。（教训：用 `FontStyle.Bold` 去 grep 方法名，
grep 到的是枚举值。）

