# 花式文字 FancyText

把剪贴板或输入的文字一键转换为 **菊花体、魔鬼文字（Zalgo）、花藤体、花体 𝓕𝓪𝓷𝓬𝔂、火星文** 等 **130 种**内置花式 Unicode 样式（含简⇄繁、拼音），回车即复制；支持**样式包**扩充（官方包 + 导入分享）。

三种形态共用同一引擎：**独立桌面版**（主形态，免 PowerToys，全局热键弹窗）、**PowerToys Command Palette 插件**（维护模式：引擎持续跟随，UI 不再投入新功能）、命令行工具 `fancy`。

调研结论（同类插件不存在、全部样式来源与码点依据）见 [docs/01-调研报告-花式文字插件.md](docs/01-调研报告-花式文字插件.md)；独立工具形态分析见 [docs/02-独立工具探索.md](docs/02-独立工具探索.md)。

## 独立桌面版（主形态，无需 PowerToys）

WPF 弹窗式转换器：托盘常驻 + 全局热键。免安装绿色软件，单文件 exe（自包含，无需 .NET 运行时），与插件/CLI 共享同一引擎和收藏（`%LOCALAPPDATA%\FancyText\state.json`）。

**下载**：GitHub Releases 的 `FancyText.Desktop-x.x.x-win-x64.zip`（解压即用），或自行构建：

```bash
dotnet publish src/FancyText.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# 或 tools/package.ps1 一键打包 zip
```

