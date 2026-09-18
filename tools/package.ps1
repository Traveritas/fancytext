# ASCII-safe ids; Chinese only in README content written with explicit UTF8 encoding.
# Packages FancyText Desktop as a self-contained single-file exe + README into dist/.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
# 版本号与 csproj 同步（单一真源，避免打包名与内容错位）
$csproj = [xml][IO.File]::ReadAllText((Join-Path $root 'src\FancyText.Desktop\FancyText.Desktop.csproj'), [Text.UTF8Encoding]::new($false))
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'cannot read Version from csproj' }
$outDir = Join-Path $root 'dist'
$stage = Join-Path $outDir "FancyText.Desktop-$version"
$zipPath = Join-Path $outDir "FancyText.Desktop-$version-win-x64.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host '== publish self-contained single file =='
dotnet publish (Join-Path $root 'src\FancyText.Desktop') -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $stage
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# pdb 是调试符号，分发不需要（白占体积）
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force

$readme = @"
花式文字 桌面版 v$version
======================

花式 Unicode 文字转换弹窗：130 种样式实时预览，回车复制。

快速上手
--------
1. 双击 FancyText.Desktop.exe 启动（常驻托盘，不占任务栏）。
2. 按 Ctrl+Alt+F 唤出（自动预填其它应用中选中的文字），输入文字，↑↓ 浏览样式，Enter 复制并收起。
   - Ctrl+D 收藏 / Ctrl+R 随机换一个 / Esc 收起
   - 家族条目（如"翅膀 · 28 个样式"）按 Enter 钻入，Esc 返回上一级
   - 纯键盘：Tab 展开/收起筛选菜单，Ctrl+Tab 循环切换筛选，
     列表到顶按 ↑ 进按钮区后 ←→ 循环、Enter 激活
3. 托盘图标右键：打开 / 设置 / 退出。

设置
----
托盘右键 -> 设置（或运行 exe --settings）：
语言、主题（浅色/深色/跟随系统）、背景材质（跟随系统 Mica/纯色）、强调色、
预览字号、减少动效、唤出快捷键、开机自启动、唤出时预填选中文字/剪贴板、
预填兼容模式（模拟复制兜底）、复制后收起、弹窗位置；
样式包面板：5 个官方扩充包（华丽装饰 / 星月夜 / 叠加特效 / 颜文字情绪 / 火星文非主流）
一键安装、启用/停用/卸载；纯 JSON 样式包放目录即生效。

说明
----
- 绿色软件：单文件、免安装。配置在 %LOCALAPPDATA%\FancyText\
  （desktop.json 设置 / state.json 收藏与最近 / diag.log 诊断日志）。
- 卸载：托盘退出后删除 exe 即可；若开过"开机自启动"，先在设置里关掉。
- 姊妹项目：PowerToys Command Palette 插件版（另附），收藏数据互通。
"@

[System.IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, [System.Text.UTF8Encoding]::new($true))

Write-Host '== zip =='
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force
Remove-Item $stage -Recurse -Force

$size = (Get-Item $zipPath).Length / 1MB
Write-Host ("done: {0} ({1:N1} MB)" -f $zipPath, $size)
