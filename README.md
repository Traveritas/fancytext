# 花式文字 FancyText

把剪贴板或输入的文字一键转换为 **菊花体、魔鬼文字（Zalgo）、花藤体、花体 𝓕𝓪𝓷𝓬𝔂、火星文** 等 **130 种**内置花式 Unicode 样式（含简⇄繁、拼音），回车即复制。

## 特色

- **唤出即用**：全局热键 `Ctrl+Alt+F` 弹窗瞬间出现，选中文字预填在后台异步进行、不阻塞唤出；输入实时预览，`Enter` 复制全文并收起，全程一两秒。
- **纯键盘操作友好**：焦点全程留在输入框，从唤出到复制不需要碰鼠标——`↑↓` 浏览、`Enter` 复制、`Ctrl+D` 收藏、`Ctrl+R` 随机、`Tab` 筛选分类、`→` 钻入同族样式；鼠标同样全程可用。
- **快速、低占用**：托盘常驻，隐藏后修剪工作集，任务管理器观感 **~7–10MB**；零第三方依赖；简繁/拼音等大词典 gzip 嵌入、用到才加载。
- **样式包可扩展**：样式是**纯数据**（声明式 JSON 管道，不含可执行代码），一文件一包；设置页一键安装 4 个官方包，或从在线索引获取社区包；做新包可以直接让 AI 助手按[样式包规范](docs/样式包规范.md)生成。
- **任意机器不出现豆腐块**：内置 GDI 动态字体覆盖库，组合符按码点实时解析到已安装的系统字体，免配置。
- **免安装绿色软件**：单文件自包含 exe（无需 .NET 运行时），解压即用；中英双语界面，明暗主题与强调色跟随系统。

同一引擎另有命令行工具 `fancy`（开发/脚本用途）。

## 下载与运行

GitHub Releases 的 `FancyText.Desktop-x.x.x-win-x64.zip`，解压后双击 `FancyText.Desktop.exe`。或自行构建：

