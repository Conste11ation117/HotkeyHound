# HotkeyHound 构建脚本
# 用法： powershell -ExecutionPolicy Bypass -File build.ps1
#
# 本机没有 .NET SDK / 没有 .NET Framework 引用程序集，因此直接调用 VS 自带的
# Roslyn csc.exe，并以 -nostdlib+ 显式引用 .NET Framework 4.8 的运行时程序集。
#
# 原生部分要编两个位数：钩子只能注入与安装者同位数的进程，所以除了 x64 的
# HotkeyHound.Hook.dll，还需要 x86 的 HotkeyHound.Hook32.dll 和装它的 32 位宿主
# HotkeyHound.Helper32.exe。四者最终只有 HotkeyHound.exe 对外分发，另外三个内嵌为资源。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

# 工具链自动探测：本机装的是 VS 2026 Community，GitHub 的 windows runner 上是 VS 2022，
# 所以不能写死路径。先按通配符找（覆盖任意版本/任意版本号），找不到再退回 vswhere。
$vsRoots = @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")
function Find-Tool([string]$relative) {
  foreach ($r in $vsRoots) {
    $hit = Get-ChildItem -Path (Join-Path $r "*\$relative") -ErrorAction SilentlyContinue |
           Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
  }
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * -find $relative 2>$null | Select-Object -First 1
    if ($found -and (Test-Path $found)) { return $found }
  }
  return $null
}

$csc     = Find-Tool 'MSBuild\Current\Bin\Roslyn\csc.exe'
$vcvars64 = Find-Tool 'VC\Auxiliary\Build\vcvars64.bat'
$vcvars32 = Find-Tool 'VC\Auxiliary\Build\vcvars32.bat'
$fx      = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$fx32    = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'

if (-not $csc)     { throw '找不到 Roslyn csc.exe：请安装 Visual Studio（含 .NET 桌面开发工作负载）或 Build Tools' }
if (-not $vcvars64) { throw '找不到 vcvars64.bat：请安装 Visual Studio 的「使用 C++ 的桌面开发」工作负载' }
if (-not $vcvars32) { throw '找不到 vcvars32.bat：请安装 Visual Studio 的「使用 C++ 的桌面开发」工作负载（x86 工具集）' }
if (-not (Test-Path (Join-Path $fx 'mscorlib.dll'))) { throw "找不到 .NET Framework 运行时程序集：$fx" }
if (-not (Test-Path (Join-Path $fx32 'mscorlib.dll'))) { $fx32 = $fx }
Write-Host ("csc     = " + $csc) -ForegroundColor DarkGray
Write-Host ("vcvars64 = " + $vcvars64) -ForegroundColor DarkGray
Write-Host ("vcvars32 = " + $vcvars32) -ForegroundColor DarkGray

# 读 PE 头里的 Machine 字段，确认产物位数没有搞反（0x8664=x64，0x014C=x86）。
# 这样构建自己就能证明位数，不必再手工跑 dumpbin。
function Get-PeMachine([string]$path) {
  $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
  try {
    $reader = New-Object System.IO.BinaryReader($stream)
    if ($stream.Length -lt 0x40) { return 0 }
    $stream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    if ($peOffset -le 0 -or $peOffset -gt ($stream.Length - 6)) { return 0 }
    $stream.Position = $peOffset + 4
    return [int]$reader.ReadUInt16()
  } finally {
    $stream.Dispose()
  }
}

function Assert-PeMachine([string]$path, [int]$expected, [string]$label) {
  $machine = Get-PeMachine $path
  if ($machine -ne $expected) {
    throw ("{0} 的位数不对：PE Machine=0x{1:X4}，期望 0x{2:X4}（文件：{3}）" -f $label, $machine, $expected, $path)
  }
  Write-Host ('  {0}：PE Machine=0x{1:X4}（{2}）' -f (Split-Path -Leaf $path), $machine, $label) -ForegroundColor DarkGreen
}

$buildDir = Join-Path $root 'build'
$distDir  = Join-Path $root 'dist'
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Path $buildDir | Out-Null
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

# ---------------------------------------------------------------- 1) 原生 hook DLL（两个位数）
$nativeDir = Join-Path $buildDir 'native'
New-Item -ItemType Directory -Path $nativeDir | Out-Null
$cppFile = Join-Path $root 'src\Hook\HookDll.cpp'
$defFile = Join-Path $root 'src\Hook\HookDll.def'
$hookDll   = Join-Path $nativeDir 'HotkeyHound.Hook.dll'
$hook32Dll = Join-Path $nativeDir 'HotkeyHound.Hook32.dll'

Write-Host '正在编译原生 hook DLL（cl, MSVC x64, /LD /MT）...' -ForegroundColor Cyan
$clLine = 'call "' + $vcvars64 + '" >nul && cl /nologo /LD /MT /O2 /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE ' `
        + '/FeHotkeyHound.Hook.dll "' + $cppFile + '" /link /DEF:"' + $defFile + '" user32.lib kernel32.lib'
Push-Location $nativeDir
try {
  & cmd.exe /c $clLine
  if ($LASTEXITCODE -ne 0) { throw "原生 DLL 编译失败，cl 退出码 $LASTEXITCODE" }
} finally { Pop-Location }
if (-not (Test-Path $hookDll)) { throw "原生 DLL 没有产出：$hookDll" }

Write-Host '正在编译 32 位原生 hook DLL（cl, MSVC x86, /LD /MT）...' -ForegroundColor Cyan
$clLine32 = 'call "' + $vcvars32 + '" >nul && cl /nologo /LD /MT /O2 /W3 /EHsc /utf-8 /DUNICODE /D_UNICODE ' `
          + '/FeHotkeyHound.Hook32.dll "' + $cppFile + '" /link /DEF:"' + $defFile + '" user32.lib kernel32.lib'
