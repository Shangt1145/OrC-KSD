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
| `src/Orc.Mcp` | `Orc.Mcp` | 面向 MCP 的协议无关服务层（工具目录、对局执行器、沙盒模式、资产管线、调试与文档读取） |
| `tests/Orc.Mcp.Tests` | `Orc.Mcp.Tests` | 服务层契约断言与端到端闭环测试 |
| `samples/Orc.Mcp.Stdio` | `Orc.Mcp.Stdio` | MCP stdio 适配示例（手写最小 JSON-RPC；零第三方依赖） |
| `samples/Orc.Mcp.HostSample` | `Orc.Mcp.HostSample` | UI 宿主集成参考宿主（人机同局：投影/选靶仲裁/段消费/强类型动作面） |

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

## 通过 MCP 使用引擎（卡牌编辑者 / 沙盒模式）

```bash
dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring --sandbox ./.orc-sandbox
```

一条命令即可启动 stdio MCP 服务：外部 agent 可建沙盒对局、写卡（原文 JSON + 校验）、逐卡注册进当前对局、构造验证场景、读取观察面与事件流段、读引擎文档，全过程**不需要阅读源码**。

- `src/Orc.Mcp` 是**协议无关**服务层（零 MCP SDK 依赖）；`samples/Orc.Mcp.Stdio` 是薄 stdio 适配示例（同样零第三方依赖）。
- 门控：沙盒专属工具只在 `--mode authoring` 下存在；写入只允许落在 `--sandbox` 根内；产物提升（`--promote`）与 csx 编辑（`--csx`）默认关闭。
- 服务层限制与工具清单可用 `service.info` / `service.tools` 查询；创作文档经 `docs.list` / `docs.read` 读取。
- 客户端接入片段见 [`samples/Orc.Mcp.Stdio/README.md`](samples/Orc.Mcp.Stdio/README.md)。

### 在真实 UI 里用人机同局

```bash
dotnet run --project samples/Orc.Mcp.HostSample
```

参考宿主演示 UI 侧接入的全部接线（服务层是唯一驱动真源：唯一桥、唯一门禁、唯一段取用点）：投影 provider 自建、待决选靶请求订阅与认领、段消费与喂回、强类型动作面。接入步骤与常见坑见 [`docs/mcp-ui-integration.md`](docs/mcp-ui-integration.md)。

## 目录结构

```
src/               引擎与游戏层源码（8 个工程）
tests/             xUnit 测试（5 个工程）
samples/           可运行示例（含 MCP stdio 适配示例）
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
| `src/Orc.Mcp` | `Orc.Mcp` | Protocol-agnostic MCP-facing service layer (tool catalog, match executor, sandbox mode, asset pipeline, diagnostics and docs reading) |
| `tests/Orc.Mcp.Tests` | `Orc.Mcp.Tests` | Service-layer contract assertions and an end-to-end loop test |
| `samples/Orc.Mcp.Stdio` | `Orc.Mcp.Stdio` | MCP stdio adapter sample (hand-written minimal JSON-RPC; no third-party dependencies) |
| `samples/Orc.Mcp.HostSample` | `Orc.Mcp.HostSample` | UI host-integration reference host (human + agent in one match: projection, targeting arbitration, segment consumption, strongly-typed actions) |

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

## Using the engine through MCP (card-authoring / sandbox mode)

```bash
dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring --sandbox ./.orc-sandbox
```

One command starts a stdio MCP server: an external agent can create a sandbox match, write cards (raw JSON plus validation), register them card by card into the running match, shape verification scenarios, read the observation surface and event segments, and read the engine docs — all without reading engine source.

- `src/Orc.Mcp` is a **protocol-agnostic** service layer (zero MCP SDK dependencies); `samples/Orc.Mcp.Stdio` is a thin stdio adapter sample (also dependency-free).
- Gating: sandbox-only tools exist only under `--mode authoring`; writes are confined to the `--sandbox` root; promotion (`--promote`) and csx editing (`--csx`) are off by default.
- Query `service.info` / `service.tools` for limitations and the tool list; read authoring docs via `docs.list` / `docs.read`.
- Client configuration snippet: see [`samples/Orc.Mcp.Stdio/README.md`](samples/Orc.Mcp.Stdio/README.md).

## Repository layout

```
src/               Engine and game-layer sources (8 projects)
tests/             xUnit tests (5 projects)
samples/           Runnable samples (including the MCP stdio adapter)
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