```bash
tools/package.ps1     # 一键打包 zip 到 dist/
# 或
dotnet publish src/FancyText.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 使用

- **唤出**：全局热键 `Ctrl+Alt+F`（设置页可视化换绑；换绑的键被占用时保留旧键）、托盘双击、或再次运行 exe；单实例。启动时热键若被其它程序占用，托盘提示会写明，可双击托盘唤出。
- **弹窗**：Win11 Mica 系统材质，明暗主题自动跟随（可切纯色）；输入实时预览（150ms 防抖，预览只转换前 64 个字素）；窗口出现在鼠标附近（可改主屏居中），点击别处自动隐藏。
- **操作**：`Enter` 复制选中样式的**全文**转换并收起；`Esc` 隐藏；`Ctrl+D` 收藏（⭐）；`Ctrl+R` 随机换一个；`↑↓` 浏览。`Tab` 展开/收起筛选菜单；`Ctrl+Tab`/`Ctrl+Shift+Tab` 循环切换筛选；列表到顶再按 `↑` 或非族行按 `→` 进入按钮区，`←→` 在「全部 ▾」/⭐/🕘 间循环、`Enter` 激活、`↓`/`Esc` 回列表；族条目 `→` 钻入、`←` 返回上一级。
- **家族目录钻取**：翅膀、删除线、菊花体、包围字、边框等 ≥4 个的同族样式折叠成一行族条目，`Enter` 钻入只看该族；收藏/最近视图始终平铺。样式包按包名归族。
- **预填**：
  - 选中文字（默认开）：后台用 UIA 只读读取其它应用中选中的文字，不动剪贴板；主流浏览器/Office/记事本均支持。用户已开始输入则不打断。
  - 兼容模式（默认关）：UIA 不支持的应用改用模拟 `Ctrl+Insert` 复制兜底，读后立即还原剪贴板。只备份还原纯文本格式，可能让刚复制的内容进入 Win+V 剪贴板历史。
  - 剪贴板文字（**默认关**）：打开后唤出时预填剪贴板内容——注意刚复制的密码等会明文显示在弹窗里。
- **设置**（托盘右键 → 设置，全部即时生效）：主题、背景材质、强调色（含跟随系统）、预览字号、减少动效、语言、唤出快捷键、开机自启动、上述预填选项、复制后收起、弹窗位置、样式包管理。
- **诊断**：设环境变量 `FANCYTEXT_DIAG=1` 后启动，预填失败原因等写入 `%LOCALAPPDATA%\FancyText\diag.log`。

配置与数据都在 `%LOCALAPPDATA%\FancyText\`：`desktop.json` 设置、`state.json` 收藏/最近/语言/停用包、`styles\` 样式包。

## 样式包

样式在本引擎里是**纯数据**（声明式步骤管道），一组样式存成一个 `.json` 包文件：

- **官方包**：exe 内嵌 4 个——华丽装饰（32）、叠加特效（28）、颜文字（22）、星月夜（10），设置 →「样式包」一键安装，内嵌版本更新后提示 [更新]。
- **在线获取**：设置 → 样式包 → 获取更多样式包，一键安装 [fancytext-styles 索引仓库](https://github.com/Traveritas/fancytext-styles) 里的包（raw 主通道 + jsDelivr 镜像回退）；社区可向该仓库 PR 上架新包。
- **导入**：设置 →「导入样式包…」；或把 `.json` 放进 `%LOCALAPPDATA%\FancyText\styles\`（重启程序后生效，没有目录监听）；命令行 `fancy --import 包.json`。
- **管理**：每包可停用/启用（不删文件）、卸载（损坏的包标红且只能卸载）、展开查看包内样式与当前文本的实时预览。
- **校验**：只拦会导致加载失败的内容（格式错误、ID 与内置或其他包冲突、孤立代理对/控制字符、超限参数）；覆盖已装的同名包、输出膨胀过大等情况照常安装并给出提示。坏包只影响自己，不会拖垮程序。

包格式、全部步骤写法与官方收录标准见 [docs/样式包规范.md](docs/样式包规范.md)，可直接导入体验的示例见 [docs/sample-pack.json](docs/sample-pack.json)。

### 内置样式（130 个）

| 分类 | 数量 | 代表样式 |
|---|---|---|
| 特效（组合符，中英通吃） | 46 | 菊花体×4、删除线/下划线/上划线家族、圈圈/包围框/菱形/禁止字、飞鸟/蝴蝶/尾巴/冒烟×3/萌芽/爱心文、花藤字×2、闪电/连弧/横条组合符、魔鬼文字（Zalgo）×3 |
| 英文/字母花体（Unicode 区段映射） | 28 | 粗体/斜体/花体/哥特/空心/等宽/无衬线系、泡泡字、黑底圈字、方块字、括号字、旗帜字母、全角、小型大写、上下标、货币体、符号体、无衬线圈/双圈数字、俄化 |
| 装饰（前后缀/逐字/分字） | 34 | 经典/华丽/精美/皇冠/典雅/钻石/双层/流光/柔光/神秘/藏文系翅膀、星光/花/闪耀边框、日式括号、分字空格、宽体 |
| 变换 | 8 | 倒转 oʇʇǝ、镜像、倒序、Leet、全大写/全小写/交替大小写、还原（去装饰） |
| 中文 | 6 | 火星文（2088 字字典）、火星文还原、简→繁 / 繁→简（OpenCC 词级消歧：头发→頭髮）、拼音（带调 nǐ hǎo）、拼音缩写（nhm） |
| 编码 | 8 | Base64、ROT13、摩斯电码、盲文 ⠓⠑⠇⠇⠕、NATO、A1Z26、二进制、十六进制 |

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
fancy --remove 包名         # 卸载样式包
fancy --online              # 列出在线索引；--online-install <id> 安装
fancy --help / --version
fancy bold -- --text        # -- 之后全部当文本
```

未知选项返回退出码 2；`--json` / `--random` 后的位置参数全部当作文本。

## 设计理念：占用少、轻量化

常驻工具，轻量是硬约束而非加分项：

- **引擎**：预览只转换有界文本（前 64 字素）、输入防抖——开销与「刷新次数 × 文本长度」解耦；
- **依赖**：桌面版/CLI 零 NuGet 包；
- **渲染**：圆角/背景 Mica 走 Win11 DWM 系统特性（零额外缓冲），曾实测 `AllowsTransparency + DropShadowEffect` 位图特效会把工作集从 ~145MB 推到 ~190MB，已弃用；动效只动 Opacity/RenderTransform，跟随系统动画总开关；
- **实测水位**（Win11 26100，.NET 10，2026-10 复测，供回归对照）：

  | 状态 | 工作集（任务管理器「内存」） | 提交内存（私有） |
  |---|---|---|
  | 启动后从未唤出（尚未修剪） | ~130–145MB | ~70–80MB |
  | 首次唤出（字体/词典/动画预热） | ~210MB | ~130MB |
  | 之后每次唤出 | ~60–110MB | ~145–155MB |
  | 隐藏 1.5s 后（修剪工作集） | **~3–12MB** | ~145–155MB |

  连续唤出/隐藏 15 轮，提交内存在第 3 轮后稳定在 145–155MB，无持续增长；.NET 8 同流程测得数值基本一致。修剪的是工作集，提交内存不会因此下降。命令行工具单文件约 1MB（含内嵌词典，需本机 .NET 10 运行时）。

引擎基准（`dotnet run --project tests/FancyText.Core.Tests -- bench`）：全目录（内置 + 已装官方包，约 230 个样式）× 64 字输入，约 1–2ms/次。

