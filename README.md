# OrC-KSD

> 卡牌游戏引擎：引擎内核 **Orc** ＋ KARDS 风格游戏层 **Orc.Game**（C# / .NET 8）。
> 许可：**AGPL-3.0-or-later**，另含一项**仅对单个外部仓库生效**的具名附加许可（见[许可](#许可)）。

## 这是什么

本项目是一个**卡牌游戏引擎**，由两层构成：

- **引擎内核 `Orc`**：通用卡牌机制基座——触发器与事件总线、效果、流程、上下文视图、安全引用、判定器、编排与审计链；**不含任何具体卡牌规则**。
- **游戏层 `Orc.Game`**：KARDS 风格的游戏规则实现——卡牌体系与词条、对局与生命周期、指挥点、目标选择（Selector／Targeter）、阵营、效果解析（`EffectParsing/`）等。

引擎以**单线程 Task 模型**驱动；创作者可用 C# 脚本（`Orc.Script`，基于 Roslyn Scripting）承载动态效果。本项目**只提供引擎与示例**，不含任何宿主／客户端实现，也不含官方卡牌数据。

> 本项目是**独立实现**，不是 KARDS 或其发行商的官方产物，也与任何游戏引擎的官方组织无隶属关系（详见[第三方依赖与商标](#第三方依赖与商标)）。

## 工程构成

解决方案 `OrcEngine.slnx` / `OrcEngine.sln`（两者工程集一致）：

| 路径 | 工程 | 说明 |
|---|---|---|
| `src/Orc` | `Orc` | 引擎内核：触发器与事件总线、效果、流程、上下文视图、安全引用、判定器、编排、审计链 |
| `src/Orc.Game` | `Orc.Game` | 游戏层：卡牌体系与词条、对局与生命周期、指挥点、目标选择、阵营、效果解析 |
| `src/Orc.Script` | `Orc.Script` | C# 脚本求值（`Microsoft.CodeAnalysis.CSharp.Scripting`；安全限制自行实现） |
| `src/Orc.Packages` | `Orc.Packages` | 插件与卡包双层模型：引擎宿主、包装配、运行期卡目录、包生命周期 |
| `src/Orc.Plugins.BuiltIn` | `Orc.Plugins.BuiltIn` | 内置插件包（内置判定器、资产服务、词条等内容的注册面） |
| `src/Orc.UI.Contracts` | `Orc.UI.Contracts` | 中立视图契约：投影值对象与相关枚举／标记接口 |
| `src/Orc.UI` | `Orc.UI` | 内置 UI 插件：投影提供者、静态卡定义投影、卡包词条加载 |
| `tests/Orc.Tests` | `Orc.Tests` | 内核单元测试 |
| `tests/Orc.Game.Tests` | `Orc.Game.Tests` | 游戏层单元测试 |
| `tests/Orc.Script.Tests` | `Orc.Script.Tests` | 脚本层单元测试 |
| `tests/Orc.Packages.Tests` | `Orc.Packages.Tests` | 包模型与端到端测试 |
| `samples/Orc.Game.Sample` | `Orc.Game.Sample` | 可运行示例（入口程序 ＋ 目标选择桥接示例） |

全部工程目标框架 `net8.0`、语言版本 C# 12。

## 构建与测试

需要 .NET SDK（能构建 `net8.0`）：

```bash
dotnet build OrcEngine.slnx --no-incremental
dotnet test  OrcEngine.slnx
```

## 运行示例

```bash
dotnet run --project samples/Orc.Game.Sample/Orc.Game.Sample.csproj
```

## 目录结构

```
src/               引擎与游戏层源码（7 个工程）
tests/             xUnit 测试（4 个工程）
samples/           可运行示例
docs/              设计与研究报告（含初始设计基线）
.ams/context/      术语与上下文文档（入口 CONTEXT-MAP.md）
.ams-docs/         需求／实现过程文档
CONTEXT.md         项目术语表
```

## 许可

本仓库**原创代码**以 **GNU Affero 通用公共许可证第 3 版或其后续版本（AGPL-3.0-or-later）** 授权，全文见 [`LICENSE`](LICENSE)。

另有一项**具名附加许可**，全文见 [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md)：

- 它**只**对 `https://github.com/Shangt1145/Godot-orcKsd` 这一个仓库生效（含该仓库自身构建的发行物），使该仓库无须遵守 AGPL 的传递与开源义务；
- 该仓库被 **fork／派生**，或**改名／迁移**后，该附加许可即失效；
- **除该仓库外，本项目的许可就是完整的 AGPL-3.0-or-later**：其他任何 Godot 项目、非 Godot 使用（服务端、命令行、其他引擎）、以及本项目脱离该仓库的独立分发，均不受该附加许可影响。

因含附加许可，本项目的许可**并非纯标准 AGPL**；对外提供本项目的任何一方仍须自行履行 AGPL 的全部义务（含向接收者提供 [`LICENSE`](LICENSE) 与 [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md)）。

## 第三方依赖与商标

- `Microsoft.CodeAnalysis.CSharp.Scripting` —— MIT
- xUnit 系列测试框架 —— Apache-2.0

**商标与归属**：本项目是独立社区实现，与 1939 Games、Godot Foundation 均无隶属、赞助或背书关系。"KARDS" 是 1939 Games 的商标，"Godot" 是 Godot Foundation 的商标，此处仅作指称之用。

**内容边界**：本许可范围仅及于本项目原创代码。本仓库**不包含** KARDS 官方卡牌数据、名称、美术或音效；此类内容的权利归其各自所有者，不受本许可约束，也不因本许可而被授权。

## 反馈

问题与建议请通过本仓库的 Issue 提交。

---

# OrC-KSD (English)

> A card-game engine: the **Orc** engine kernel plus the KARDS-style game layer **Orc.Game** (C# / .NET 8).
> License: **AGPL-3.0-or-later**, plus a **named additional permission that applies to exactly one external repository** (see [License](#license)).

## What this is

This project is a **card-game engine** made of two layers:

- **`Orc` — the engine kernel**: the generic foundation — triggers and an event bus, effects, flows, context views, safe references, resolvers, orchestration and an audit chain. It contains **no concrete card rules**.
- **`Orc.Game` — the game layer**: a KARDS-style rule implementation — the card model and keywords, matches and their lifecycle, command points, target selection (Selector / Targeter), factions, and effect parsing (`EffectParsing/`).

The engine runs on a **single-threaded Task model**. Creators can carry dynamic effects in C# scripts (`Orc.Script`, built on Roslyn Scripting). This project ships **the engine and a sample only** — it contains no host/client implementation and no official card data.

> This project is an **independent implementation**. It is not an official product of KARDS or its publisher, and it is not affiliated with the official organization of any game engine (see [Third-party dependencies and trademarks](#third-party-dependencies-and-trademarks)).

## Projects

Solutions `OrcEngine.slnx` / `OrcEngine.sln` (both contain the same projects):

| Path | Project | Description |
|---|---|---|
| `src/Orc` | `Orc` | Engine kernel: triggers and event bus, effects, flows, context views, safe references, resolvers, orchestration, audit chain |
| `src/Orc.Game` | `Orc.Game` | Game layer: card model and keywords, matches and lifecycle, command points, target selection, factions, effect parsing |
| `src/Orc.Script` | `Orc.Script` | C# script evaluation (`Microsoft.CodeAnalysis.CSharp.Scripting`; safety limits implemented locally) |
| `src/Orc.Packages` | `Orc.Packages` | Plugin and card-pack dual model: engine host, package activation, runtime card catalog, package lifecycle |
| `src/Orc.Plugins.BuiltIn` | `Orc.Plugins.BuiltIn` | Built-in plugin package (registration surface for built-in resolvers, asset services, keywords) |
| `src/Orc.UI.Contracts` | `Orc.UI.Contracts` | Neutral view contracts: projection value objects and related enums / marker interfaces |
| `src/Orc.UI` | `Orc.UI` | Built-in UI plugin: projection provider, static card projections, card-pack keyword loading |
| `tests/Orc.Tests` | `Orc.Tests` | Kernel unit tests |
| `tests/Orc.Game.Tests` | `Orc.Game.Tests` | Game-layer unit tests |
| `tests/Orc.Script.Tests` | `Orc.Script.Tests` | Script-layer unit tests |
| `tests/Orc.Packages.Tests` | `Orc.Packages.Tests` | Package model and end-to-end tests |
| `samples/Orc.Game.Sample` | `Orc.Game.Sample` | Runnable sample (entry program plus a target-selection bridge example) |

All projects target `net8.0` and C# 12.

## Build and test

Requires a .NET SDK able to build `net8.0`:

```bash
dotnet build OrcEngine.slnx --no-incremental
dotnet test  OrcEngine.slnx
```

## Run the sample

```bash
dotnet run --project samples/Orc.Game.Sample/Orc.Game.Sample.csproj
```

## Repository layout

```
src/               Engine and game-layer sources (7 projects)
tests/             xUnit tests (4 projects)
samples/           Runnable sample
docs/              Design and research documents (including the initial design baseline)
.ams/context/      Terminology and context documents (entry point: CONTEXT-MAP.md)
.ams-docs/         Requirements / implementation process documents
CONTEXT.md         Project glossary
```

## License

The **original code** in this repository is licensed under the **GNU Affero General Public License, version 3 or later (AGPL-3.0-or-later)** — see [`LICENSE`](LICENSE).

There is also a **named additional permission**, see [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md):

- It applies **only** to the single repository `https://github.com/Shangt1145/Godot-orcKsd` (including artifacts built by that repository), relieving it of the AGPL's conveying and source-disclosure obligations;
- It ceases to apply if that repository is **forked / derived from**, or **renamed / migrated**;
- **Apart from that repository, this project is licensed under the full AGPL-3.0-or-later**: no other Godot project, no non-Godot use (server, command line, other engines), and no distribution of this project separate from that repository is covered by it.

Because of this additional permission, the licensing of this project is **not plain stock AGPL**; anyone conveying this project must still fulfil all AGPL obligations, including providing recipients with [`LICENSE`](LICENSE) and [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md).

## Third-party dependencies and trademarks

- `Microsoft.CodeAnalysis.CSharp.Scripting` — MIT
- xUnit test framework family — Apache-2.0

**Trademarks and attribution**: this is an independent community implementation. It is not affiliated with, sponsored by, or endorsed by 1939 Games or the Godot Foundation. "KARDS" is a trademark of 1939 Games and "Godot" is a trademark of the Godot Foundation; both are used here for reference only.

**Content boundary**: this license covers the original code of this project only. This repository does **not** contain KARDS official card data, names, artwork or audio; rights to such content belong to their respective owners, are not governed by this license, and are not granted by it.

## Feedback

Please use this repository's issues for questions and suggestions.
