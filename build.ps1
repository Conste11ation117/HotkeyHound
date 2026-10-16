# HotkeyHound 构建脚本
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1
#
# 本机没有 .NET SDK / 没有 .NET Framework 引用程序集，因此直接调用 VS 自带的
# Roslyn csc.exe，并以 -nostdlib+ 显式引用 .NET Framework 4.8 的运行时程序集。
# 原生 hook DLL 先用 vcvars64 + cl 编译，再作为资源内嵌进 exe。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$csc   = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$fx    = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$vcvars = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'

if (-not (Test-Path $csc)) { throw "找不到 C# 编译器：$csc" }
if (-not (Test-Path (Join-Path $fx 'mscorlib.dll'))) { throw "找不到 .NET Framework 运行时程序集：$fx" }

$buildDir = Join-Path $root 'build'
$distDir  = Join-Path $root 'dist'
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Path $buildDir | Out-Null
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

# ---------------------------------------------------------------- 1) 原生 hook DLL
$nativeDir = Join-Path $buildDir 'native'
New-Item -ItemType Directory -Path $nativeDir | Out-Null
$cppFile = Join-Path $root 'src\Hook\HookDll.cpp'
$defFile = Join-Path $root 'src\Hook\HookDll.def'
$hookDll = Join-Path $nativeDir 'HotkeyHound.Hook.dll'

if (-not (Test-Path $vcvars)) { throw "找不到 vcvars64.bat：$vcvars" }

Write-Host '正在编译原生 hook DLL（cl, MSVC x64, /LD /MT）...' -ForegroundColor Cyan
$clLine = 'call "' + $vcvars + '" >nul && cl /nologo /LD /MT /O2 /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE ' `
        + '/FeHotkeyHound.Hook.dll "' + $cppFile + '" /link /DEF:"' + $defFile + '" user32.lib kernel32.lib'
Push-Location $nativeDir
try {
  & cmd.exe /c $clLine
  if ($LASTEXITCODE -ne 0) { throw "原生 DLL 编译失败，cl 退出码 $LASTEXITCODE" }
} finally { Pop-Location }

if (-not (Test-Path $hookDll)) { throw "原生 DLL 没有产出：$hookDll" }
$hookSize = (Get-Item $hookDll).Length
Write-Host ('原生 hook DLL 编译成功：{0} ({1:N0} 字节)' -f $hookDll, $hookSize) -ForegroundColor Green

# ---------------------------------------------------------------- 2) 托管程序
$sources = @()
$sources += Get-ChildItem -Path (Join-Path $root 'src')   -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
$sources += Get-ChildItem -Path (Join-Path $root 'tests') -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw '没有找到任何 C# 源文件' }

$out = Join-Path $buildDir 'HotkeyHound.exe'

# DPI 感知清单（PerMonitorV2）：避免在 150%/200% 缩放下被系统位图缩放、字体发糊。
$manifest = Join-Path $root 'app.manifest'
if (-not (Test-Path $manifest)) { throw "找不到 app.manifest：$manifest" }

# 帮助文档 HTML（构建时内嵌进 exe，供帮助窗口与「在浏览器中打开」使用）。
$helpZh = Join-Path $root 'docs\help.zh-CN.html'
$helpEn = Join-Path $root 'docs\help.en-US.html'
if (-not (Test-Path $helpZh)) { throw "找不到帮助文档：$helpZh" }
if (-not (Test-Path $helpEn)) { throw "找不到帮助文档：$helpEn" }

$refs = @(
  'mscorlib.dll', 'System.dll', 'System.Core.dll',
  'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll'
) | ForEach-Object { '-r:' + (Join-Path $fx $_) }

$cscArgs = @(
  '-nologo',
  '-nostdlib+',
  '-langversion:7.3',
  '-platform:x64',
  '-target:winexe',
  '-codepage:65001',
  '-optimize+',
  '-warn:4',
  '-nowarn:0618',
  "-out:$out",
  "-win32manifest:$manifest",
  "-resource:$hookDll,HotkeyHound.Hook.dll",
  "-resource:$helpZh,help.zh-CN.html",
  "-resource:$helpEn,help.en-US.html"
) + $refs + $sources

Write-Host '正在编译 HotkeyHound.exe（csc.exe, .NET Framework 4.8, C# 7.3）...' -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，csc 退出码 $LASTEXITCODE" }

$distExe = Join-Path $distDir 'HotkeyHound.exe'
try {
  Copy-Item $out $distExe -Force -ErrorAction Stop
} catch {
  throw ("无法写入 {0}：{1}`n可能还有 HotkeyHound.exe 正在运行。请先结束它（任务管理器或 taskkill /F /IM HotkeyHound.exe）后重试。" -f $distExe, $_.Exception.Message)
}

$size = (Get-Item $distExe).Length
Write-Host ('构建成功：{0} ({1:N0} 字节，内嵌 hook DLL {2:N0} 字节)' -f $distExe, $size, $hookSize) -ForegroundColor Green