Push-Location $nativeDir
try {
  & cmd.exe /c $clLine32
  if ($LASTEXITCODE -ne 0) { throw "32 位原生 DLL 编译失败，cl 退出码 $LASTEXITCODE" }
} finally { Pop-Location }
if (-not (Test-Path $hook32Dll)) { throw "32 位原生 DLL 没有产出：$hook32Dll" }

Assert-PeMachine $hookDll 0x8664 'x64 hook DLL'
Assert-PeMachine $hook32Dll 0x014C 'x86 hook DLL'
$hookSize = (Get-Item $hookDll).Length
$hook32Size = (Get-Item $hook32Dll).Length
Write-Host ('原生 hook DLL 编译成功：{0} ({1:N0} 字节)、{2} ({3:N0} 字节)' -f $hookDll, $hookSize, $hook32Dll, $hook32Size) -ForegroundColor Green

# ---------------------------------------------------------------- 2) 32 位宿主（x86 控制台程序）
$helper32 = Join-Path $buildDir 'HotkeyHound.Helper32.exe'
$helperSource = Join-Path $root 'src\Helper32\Helper32Main.cs'
if (-not (Test-Path $helperSource)) { throw "找不到 32 位宿主源码：$helperSource" }

# 32 位宿主用 32 位运行时程序集编译，避免 -platform:x86 引用 64 位目录里的 mscorlib。
$helperRefs = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '-r:' + (Join-Path $fx32 $_) }
$helperArgs = @(
  '-nologo',
  '-nostdlib+',
  '-langversion:7.3',
  '-platform:x86',
  '-target:exe',
  '-codepage:65001',
  '-optimize+',
  '-warn:4',
  "-out:$helper32"
) + $helperRefs + @($helperSource)

Write-Host '正在编译 32 位宿主 HotkeyHound.Helper32.exe（csc.exe, x86）...' -ForegroundColor Cyan
& $csc $helperArgs
if ($LASTEXITCODE -ne 0) { throw "32 位宿主编译失败，csc 退出码 $LASTEXITCODE" }
if (-not (Test-Path $helper32)) { throw "32 位宿主没有产出：$helper32" }
Assert-PeMachine $helper32 0x014C 'x86 helper'
$helper32Size = (Get-Item $helper32).Length
Write-Host ('32 位宿主编译成功：{0} ({1:N0} 字节)' -f $helper32, $helper32Size) -ForegroundColor Green

# ---------------------------------------------------------------- 3) 托管主程序
$sources = @()
$sources += Get-ChildItem -Path (Join-Path $root 'src') -Filter *.cs -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/]Helper32[\\/]' } |
            ForEach-Object { $_.FullName }
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

# 钩子型热键知识库（构建时内嵌；用户可在 %LOCALAPPDATA%\HotkeyHound\catalog.json 覆盖/追加）。
$catalog = Join-Path $root 'data\hook-catalog.json'
if (-not (Test-Path $catalog)) { throw "找不到钩子知识库：$catalog" }

# 正式程序图标（256×256 PNG-in-ICO，由工具生成；窗口图标也从 exe 的这个资源读取）。
$icon = Join-Path $root 'assets\hotkeyhound.ico'
if (-not (Test-Path $icon)) { throw "找不到程序图标：$icon" }

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
  "-win32icon:$icon",
  "-resource:$hookDll,HotkeyHound.Hook.dll",
  "-resource:$hook32Dll,HotkeyHound.Hook32.dll",
  "-resource:$helper32,HotkeyHound.Helper32.exe",
  "-resource:$helpZh,help.zh-CN.html",
  "-resource:$helpEn,help.en-US.html",
  "-resource:$catalog,hook-catalog.json"
) + $refs + $sources

Write-Host '正在编译 HotkeyHound.exe（csc.exe, .NET Framework 4.8, C# 7.3）...' -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，csc 退出码 $LASTEXITCODE" }
Assert-PeMachine $out 0x8664 'x64 主程序'

$distExe = Join-Path $distDir 'HotkeyHound.exe'
try {
  Copy-Item $out $distExe -Force -ErrorAction Stop
} catch {
  throw ("无法写入 {0}：{1}`n可能还有 HotkeyHound.exe 正在运行。请先结束它（任务管理器或 taskkill /F /IM HotkeyHound.exe）后重试。" -f $distExe, $_.Exception.Message)
}

$size = (Get-Item $distExe).Length
Write-Host ('构建成功：{0} ({1:N0} 字节)' -f $distExe, $size) -ForegroundColor Green
Write-Host ('  内嵌：HotkeyHound.Hook.dll (x64) {0:N0} 字节、HotkeyHound.Hook32.dll (x86) {1:N0} 字节、HotkeyHound.Helper32.exe (x86) {2:N0} 字节' -f $hookSize, $hook32Size, $helper32Size) -ForegroundColor Green
Write-Host ('  构建中间产物：{0}' -f $buildDir) -ForegroundColor DarkGray
