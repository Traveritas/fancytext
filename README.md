# 花式文字 for PowerToys Command Palette

把剪贴板或输入的文字一键转换为 **菊花体、魔鬼文字（Zalgo）、花藤体、花体 𝓕𝓪𝓷𝓬𝔂、火星文** 等 **126 种**花式 Unicode 样式，回车即复制。另附同引擎的命令行工具 `fancy`。

调研结论（同类插件不存在、全部样式来源与码点依据）见 [docs/01-调研报告-花式文字插件.md](docs/01-调研报告-花式文字插件.md)；独立工具形态分析见 [docs/02-独立工具探索.md](docs/02-独立工具探索.md)。

## 功能

- **两级导航**：首页按分类展示（中文特效 / 花体 / 装饰 / 变换 / 中文 / 编码，外加「全部样式」），每类入口的副标题实时预览代表样式效果与当前文本下的可用数量；回车进入二级页浏览该分类全部样式，面包屑返回。
- **根搜索框直转**：在 Command Palette 根搜索框输入任意文字 → 「花式文字转换」回车，首页直接跟随你输入的内容（Fallback query 实时同步）。
- **收藏 + 最近使用**：样式上右键即可收藏（⭐），收藏与最近使用的样式会出现在首页顶部，**回车直接复制**，不用进二级页；状态持久化到本地。
- **自动取剪贴板**：搜索框为空时自动转换剪贴板内容。
- **实时预览**：二级页每个样式一行，标题即转换结果；详情页显示完整结果、实现机制（码点）与原文。
- **回车复制**，Toast 反馈；右键有「复制并保持打开」「收藏」等命令。
- **还原功能**：「变换」分类内置「还原（去装饰）」，可把被组合符装饰过的文字清洗回原文；「中文」分类有「火星文还原」。

### 样式分类（126 个）

| 分类 | 数量 | 代表样式 |
|---|---|---|
| 中文特效（组合符，中英通吃） | 46 | 菊花体×4、删除线/下划线/上划线家族、圈圈/包围框/菱形/禁止字、飞鸟/蝴蝶/尾巴/冒烟×3/萌芽/爱心文、花藤字×2、闪电/连弧/横条组合符、魔鬼文字（Zalgo）×3 |
| 花体（Unicode 区段映射） | 28 | 粗体/斜体/花体/哥特/空心/等宽/无衬线系、泡泡字、黑底圈字、方块字、括号字、旗帜字母、全角、小型大写、上下标、货币体、符号体、无衬线圈/双圈数字、俄化 |
| 装饰（前后缀/逐字/分字） | 34 | 经典/华丽/精美/皇冠/典雅/钻石/双层/流光/柔光/神秘/藏文系翅膀、星光/花/闪耀边框、日式括号、分字空格、宽体 |
| 变换 | 8 | 倒转 oʇʇǝ、镜像、倒序、Leet、全大写/全小写/交替大小写、还原（去装饰） |
| 中文 | 2 | 火星文（2088 字字典）、火星文还原 |
| 编码 | 8 | Base64、ROT13、摩斯电码、盲文 ⠓⠑⠇⠇⠕、NATO、A1Z26、二进制、十六进制 |

## 独立桌面版（产品线 #2，无需 PowerToys）

WPF 弹窗式转换器：托盘常驻 + 全局热键，**与插件共享同一引擎和收藏**（`%LOCALAPPDATA%\FancyText\state.json`）。

```bash
# 构建/单文件发布（产物约 270KB，需 .NET 8 运行时；改 --self-contained true 可免装运行时）
dotnet publish src/FancyText.Desktop -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

- **唤出**：全局热键 `Ctrl+Alt+F`（改热键：编辑 `%LOCALAPPDATA%\FancyText\desktop.json`，格式 `{"hotkey":"Ctrl+Alt+Shift+G"}`）、托盘双击、或再次运行 exe；单实例。
- **弹窗**：自动预填剪贴板（无则示例文字），输入实时预览（150ms 防抖，预览只转换前 64 字素）；窗口出现在鼠标附近，点击别处自动隐藏。
- **操作**：`Enter` 复制选中样式的**全文**转换并隐藏；`Esc` 隐藏；`Ctrl+D` 收藏（⭐，插件同步可见）；`Ctrl+R` 随机换一个；`↑↓` 浏览；分类筛选含全部/收藏/最近/六大类。

## 命令行工具（开发/脚本用途）

```bash
dotnet publish src/FancyText.Cli -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false

fancy "你好 Hello"          # 全部样式表格预览
fancy --list                # 样式 ID 清单
fancy bold-script "Hi"      # 单样式，只输出结果（可管道）
fancy --random "你好"       # 随机样式
fancy --json "你好"         # JSON 输出（供其它程序消费）
```

## 项目结构

```
src/FancyText.Core/        转换引擎（无 UI 依赖，CLI 与插件共用）
  TextTransforms.cs        四个正交原语：MapReplace / AppendMark / WrapString+WrapEach / Spacing+Reverse
  ZalgoTransformer.cs      魔鬼文字（上/中/下三组组合符随机叠加，强度可调，可反向清洗）
  LatinMaps.cs             拉丁映射表（数学字母区段+洞字符、带圈/方块、全角、上下标、倒转/镜像、盲文…）
  MartianDictionary.cs     火星文字典加载（嵌入资源，Rune 对齐）
  StyleCatalog.cs(.Expanded)  全部 124 个样式定义
  Resources/spark-simple.json  cnchar 火星文字典（MIT）
