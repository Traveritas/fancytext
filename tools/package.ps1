# ASCII-safe ids; Chinese only in README content written with explicit UTF8 encoding.
# Packages FancyText Desktop as a self-contained single-file exe + README into dist/.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
# 版本号读 Directory.Build.props（单一真源，避免打包名与内容错位）
$props = [xml][IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'), [Text.UTF8Encoding]::new($false))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'cannot read Version from Directory.Build.props' }
$outDir = Join-Path $root 'dist'
# 临时目录带 .stage 前缀：不能和解压出来试用的 FancyText.Desktop-x.y.z 同名，否则打包会把它删掉
$stage = Join-Path $outDir ".stage-FancyText.Desktop-$version"
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

# 许可与第三方声明必须随二进制分发（MIT 要求随副本携带版权与许可声明；
# 自包含 exe 内含 MIT 的 .NET 运行时，内嵌词典含 Apache-2.0 的 OpenCC 词表）
foreach ($doc in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item (Join-Path $root $doc) $stage -Force
}

$readme = @"
花式文字 桌面版 v$version
======================

花式 Unicode 文字转换弹窗：130 种样式实时预览，回车复制。

快速上手
--------
1. 双击 FancyText.Desktop.exe 启动（常驻托盘，不占任务栏）。
2. 按 Ctrl+Alt+F 唤出（自动预填其它应用中选中的文字；预填剪贴板默认关闭，可在设置里打开），
   输入文字，↑↓ 浏览样式，Enter 复制并收起。
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
样式包面板：4 个官方包（华丽装饰 / 星月夜 / 叠加特效 / 颜文字）一键安装、启用/停用/卸载；
「获取更多样式包」一键安装在线索引（fancytext-styles 仓库）里的包；「导入样式包…」导入别人分享的 .json；
也可以把 .json 直接放进 %LOCALAPPDATA%\FancyText\styles\，再点「重新加载」生效。
火星文正反向与去装饰均已内置（原火星文包退役，升级后自动清理）。

说明
----
- 绿色软件：单文件、免安装。配置在 %LOCALAPPDATA%\FancyText\
  （desktop.json 设置 / state.json 收藏与最近 / diag.log 诊断日志）。
- 卸载：托盘退出后删除 exe 即可；若开过"开机自启动"，先在设置里关掉。
- 许可：本项目代码 MIT（LICENSE）；第三方组件与内嵌词典数据（.NET 运行时、cnchar、
  OpenCC、pinyin-data）的许可与版权声明见 THIRD-PARTY-NOTICES.md。
"@

[System.IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, [System.Text.UTF8Encoding]::new($true))

Write-Host '== zip =='
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force
Remove-Item $stage -Recurse -Force

$size = (Get-Item $zipPath).Length / 1MB
Write-Host ("done: {0} ({1:N1} MB)" -f $zipPath, $size)
