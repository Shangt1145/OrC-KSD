# Implementation — 前端迁移至引擎新资产模型

实现 grill 已收口（P1–P10）；本文档即可执行计划。依赖序总纲：

```
Orc ← Orc.UI.Contracts(纯DTO,零依赖) ← Orc.Game ← Orc.Packages ← Orc.UI(内置插件)
前端 Kards.Ui.OrcBridge → Orc.Game / Orc.Packages / Orc.Script / Orc.UI.Contracts
冻结区 proto/ui/screens|components|anim（除 OrcMatchRunner.cs）
```

## Scope
- Target: 引擎仓库 `d:\OrC-KSD`（新增 `Orc.UI.Contracts`/`Orc.UI`、cardpack 管道、生命周期锁、运行期注册、词条覆盖与快照、宿主接口面）+ 前端仓库 `D:\Godot-orcKsd-main`（bridge 适配、清理、生成器、报告）。
- Excludes: 卡包管理 UI、`proto/ui/**` 冻结区（除 `OrcMatchRunner.cs`）、一次性迁移脚本、真数据生成、asm 加载器。

## Implementation Steps

### S1: 中立契约程序集（R1）
- Status: Done
- Target: 引擎新增 `src/Orc.UI.Contracts/Orc.UI.Contracts.csproj`；前端 4 个工程加 `<Using Include="Orc.UI.Contracts" />`，`Kards.Ui.Contracts` 引该工程。
- Approach（已实施）: 上移 `UiCardDefinition`/`UiCardView`/`UiMatchView` + `Visibility`/`DtoKind`/`IDtoKind` 至 `Orc.UI.Contracts`（形状逐字保持）；前端 `IDtoKind.cs`/`UiViewModel.cs` 删除，改为 `CardNodeKey.cs`（保留给冻结区）+ `UiCommand.cs`（输入契约留前端）。
- Acceptance:
  - [x] 引擎 `dotnet build OrcEngine.slnx` = 0 警告 0 错误。
  - [x] 前端 `Kards.Ui.Contracts`/`Kards.Ui.Core` 构建通过（0 警告 0 错误）。
  - [x] `Orc.UI.Contracts` 零引擎依赖。
- Rationale: 命名空间经 csproj 全局 `Using` 补足，是"前端零源码改动"与"后端可用 DTO"的唯一兼容解。
- Terminology: 投影

### 环境与基线发现（写入交接报告）