## 项目结构

```
src/FancyText.Core/        转换引擎（无 UI 依赖）
  TextTransforms.cs        四个正交原语：MapReplace / AppendMark / WrapString+WrapEach / Spacing+Reverse
  ZalgoTransformer.cs      魔鬼文字（上/中/下三组组合符随机叠加，强度可调）
  LatinMaps.cs             拉丁映射表（数学字母区段+洞字符、带圈/方块、全角、上下标、倒转/镜像、盲文…）
  MartianDictionary.cs     火星文字典加载（嵌入资源）
  StyleCatalog.cs(.Expanded)  全部 130 个内置样式（声明式 StyleDefinition 定义）+ 包样式合并目录
  StyleFamilies.cs         家族聚族表（kebab 前缀 → 族，供目录钻取）
  ChineseText.cs           简⇄繁（OpenCC 词级贪心最长匹配）与拼音；词典 gzip 嵌入、惰性加载
  StyleDefinition.cs       样式定义 + StyleFactory（定义 → 可执行 TextStyle）
  TransformStep.cs         声明式步骤（查表/引用内置表/附加组合符/包围/分隔/倒序/算法/守卫）
  StyleInterpreter.cs      步骤管道 → 委托链；外部包样式加输出上限与异常兜底
  StylePack.cs             样式包模型、校验、导入/扫描/卸载、内嵌官方包
  PackGrowth.cs            样式包输出膨胀估算（安装时警告）
  StylePackJson.cs         包 JSON 的 op 判别转换器 + 分类/算法 kebab 命名
  StyleRegistry.cs         在线索引拉取与下载
  StyleEnglish.cs          内置样式的英文名/说明
  Localization.cs          中英双语机制
  UsageState.cs            收藏/最近/语言/停用包（state.json）
  KnownTransforms.cs       内置命名映射表与算法注册表
  EncodingTransforms.cs    NATO / A1Z26 / 二进制 / 十六进制 / 交替大小写
  Resources/               火星文字典（cnchar, MIT）、OpenCC 简繁词典（Apache-2.0）、拼音数据（MIT）、bundled/ 官方包
src/FancyText.Desktop/     桌面版（WPF：全局热键 + 托盘 + 弹窗转换器）
src/FancyText.Cli/         命令行工具 fancy
src/FancyText.FontProbe/   字体覆盖探测小工具（开发用，不在解决方案内）
tests/FancyText.Core.Tests/  引擎自检测试 + demo / bench / audit 子命令
tools/                     打包、图标生成与各类 UI 验证脚本
docs/                      样式包规范、示例包、UI 设计稿、品牌资产；archive/ 为早期调研存档
```

## 构建与测试

要求：.NET SDK 10（`global.json` 锁定 10.0.x，目标框架 net10.0）；Windows 10 19041+（建议 Win11）。桌面版自包含发布，用户无需安装 .NET；CLI 为框架依赖发布，需要 .NET 10 运行时。

```bash
# 引擎测试（336 项断言，含样式包全链路与简繁词级消歧；失败退出码 1）
dotnet run --project tests/FancyText.Core.Tests

# 效果预览（不进 UI，直接打印全部样式的转换结果）
dotnet run --project tests/FancyText.Core.Tests -- demo "你好 Hello"

# 样式包重复审计（与内置样式输出重复则返回 1）
dotnet run --project tests/FancyText.Core.Tests -- audit 我的包.json

# 桌面版打包
tools/package.ps1
```

版本号统一在 `Directory.Build.props`。

## 关于 Command Palette 插件

项目最早是 PowerToys Command Palette 插件，后来桌面版成为唯一产品形态，插件已停止维护并从仓库移除（代码保留在 git 历史中）：SDK 的列表页模型承载不了桌面版的交互与渲染，宿主进程常驻 ~200MB+ 也不在本项目可控范围。

## 已知限制

- 魔鬼文字是随机的，复制形态与预览不完全一致；
- 组合符渲染因平台/字体而异（手机与游戏内最佳，PC 部分字体显示方块）；
- 火星文为字典逐字替换，还原为尽力而为（部分火星字形对应多个原字）；
- 拼音按单字取音，无词级多音字消歧（如「重庆」）。

## 许可

- **本项目代码**：MIT，见 [LICENSE](LICENSE)（版权人 Traveritas）
- **第三方组件与内嵌词典数据**：完整清单、版本与许可全文见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)——含 .NET 运行时（MIT）、[cnchar](https://github.com/theajack/cnchar) 火星文字典（MIT）、[OpenCC](https://github.com/BYVoid/OpenCC) 简⇄繁词表（Apache-2.0）、[mozillazg/pinyin-data](https://github.com/mozillazg/pinyin-data) 拼音数据（MIT；其中源自 Unihan 的部分适用 Unicode 许可）