- **唤出**：全局热键 `Ctrl+Alt+F`（设置页可视化换绑，占用自动回退旧键）、托盘双击、或再次运行 exe；单实例。
- **弹窗**：Win11 Mica 系统材质，明暗主题自动跟随（设置页可切纯色；实现要点：无边框窗口需先 `DwmExtendFrameIntoClientArea(-1)` 扩展框架区，Mica 才有落笔处），唤出淡入上移入场、隐藏同款退出（167/200ms 与 150ms，跟随系统动画开关，可「减少动效」全关）；自动预填剪贴板（可关，无则示例文字），输入实时预览（150ms 防抖，预览只转换前 64 字素）；窗口出现在鼠标附近（可改主屏居中），点击别处自动隐藏。
- **操作**：`Enter` 复制选中样式的**全文**转换并以退出动画收起；`Esc` 隐藏；`Ctrl+D` 收藏（⭐，插件同步可见）；`Ctrl+R` 随机换一个；`↑↓` 浏览。纯键盘模型（焦点全程留在输入框，按钮不进 Tab 链）：`Tab` 展开/收起筛选菜单（`↑↓` 选择、`Enter` 应用）；`Ctrl+Tab`/`Ctrl+Shift+Tab` 循环切换筛选；列表到顶再按 `↑` 或非族行按 `→` 进入按钮区，`←→` 在「全部 ▾」/⭐/🕘 间循环、`Enter` 激活、`↓`/`Esc` 回列表并恢复原选中；族条目 `→` 钻入、`←` 返回上一级（取舍：列表区非族行的 `→` 用于进按钮区，文本光标无法用键盘右移）。鼠标侧：`全部 ▾` 下拉 + ⭐/🕘 图标。
- **家族目录钻取**：翅膀、删除线、菊花体、包围字、边框等 ≥4 个的同族样式在「全部/分类」视图折叠成一行族条目（族名 + 数量 ▸ + 代表预览），`Enter` 钻入只看该族（首行 ‹ 返回，`Esc` 先返回再隐藏）；收藏/最近视图始终平铺。样式包按包名归族（如「华丽装饰扩充」）。
- **选中文字预填**：唤出时自动读取其它应用中选中的文字（UIA 只读，不动剪贴板；主流浏览器/Office/记事本均支持），拿不到再回退剪贴板；设置页可关。设置页另有「预填兼容模式（模拟复制）」（默认关，仅主开关开启时可用）：UIA 不支持的应用改用模拟 `Ctrl+Insert` 复制兜底（读后立即还原剪贴板，不用有中断语义的 Ctrl+C）；选读失败会写 `diag.log` 注明原因。
- **设置**（托盘右键 → 设置，全部即时生效）：主题（浅色/深色/跟随系统，系统切换明暗自动跟随）、背景材质（跟随系统 Mica / 纯色）、强调色（色板/自定义/**跟随系统强调色**，系统改色后自动跟随）、预览字号、减少动效、语言（中/英/跟随系统）、唤出快捷键录制、开机自启动、唤出时预填选中文字/剪贴板、复制后收起、弹窗位置。
- **渲染**：花式字符横跨十余文字系统，内置 GDI 动态字体覆盖库（FontCoverage）——组合符按码点实时解析到覆盖它的系统字体，任意机器免配置不出现豆腐块；深浅主题全控件适配；Mica 走 DWM 系统材质（零应用侧缓冲），动效只动 Opacity/位移（GPU 合成，不用位图特效）。
- **轻量**：隐藏 1.5s 后自动修剪工作集，任务管理器常驻观感 ~7-10MB。

## 样式包：导入与分享

样式在本引擎里是**纯数据**（声明式步骤管道，不含可执行代码），一组样式可以存成一个 `.json` 包文件互相分享：

- **导入**：桌面版 设置 →「样式包」→ 导入样式包…；或把 `.json` 直接丢进 `%LOCALAPPDATA%\FancyText\styles\`（一文件一包，放下即生效）；命令行 `fancy --import 包.json`。
- **导出**：桌面版设置页「导出收藏为包…」把收藏夹生成包文件；命令行 `fancy --export <样式ID…> --out 包.json`。
- **官方扩展包**：exe 内嵌「华丽装饰扩充」（33 个装饰样式：成对括号/星花雪叶/线框角隅/分隔线等），设置页样式包面板一键安装，无需下载；与导入包同格式同校验。
- **管理**：设置页样式包面板——每包可**停用/启用**（停用不删文件，样式即时从目录隐藏，三端一致）、卸载（损坏的包标红且只能卸载）、展开查看包内样式与当前文本的实时预览；`fancy --packs`（停用包有标记）/ `fancy --remove 包名`。
- 桌面版导入/卸载/停启用即时生效；命令面板插件在面板进程重启后生效。列表中包样式的分类后会标注来源包名。
- **安全校验**：ID 冲突（内置/其它包）拒绝导入；孤立代理对、控制字符、超限参数（映射条数/步骤数/文件大小 2MB）一律拦截。
- 包格式与全部步骤写法（mapReplace / useMap / appendMark / wrapString / wrapEach / spacing / reverse / algorithm / ifChanged 守卫）见可直接导入体验的 [docs/sample-pack.json](docs/sample-pack.json)；样式可带可选 `nameEn` / `noteEn` 双语字段，英文名缺失时回退 `name`。

### 样式分类（130 个）

| 分类 | 数量 | 代表样式 |
|---|---|---|
| 特效（组合符，中英通吃） | 46 | 菊花体×4、删除线/下划线/上划线家族、圈圈/包围框/菱形/禁止字、飞鸟/蝴蝶/尾巴/冒烟×3/萌芽/爱心文、花藤字×2、闪电/连弧/横条组合符、魔鬼文字（Zalgo）×3 |
| 英文/字母花体（Unicode 区段映射） | 28 | 粗体/斜体/花体/哥特/空心/等宽/无衬线系、泡泡字、黑底圈字、方块字、括号字、旗帜字母、全角、小型大写、上下标、货币体、符号体、无衬线圈/双圈数字、俄化 |
| 装饰（前后缀/逐字/分字） | 34 | 经典/华丽/精美/皇冠/典雅/钻石/双层/流光/柔光/神秘/藏文系翅膀、星光/花/闪耀边框、日式括号、分字空格、宽体 |
| 变换 | 8 | 倒转 oʇʇǝ、镜像、倒序、Leet、全大写/全小写/交替大小写、还原（去装饰） |
| 中文 | 6 | 火星文（2088 字字典）、火星文还原、简→繁 / 繁→简（OpenCC 词级消歧：头发→頭髮）、拼音（带调 nǐ hǎo）、拼音缩写（nhm） |
| 编码 | 8 | Base64、ROT13、摩斯电码、盲文 ⠓⠑⠇⠇⠕、NATO、A1Z26、二进制、十六进制 |

## Command Palette 插件（维护模式）

> 插件端已转入**维护模式**：引擎持续跟随（新样式/包机制自动受益），UI 不再投入新功能——SDK 的列表页模型无法承载桌面端的 Mica/动效/自定义渲染，且宿主进程（Microsoft.CmdPal.UI，实测常驻 ~200MB+）不属于本项目可控范围。当前主形态为桌面版（实测宿主+插件合计 ~230MB vs 桌面版常驻 ~7-10MB）。

- **两级导航**：首页按分类展示（特效 / 英文/字母花体 / 装饰 / 变换 / 中文 / 编码，外加「全部样式」），每类入口的副标题实时预览代表样式效果与当前文本下的可用数量；回车进入二级页浏览该分类全部样式，面包屑返回。
- **根搜索框直转**：在 Command Palette 根搜索框输入任意文字 → 「花式文字转换」回车，首页直接跟随你输入的内容（Fallback query 实时同步）。
- **收藏 + 最近使用**：样式上右键即可收藏（⭐），收藏与最近使用的样式会出现在首页顶部，**回车直接复制**，不用进二级页；状态持久化到本地。
- **自动取剪贴板**：搜索框为空时自动转换剪贴板内容。
- **实时预览**：二级页每个样式一行，标题即转换结果；详情页显示完整结果、实现机制（码点）与原文。
- **回车复制**，Toast 反馈；右键有「复制并保持打开」「收藏」等命令。
- **中英双语**：与桌面版共享同一语言偏好（`state.json`），插件在面板进程重启后生效。

## 命令行工具（开发/脚本用途）

```bash
dotnet publish src/FancyText.Cli -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false

fancy "你好 Hello"          # 全部样式表格预览
fancy --list                # 样式 ID 清单
fancy bold-script "Hi"      # 单样式，只输出结果（可管道）
fancy --random "你好"       # 随机样式
fancy --json "你好"         # JSON 输出（供其它程序消费）
fancy --import 包.json      # 导入样式包
fancy --packs               # 列出已安装的样式包（含停用标记）
fancy --export bold morse --out 我的包.json   # 导出样式为可分享的包
fancy --remove 包名         # 卸载样式包
```

## 设计理念：占用少、轻量化

常驻工具，轻量是硬约束而非加分项：

- **引擎**：预览只转换有界文本（前 64 字素）、输入防抖、列表项持久复用——开销与"刷新次数 × 文本长度"解耦；
- **依赖**：桌面版/CLI 零 NuGet 包；扩展仅 CmdPal SDK + WindowsAppSDK 必需项；
- **渲染**：桌面版圆角/阴影/背景 Mica 材质走 Win11 DWM 系统特性（零额外缓冲），曾实测 `AllowsTransparency + DropShadowEffect` 位图特效会把工作集从 ~145MB 推到 ~190MB，已弃用；动效只动 Opacity/RenderTransform（不触发布局、不用位图特效），跟随系统动画总开关；
- 当前实测水位（供回归对照，Win11 26100）：桌面版常驻 **~7-10MB**（隐藏后 EmptyWorkingSet 修剪；首次唤出瞬时 ~220MB 含字体/词典/动画基础设施预热、稳态唤出 ~100MB，用后 1.5s 回落常驻水位）；命令行工具单文件 270KB；扩展进程约 26-56MB（宿主 Microsoft.CmdPal.UI 实测 ~200MB+，不可控）。

## 项目结构

```
src/FancyText.Core/        转换引擎（无 UI 依赖，三端共用）
  TextTransforms.cs        四个正交原语：MapReplace / AppendMark / WrapString+WrapEach / Spacing+Reverse
  ZalgoTransformer.cs      魔鬼文字（上/中/下三组组合符随机叠加，强度可调，可反向清洗）
  LatinMaps.cs             拉丁映射表（数学字母区段+洞字符、带圈/方块、全角、上下标、倒转/镜像、盲文…）
  MartianDictionary.cs     火星文字典加载（嵌入资源，Rune 对齐）
  StyleCatalog.cs(.Expanded)  全部 130 个内置样式（声明式 StyleDefinition 定义）
  StyleFamilies.cs         家族聚族表（kebab 前缀 → 族，供目录钻取）
  ChineseText.cs            简⇄繁（OpenCC 词级贪心最长匹配）与拼音转换；词典 gzip 嵌入、惰性加载（不用不占内存）
  StyleDefinition.cs       样式定义 DTO + StyleFactory（定义 → 可执行 TextStyle，含来源标记）
  TransformStep.cs         声明式步骤层次（查表/引用内置表/附加组合符/包围/分隔/倒序/算法/守卫）
  StyleInterpreter.cs      步骤管道 → 委托（构建期一次编译，运行时纯委托链）
  StylePack.cs             样式包模型、校验、导入/导出/扫描/卸载、内嵌官方包（LoadBundled/InstallBundled）
  StylePackJson.cs         包 JSON 的 op 判别转换器 + 分类/算法 kebab 命名
  StyleEnglish.cs          内置样式的英文层（NameEn/NoteEn 按 ID 补丁）
  Localization.cs          AppLanguage / Loc.S —— 三端共用的中英双语机制
  UsageState.cs            收藏/最近/语言/停用包（%LOCALAPPDATA%\FancyText\state.json，三端共享）
  KnownTransforms.cs       内置命名映射表与算法注册表（UseMap / Algorithm 的引用目标）
  EncodingTransforms.cs    NATO / A1Z26 / 二进制 / 十六进制 / 交替大小写
  Resources/spark-simple.json  cnchar 火星文字典（MIT）
  Resources/opencc-*.txt.gz    OpenCC 简⇄繁词典（Apache-2.0，词级消歧）
  Resources/pinyin.txt.gz      拼音数据（mozillazg/pinyin-data，MIT）
  Resources/bundled/*.json     官方扩展包（内嵌一键安装）
src/FancyText.Desktop/     独立桌面版（WPF：全局热键 + 托盘 + 弹窗转换器，主形态）
src/FancyText.CmdPal/      Command Palette 扩展（WinUI3 / MSIX，维护模式）
src/FancyText.Cli/         命令行工具 fancy（--list/--json/--random/单样式）
tests/FancyText.Core.Tests/  自检测试（255 项断言）+ `-- demo`（效果预览）+ `-- bench`（性能基准）
reference/                 参考项目（ChangeCaseExtension、cnchar 克隆，仅研读，不参与构建）
docs/                      调研报告、独立工具探索、UI 设计稿（ui-mockups/）、品牌资产（brand/）
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
# 运行引擎测试（255 项断言，含样式包全链路与简繁词级消歧）
dotnet run --project tests/FancyText.Core.Tests

# 效果预览（不进 UI，直接打印全部样式的转换结果）
dotnet run --project tests/FancyText.Core.Tests -- demo "你好 Hello"

# 桌面版构建/打包（见「独立桌面版」一节）
tools/package.ps1

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
- 样式包为纯数据格式，无在线包仓库（当前靠文件分享 + 官方内嵌包；网页版包编辑器排期中）；
- 组合符渲染因平台/字体而异（手机与游戏内最佳，PC 部分字体显示方块）——详情页已标注码点；
- 火星文为字典逐字替换，不含语气词与符号装饰的完整「火星文风格」；
- 插件端维护模式（见上节）；独立网页版（单文件 HTML）见 docs/02 的路线建议。

## 许可

- 本项目代码：MIT（见 [LICENSE](LICENSE)）
- 火星文字典数据：来自 [cnchar](https://github.com/theajack/cnchar)（MIT）
- 简⇄繁词典：来自 [OpenCC](https://github.com/BYVoid/OpenCC)（Apache-2.0）
- 拼音数据：来自 [mozillazg/pinyin-data](https://github.com/mozillazg/pinyin-data)（MIT）