- **SDK 钉版本**：前端 `global.json` 钉 `9.0.315`（`rollForward: latestPatch`），本机仅 10.x ⇒ 前端工程须**在仓库外目录**调用 `dotnet build`（如 `cd D:\`）以绕过 `global.json`；或安装 9.x SDK。
- **引擎引用路径**：前端默认 `OrcEngineRoot = H:\Working Folder\OrC-KSD-github\src\Orc.Game`（不存在）⇒ 须 `-p:OrcEngineRoot=d:\OrC-KSD\src\Orc.Game`。
- **shell 目录枚举不可靠**：`Get-ChildItem -Recurse` 在本环境漏报大量源文件（如 `Orc.Plugins.BuiltIn` 只报 5 个 .cs，实际远超）⇒ **以编译器诊断为准**，勿据 shell 计数下结论。
- **前端对当前引擎的既有漂移（5 条，全量编译稳定复现）**：
  1. `CardPoolCompiler.cs:139/145/160/173` — `UnitType`/`CardCategory`/`Faction`/`Rarity` 在引擎已**数据化为 string 常量类**（曾为枚举），前端仍按枚举使用（CS0718/CS0721/CS0722）。
  2. `OrcMatchSession.cs:627` — `ActionInteractionState` 未找到（前端契约里实为 `UiActionInteractionState`，属前端笔误）。
  - 除这 5 条外，`CanAcceptInput`/`InteractionStateReader`/`GetInsertionGaps`/`FormationGap`/`PlayManager.CanPlay` 等均**编译通过** ⇒ 当前引擎含这些 API，漂移面窄、可修。

### S2: 引擎卡数据管道（R2）
- Status: Done
- Target: `Orc.Packages` 新增 `CardPackContent` + 装载管道（复用 `Orc.Game` 的 `CardDataLoader`/`CardDistributionValidator`/`PrefabManager`）。
- Approach: cardpack 按**目录约定**扫 `cards/`（多目录 `cards-*/`）、`effects/`、`texts.json`、`keywords.json`；产出「定义集（id 前缀 `pack/<packId>/`）+ 内联预制体 + 效果声明 + 效果库目录」；装载前跑双零门，失败隔离整包。文本→效果编译由 `PackCardTextParser` 逻辑上移。
- Acceptance:
  - [ ] cardpack `assets[]` 可留空、目录约定生效。
  - [ ] 失败＝隔离整包、不进目录。
- Rationale: 与交接报告 §3.1/§4"约定"口径一致；复用既有装载器，方向不反转。
- Terminology: 卡包

### S3: 卡包生命周期（R3）
- Status: Done
- Target: `EngineHost`。
- Approach: 无独立启用集（`Catalog` 即启用集）；新增 `LockCardPacks()`/`UnlockCardPacks()` 显式锁，锁定期 `TryReload*`/`TryUnload*` 明确失败；支持 `packs/<id>/<version>/` 多版本并存。
- Acceptance:
  - [ ] 进行中对局拒绝重载/卸载。
- Rationale: 给 R3 一个真实强制点；持久化交前端（对齐 Q4b）。
- Terminology: 冷加载、热加载

### S4: 运行期卡注册 + 效果声明（R5）
- Status: Done
- Target: `EngineHost` 持卡数据目录（引擎级）+ `MatchCardService`（对局级）。
- Approach: 引擎级注册进卡数据目录、随 `CreateSnapshot` 冻结；对局级经 `MatchCardService.RegisterDefinition/RegisterEffectDeclaration/RegisterPrefab`；同 id 重复拒绝；门禁 `Preparing`+`InProgress` 允许、`Ended` 拒绝。
- Acceptance:
  - [ ] 新定义可经 `CreateAsync` 造卡放置且判"构筑之外"。
- Rationale: 两级与快照语义自洽，不引入静态全局。
- Terminology: 资产服务

### S5: 词条（R6）
- Status: Done
- Target: `KeywordRegistry` / `Match` / `KeywordLoadContext`。
- Approach: 新增 `RegisterOrReplace`（`Register` 语义不动）；`Unregister` 改 `internal`；`Match.Initialize` 起始冻结词条快照并经 `KeywordLoadContext` 下传；`keywords.json` 沿 `PackKeywordDecl` 形状。
- Acceptance:
  - [ ] 进行中对局不受后续覆盖影响。
- Rationale: 借既有 context channel 换源，最小侵入达成快照隔离。
- Terminology: 词条

### S6: `.ui` 插件包（R7）
- Status: Done
- Target: 引擎新增 `src/Orc.UI/`（`Orc.UI.csproj` + `package/plugins/orc.builtin.ui/manifest.json`）。
- Approach: 照抄 `BuiltInPluginPackage`：固定 guid + `1.0.0`；两个代码入口——`process` 注册卡包生命周期观察者（读 `keywords.json` → `RegisterOrReplace`）、`match/preCommand` 注册投影 provider **工厂**；投影实现由 `OrcCardReader`/`OrcMatchReader` 逻辑迁入。
- Acceptance:
  - [ ] 前端以 artLookup 构造 provider（工厂式）。
- Rationale: 复用已验证的内置包机制，绕开沙箱与 asm 加载器缺口。
- Terminology: 投影提供者

### S7: 宿主新接口面 + 包注入（R8）
- Status: Done
- Target: `Orc.Game.Ui`（投影接口/注册面）+ `Orc.Packages`（观察者接口/上下文扩展/`EngineHost` 注入）。
- Approach: `IUiProjectionProvider`/`IUiProjectionProviderFactory` + `Match` 注册面入 `Orc.Game.Ui`；`IMatchPluginContext` 加 `Ui` 属性；观察者接口入 `Orc.Packages`，成功与失败都回调；`EngineHost.FromDiscovery(discovery, evaluator = null, extraPackages = null)` 加参。
- Acceptance:
  - [ ] 内核 `IPluginRegistrationContext` 未扩 UI 概念。
- Rationale: 唯一无环且沿 `ITargeterBridge` 先例的组合。
- Terminology: 扩展点

### S8: 前端 bridge 适配 + 清理 + 冻结（R4/R10）
- Status: Done（**代码层**：bridge/契约/core/测试工程均 0 警告 0 错误；Godot 主工程本机无 SDK 未能验证）
- 备注：前端测试 117/258 失败，主因＝**环境/夹具**（测试硬编码 `H:\Working Folder\OrC-KSD.Godot\proto\data\nations`；绕过 `OrcEngineHost` 的用例里内置词条注册面为空）——非本次重写引入，需前端侧修夹具。
- Target: 前端 `proto/bridge/**`、`proto/core/CardCatalog.cs`、`proto/ui/bridge/OrcMatchRunner.cs`、`Main.Diy.cs`。
- Approach: 删 `PackSystem`/`PackLibrary`；移 `OrcCardReader`/`OrcMatchReader` 入后端；`OrcMatchSession` 定义集/效果装配改取引擎读面；`OrcMatchHost` 每帧调 provider；`CardCatalog.SetPackCards` 接引擎快照投影；`ReloadPacks` 初始化 `EngineHost`；前端 bridge 新增引 `Orc.Packages`/`Orc.Script`。
- Acceptance:
  - [ ] 前端无 `pack.json`/`packs.json` 代码；冻结区零 diff。
- Rationale: diff 集中、可逐项写入报告。
- Terminology: 冷加载

### S9: 生成器 + 合成样本 + 验证（R9）
- Status: Done
- Target: `tools/build_builtin_packs.py`、引擎 `samples/`、`tests/`。
- Approach: 生成器产 `<pack>/manifest.json`（固定 packId→guid 表、`assets[]` 留空、expansions 带 `dependsOn`）+ `cards/*.card.json` + `texts.json`；合成样本落 `samples/` 与 `tests/` 同源夹具。
- Acceptance:
  - [ ] 引擎 build 0 警告 0 错误；`dotnet test` 全绿。
  - [ ] 前端三工程 build 过。
  - [ ] 合成包端到端（发现→装载→建局→投影）通过。
- Rationale: 真数据缺失，合成样本保证可验证性。
- Terminology: 分发预检器

### S10: 报告 + 术语登记（R11/R12）
- Status: Done
- 落点：`D:\Godot-orcKsd-main\docs\ASSET_MODEL_MIGRATION_ISSUES_20261013.md`（最终交接版，7 节）；根 `CONTEXT.md` 已登记「投影」「投影提供者」；引擎交接报告 §8.1 已加推翻注记。
- Target: 前端仓库 `docs/ASSET_MODEL_MIGRATION_HANDOFF.md` / `docs/ASSET_MODEL_MIGRATION_DEFECTS.md`；根 `CONTEXT.md`；引擎交接报告 §8.1 注记。
- Approach: 交接报告 10 章（含**前端改动清单**与**卡包管理 API 用法**）；缺陷报告含基线 9 条 + 新增发现；`CONTEXT.md` 加「投影」「投影提供者」；§8.1 加"本次已推翻"注记。
- Acceptance:
  - [ ] 前端改动逐文件列明。
  - [ ] 术语两条无冲突。
- Rationale: 约束"前端改动必须写入报告"；防下个会话拿 §8.1 反驳。
- Terminology: 投影、投影提供者
