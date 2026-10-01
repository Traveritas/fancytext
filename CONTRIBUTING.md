# 反馈与贡献

这是我一个人业余维护的项目，欢迎提 [Issue](https://github.com/Traveritas/fancytext/issues)：
遇到 bug、某个样式显示成方块、想要新样式或新功能，都可以说。
我一般会在一两周内回复，但不保证每条建议都会采纳，修复也可能排得比较慢。

## 最简单的贡献：写一个样式包

样式是纯数据，一个包就是一个 `.json` 文件，不需要会写代码。照[样式包规范](docs/样式包规范.md)写一个
（可以参考 [docs/sample-pack.json](docs/sample-pack.json)，也可以把规范丢给 AI 让它帮你写），
然后发 Issue 或 PR 给我就行。

## 改代码

小修小补直接发 PR 即可；大改动请先开 Issue 聊一下，免得白忙。

需要 .NET SDK 10（版本见 `global.json`）。

```
dotnet build FancyText.slnx -c Release
dotnet run --project tests/FancyText.Core.Tests
```

提交前请确认测试全部通过（`失败 0 项`）；PR 上的 CI 也会跑同样的检查。

打包发布用 `tools\package.ps1`，产物在 `dist\`。

## 代码约定

- 照着周围代码的风格写：命名、注释密度、写法保持一致。
- 改了样式的 ID 要谨慎：用户的收藏和最近记录是按 ID 存的，改名会让它们失效。
- 新增或调整样式后，测试里有「族成员表无失效 ID」之类的检查，跑一遍就能发现遗漏。

## 提交 PR 之前

- 一个 PR 尽量只做一件事。
- 说明改了什么、为什么改；界面改动请附截图。
- 我可能会提修改意见，或者因为方向不符而婉拒。这不是针对你，项目精力有限，我只能保持它小而稳。
