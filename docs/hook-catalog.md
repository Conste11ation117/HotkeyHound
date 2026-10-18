# 钩子型热键知识库（hook catalog）

## 为什么要这份东西

扫描（引擎 A）能看到的是「谁向系统注册了热键」。但有一整类快捷键**从不注册**：

- PowerToys 键盘管理器（改键/启动程序）
- 输入法的中英切换、候选词翻页、快捷键自定义
- AutoHotkey 脚本
- 部分截图/录屏/宏工具、键鼠厂商驱动、游戏加速器与模拟器

它们用低层键盘钩子（`WH_KEYBOARD_LL`）或驱动级拦截，按键**在到达系统热键分发之前**就被处理掉了。
所以引擎 A 会把这类组合报成「空闲」，而用户按下去毫无反应——**这是最难查的一类冲突**，也是这个
知识库存在的唯一理由。

内核驱动能读 win32k 的热键表，**但读不到钩子**——驱动方案对这类问题没用，代价却很大（未签名驱动、
Secure Boot / HVCI 拦截、杀软与反作弊误判）。所以我们走另一条路：

> **读它的配置 + 看它在不在跑 + 对照扫描结果 → 给出「高度怀疑」级别的结论，并告诉用户去哪改。**

## 证据等级（必须如实标注，不能含糊）

| 等级 | 含义 | 判定依据 |
|---|---|---|
| **已确认** | 配置里明确写了这个组合，且软件正在运行 | 配置文件可读 + 进程存在 |
| **高度怀疑** | 配置里写了，但软件当前没运行；或配置不可读但进程在跑且已知有钩子热键 | 只有一侧证据 |
| **已知默认** | 该软件的出厂默认快捷键，本机没能读到配置 | 来自官方文档/安装包默认值 |
| **仅供参考** | 该软件确实用钩子，但具体键位无法确定 | 只有进程证据 |

界面上**必须**把等级显示出来；报告里也一样。宁可写「仅供参考」，也不要写成事实。

## 数据格式（`data/hook-catalog.json`）

构建时内嵌进 exe；用户也可以放一份到 `%LOCALAPPDATA%\HotkeyHound\catalog.json` 覆盖/追加条目。

```json
{
  "version": 1,
  "entries": [
    {
      "id": "powertoys.keyboard-manager",
      "software": "PowerToys",
      "module": "键盘管理器",
      "kind": "hook",
      "processNames": ["PowerToys.exe", "PowerToys.KeyboardManagerEngine.exe"],
      "configPaths": ["%LOCALAPPDATA%\\Microsoft\\PowerToys\\Keyboard Manager\\default.json"],
      "registryKeys": [],
      "knownHotkeys": [
        { "keys": "Ctrl+Alt+M", "purpose": "重映射 → Ctrl+P", "source": "config" }
      ],
      "notes": "用 WH_KEYBOARD_LL 实现；改键与启动程序都在按键到达其它程序之前完成。",
      "docs": "https://github.com/microsoft/PowerToys"
    }
  ]
}
```

字段约定：

- `kind`：`hook`（钩子型）｜`registered`（走 `RegisterHotKey`）｜`mixed`（两种都有）。只有
  `hook` / `mixed` 会进「钩子嫌疑」清单；`registered` 的条目只是给引擎 D 补充「默认值」。
- `knownHotkeys[].source`：`config`（本机读到的）｜`default`（官方默认）｜`docs`（文档记载）。
- `keys` 用本工程的组合键写法：`Ctrl+Alt+M`、`Win+Shift+F23`、`Ctrl+Space`。
- 路径支持 `%LOCALAPPDATA%`、`%APPDATA%`、`%ProgramFiles%`、`%ProgramFiles(x86)%`、`%WINDIR%`。
  注册表键写成 `HKCU\...` / `HKLM\...`（只读）。
- 一个软件可以有多个条目（例如 PowerToys 的键盘管理器与 Peek 分开）。
- 可选英文字段：`softwareEn`、`moduleEn`、`notesEn`，以及 `knownHotkeys[].purposeEn`；
  英文界面优先使用它们，缺省时回退中文。这保证「所有面向用户的文字」在两种语言下都可读。
- 可选 `configState`：`read` / `unreadable` / `missing`。用于人工标注自动探测无法判断的格式
  （例如 MMKV 二进制、DPAPI 加密），写 `unreadable` 时界面与报告必须显示「不可读」。
- 配置可读性由程序自动探测（文件/目录能否打开、注册表键能否打开）；不要为了「好看」把
  读不到的条目写成 `read`。

## 首批要覆盖的软件

本机已安装、且值得实测配置位置的：

| 软件 | 类型 | 备注 |
|---|---|---|
| PowerToys 键盘管理器 | hook | `Keyboard Manager\default.json` 的 `remapShortcuts` |
| PowerToys Peek | mixed | `Ctrl+Space`，与输入法/IDE 冲突的重灾区 |
| 微信输入法 / 搜狗输入法 / 微软拼音 | hook | 中英切换、候选词键位；配置多为二进制（MMKV），读不到就标「参考」 |
| QQ / 微信 / 钉钉 / 飞书 | registered + hook | 截图、锁屏、唤起窗口；配置常被进程锁住，读不到就按默认 |
| AutoHotkey | hook | 脚本目录里的 `.ahk` 可扫 `^!k::` 之类的定义 |
| ToDesk / 向日葵 | mixed | 远控快捷键 |
| 网易云音乐 | registered | 全局播放控制 |
| 豆包 / 讯飞听见 | hook | 划词、语音快捷键 |
| 机械革命控制中心 / 雷蛇 / 罗技驱动 | hook | 厂商热键与宏 |
| 雷电模拟器 / 腾讯手游助手 | hook | 按键映射（窗口内为主） |
| NVIDIA App 叠加层 | registered | 已有适配器，补默认值即可 |
| Wallpaper Engine / MSI Afterburner | registered | 已有适配器，补默认值即可 |

## 实现要点

1. **只读**：配置文件只读、注册表只读；不写、不导入、不结束进程。
2. **容错**：任何一条解析失败都只影响该条目，记录原因（`unreadable` / `not-installed` /
   `process-not-running`），不要抛异常。
3. **贡献友好**：新增一个软件 = 往 JSON 里加一条（或在 `%LOCALAPPDATA%` 放覆盖文件），
   不需要改代码。这是这份知识库能长期维护的前提。
4. **不夸大**：结论里永远带上证据等级与来源路径；「已知默认」必须能在界面里区分于「本机读到」。
