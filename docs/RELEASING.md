# 发布清单（RELEASING）

仓库地址：https://github.com/Conste11ation117/HotkeyHound
本机推送需要走本地代理：`git -c http.proxy=http://127.0.0.1:65532 push …`（代理没开就别推）

## 一、仓库设置（需要 GitHub 登录态，在网页上粘贴）

**About（仓库描述，一行，关键词密集）：**

```
Hunt down the app that stole your Windows hotkey — list every occupied combination, attribute keys to processes, and catch hooks that swallow input. MIT, no installer.
```

**Topics（直接把下面这串贴进 Topics 输入框，逗号分隔）：**

```
windows, hotkey, hotkeys, keyboard-shortcuts, shortcut, registerhotkey, keyboard-hook, conflict, troubleshooting, diagnostics, powertoys, win32, winforms, csharp, cpp, utility, tools, windows-11, desktop, mit-license
```

> 为什么这样写：用户的真实搜索词是 "which process registered a global hotkey"、"hotkey already
> registered"、"hotkeys stolen"、"全局热键 冲突"。关键词放在 About/Topics/正文里，**不要**往项目名上堆。

## 二、发版步骤

1. **确认构建与自测**（在普通目录里跑，别在工作区跑——工作区带 Low 完整性标签）：
   ```
   powershell -ExecutionPolicy Bypass -File build.ps1
   copy dist\HotkeyHound.exe "%TEMP%\verify\"
   "%TEMP%\verify\HotkeyHound.exe" --selftest --no-hooks      # T1–T5 必须全绿
   "%TEMP%\verify\HotkeyHound.exe" --selftest                 # 涉及钩子的改动：7/7 必须全绿
   ```
2. **更新两个 CHANGELOG**（`CHANGELOG.md` 与 `CHANGELOG.zh-CN.md`）与 `src/Core/SharedState.cs`
   里的 `AppInfo.Version`。
3. **打标签并推送**：
   ```
   git tag -a v1.0.0 -m "HotkeyHound 1.0.0"
   git -c http.proxy=http://127.0.0.1:65532 push origin main v1.0.0
   ```
4. **建 Release**：GitHub → Releases → Draft a new release → 选刚推的标签；
   标题 `HotkeyHound 1.0.0 — <代号>`；正文从两版 CHANGELOG 复制；
   附件上传 `dist\HotkeyHound-1.0.0-win-x64.zip`（内含 exe、LICENSE、双语文档与帮助文档）。
5. **校验和**：`dist\SHA256SUMS.txt` 里的哈希要写进 Release 正文。

打包含源码与文档，产物本身不进货（`.gitignore` 已排除 `dist/`）。

## 三、分发（决定早期曝光，按性价比排序）

1. 去两个常年高流量的问答下面给出**真实答案**并附链接（只回答问题，不要贴广告）：
   - Super User: "How do I find out which programs have registered global hotkeys in Windows 10"
   - Stack Overflow: "Find out what process registered a global hotkey? (Windows API)"
2. Reddit：r/Windows11、r/Windows10、r/software、r/PowerToys（这个群体天天撞热键冲突）
3. Hacker News 的 Show HN —— 标题用"我们踩过的坑"最好使：
   *"Show HN: A hotkey conflict inspector that doesn't pretend Windows can enumerate hotkeys"*
4. 中文侧：V2EX、少数派、知乎。中文搜索词是「全局热键 冲突」「快捷键被占用」，
   中文 README 的标题与首段已经把这些词写进去了。
5. awesome-windows / awesome-tools 之类的清单仓库提 PR。

## 四、发布前必须复查的三条底线

- [ ] 界面加载**不安装任何钩子**（只有点「开始监听」/「布防」才装）
- [ ] 全局钩子**用的是原生导出地址**，不是托管委托（见 `AGENTS.md` 第一节）
- [ ] 引擎 A/D 与报告**不写**任何配置或注册表