src/FancyText.CmdPal/      Command Palette 扩展（WinUI3 / MSIX）
  Program.cs               COM 服务器入口
  FancyTextExtension.cs    IExtension 实现（Guid 与清单一致）
  FancyTextCommandsProvider.cs  顶层命令 + Fallback（根搜索框 query → 共享文本）
  Pages/DebouncedTextPageBase.cs 动态页基类：防抖/去重/剪贴板缓存/字素截断/诊断日志
  Pages/FancyTextHomePage.cs     一级首页：收藏/最近快速条目 + 分类入口 + 代表样式预览
  Pages/FancyTextStylesPage.cs   二级页：某分类（或全部）的样式列表
  Commands/CopyTextCommandEx.cs  复制命令（惰性全文转换 + 使用记录）
  Commands/StateCommands.cs      收藏切换 / 跳转分类
  Helpers/UsageState.cs          收藏与最近使用（%LOCALAPPDATA%\FancyText\state.json）
src/FancyText.Cli/         命令行工具 fancy（--list/--json/--random/单样式）
src/FancyText.Desktop/     独立桌面版（WPF：全局热键 + 托盘 + 弹窗转换器，与插件共享收藏）
tests/FancyText.Core.Tests/  自检测试（117 项断言）+ `-- demo`（效果预览）+ `-- bench`（性能基准）
reference/                 参考项目（ChangeCaseExtension、cnchar 克隆，仅研读，不参与构建）
docs/                      调研报告、独立工具探索
```

## 性能设计

目标是让扩展与命令面板宿主（Microsoft.CmdPal.UI）两侧的内存开销都与「刷新次数 × 文本长度」解耦：

- **列表项持久复用**：各页的 ListItem/命令/Details/Tag 对象只创建一次，刷新只更新属性——宿主侧 WinRT 包装对象数量恒定；
- 预览只转换输入的**前 64 个字素**（增量截断，与全文长度无关）；回车复制时才对全文做转换；
- 重建有 250ms 防抖，文本未变化直接跳过；剪贴板 3 秒缓存；
- Toast 结果 / 图标 / 分类 Tag 全部静态复用；共享文本变更经事件广播，页面按需重建。

引擎基准（`dotnet run --project tests/FancyText.Core.Tests -- bench`）：全目录 × 64 字输入，约 0.2ms/次、~80KB 临时分配（gen0 即回收）。

诊断：每次列表重建向 `%LOCALAPPDATA%\FancyText\diag.log` 写一行（托管内存/工作集/GC 次数），文件超 1MB 自动清理。

## 构建与安装

要求：
- .NET SDK **10** + **Windows SDK 10.0.26100**（`winget install Microsoft.WindowsSDK.10.0.26100`）
- `Microsoft.CommandPalette.Extensions` 锁定在 **0.6.251022008**（0.11+ 需要 .NET 10 时代的更新投影，暂不升）
- 扩展目标框架为 `net9.0-windows10.0.22621.0`（0.6 版 Toolkit 按 net9 编译）
- Windows 10 19041+（实际建议 Win11 + PowerToys ≥ 0.90）

```bash
# 运行引擎测试（117 项断言）
dotnet run --project tests/FancyText.Core.Tests

# 效果预览（不进 UI，直接打印全部样式的转换结果）
dotnet run --project tests/FancyText.Core.Tests -- demo "你好 Hello"

# 编译扩展（x64）
dotnet build src/FancyText.CmdPal -p:Platform=x64

# 打包 MSIX（产出 AppPackages\FancyText.CmdPal_1.0.0.0_x64_Test\）
dotnet publish src/FancyText.CmdPal -c Release -r win-x64 -p:Platform=x64 \
  -p:AppxPackageSigningEnabled=true -p:UapAppxPackageBuildMode=SideloadOnly \
  -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never \
  -p:PackageCertificateThumbprint=35F932FE9A128E0BDB6DBEBDE2A562AA369DFCDB
```

签名说明：仓库附带开发用自签名证书 `src/FancyText.CmdPal/FancyText.DevKey.pfx`（CN=FancyText，与清单 Publisher 一致，导出密码 `FancyTextDev`，指纹见上方命令），对应私钥也在当前用户证书存储 `Cert:\CurrentUser\My` 中。换机或丢失时重新生成：

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=FancyText' -KeyUsage DigitalSignature `
  -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
Export-PfxCertificate -Cert $cert -FilePath src\FancyText.CmdPal\FancyText.DevKey.pfx -Password (ConvertTo-SecureString 'FancyTextDev' -Force -AsPlainText)
# 然后用新指纹替换命令中的 PackageCertificateThumbprint，并同步更新清单 Publisher
```

安装（旁加载）：
1. Windows 设置 → 系统 → 开发者选项 → 启用**开发者模式**。
2. 进入 `AppPackages\FancyText.CmdPal_1.0.0.0_x64_Test\`，右键 `Add-AppDevPackage.ps1` → **使用 PowerShell 运行**（会一并安装配套证书与依赖）。
3. 打开 Command Palette（Win+Alt+Space），搜索「花式文字」；直接在根搜索框输入文字也会出现 fallback 项。

卸载：设置 → 应用 → 花式文字。正式分发（微软商店 / WinGet / 官方 Gallery）前，请把 Identity Publisher 换成自己的发布者身份并重新签名。

## 已知限制 / 路线图

- 魔鬼文字是随机的，复制形态与预览不完全一致（计划加「换一批」右键命令）；
- 设置页（纯示例模式 / 预览字数 / 分类显隐）与简繁转换排期中；
- 组合符渲染因平台/字体而异（手机与游戏内最佳，PC 部分字体显示方块）——详情页已标注码点；
- 火星文为字典逐字替换，不含语气词与符号装饰的完整「火星文风格」；
- 独立网页版（单文件 HTML）见 docs/02 的路线建议。

## 许可

- 本项目代码：MIT（见 [LICENSE](LICENSE)）
- 火星文字典数据：来自 [cnchar](https://github.com/theajack/cnchar)（MIT）
