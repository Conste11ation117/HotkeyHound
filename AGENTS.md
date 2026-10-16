# 给在本仓库工作的 agent 的说明

本文件是本仓库的硬性约定。**改代码前先读完。**

## 一、绝对不要再犯的一条：全局钩子不许用托管委托

`SetWindowsHookEx(WH_GETMESSAGE, …, 0)` 装的是**全局**钩子：Windows 会把我们的 DLL 注入当前
会话里的**每一个进程**，然后在**那些进程里**调用钩子过程。因此：

- 必须用 `GetProcAddress` 取原生导出地址（`HotkeyHoundHookProc`）作为钩子过程指针；
- **绝对不许**把 .NET 托管委托（`new HookProcDelegate(...)`）传给 `SetWindowsHookEx`。委托地址
  只在本进程有效，注入到别处就是野指针，后果是**每个 GUI 进程同时崩溃**（`0xc0000409`、
  故障模块 `unknown`），也就是整台机器一起崩。

这条规则有代价换来的教训：2026-10-16 开发期间正是这个错误导致开发机全机崩溃（同一秒内 8 个
进程集体阵亡），详见 `CHANGELOG.md` 的 0.2.0 一节。**不要把 `AttributionEngine` 里面那个
"本进程替身过程"加回来——它已经不存在，也不需要**。

同类纪律：

- `src/Hook/HookDll.cpp` 的 `DllMain` 里除 `DisableThreadLibraryCalls` 外**什么也不要写**
  （不要在加载器锁下建/删临界区、分配内存、开句柄）；
- 共享内存握手用 `InitOnceExecuteOnce` 做一次性初始化，**不许**在钩子热路径里做文件映射或加锁；
- 钩子过程只允许：读内存、比较消息、投递一次消息、`CallNextHookEx`。绝不吞键、绝不改消息。

## 二、产品行为上的硬约束

- **加载界面绝不安装任何钩子。** 只有用户点「开始监听」/「布防（安装钩子）」时才装。
  按钮文案必须写明「安装钩子」，页面必须用直白语言说明这一点。
- **引擎 A / D 与报告只读**：不写配置、不写注册表、不留注册（`RegisterHotKey` 成功必须立刻
  `UnregisterHotKey`）。
- **绝不结束或暂停其它进程**：对照排除流程只记录观察结果，由用户自己退出可疑软件。
- **没有网络代码**。任何联网行为都算 bug。

## 三、构建与验证

- 本机**没有 .NET SDK**，不要用 `dotnet build`。构建走：
  `powershell -ExecutionPolicy Bypass -File build.ps1`
  （内部用 VS 的 Roslyn `csc.exe` 编 .NET Framework 4.8 + MSVC `cl` 编原生 DLL，再内嵌资源）
- **验收必须在普通目录里跑**：把 `dist\HotkeyHound.exe` 复制到例如
  `C:\Users\Constellation\AppData\Local\Temp\hs-verify\` 再执行。
  工作区目录带 Low 完整性标签，直接在工作区里跑会写不了临时目录、钩子注入也会被 UIPI 拦。
- 改完必须验证（全绿才算完）：
  - `HotkeyHound.exe --selftest --no-hooks` → T1–T5 通过（不需要管理员、不装钩子）
  - 涉及钩子的改动：`HotkeyHound.exe --selftest` → **7/7**；跑之前先想清楚会不会影响用户机器，
    优先在虚拟机里验证，并准备好随时回滚
  - `HotkeyHound.exe --scan --report x.html` 与 `--lang en` 版本都要能生成
- `.cs` / `.ps1` 一律 **UTF-8 带 BOM + CRLF**；`csc` 已带 `/codepage:65001`，但习惯要守住。

## 四、代码与文档风格

- C# 7.3 语法以内；**不用 NuGet、不联网取依赖**；能用 .NET Framework 自带 API 就不要引三方库。
- 面向用户的文字**一律走 `L10n`**（`zh-CN` / `en-US` 两套），不要在代码里散落中文字面量。
- 注释只写「为什么」，不写「做了什么」。
- 文档中英成对：`README.md` / `README.zh-CN.md`，`CHANGELOG.md` / `CHANGELOG.zh-CN.md`，
  `docs/help.en-US.md` / `docs/help.zh-CN.md`。改一份就要改另一份。

## 五、提交与推送习惯

- **一次只改一件事**，改完就验证；提交信息用中文，写清「为什么」而不只是「做了什么」。
- 本机推送 GitHub 需要走本地代理：`git -c http.proxy=http://127.0.0.1:65532 push …`
  （代理没开就别推，报错是 `Failed to connect … over proxy`）。
- 版本号在 `src/Core/SharedState.cs` 的 `AppInfo.Version`；发版要同时更新两个 CHANGELOG，
  并重建 `dist\HotkeyHound.exe`。
- 产物（`dist\HotkeyHound.exe`）**不进货**（`.gitignore` 已排除）：走 GitHub Release 附件分发。
  但改完源码**务必重建并自测**，发版时把新产物传上去——用户是照产物用的。
