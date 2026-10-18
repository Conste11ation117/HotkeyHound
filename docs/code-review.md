# 代码审查（2026-10-18，1.2.0 → 1.3.0 期间）

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
