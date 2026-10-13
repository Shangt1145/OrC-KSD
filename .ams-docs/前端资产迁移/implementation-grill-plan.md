# Implementation Grill Plan — 前端迁移至引擎新资产模型

> 依据 `requirements.md`（R1–R12）逐条展开实现路径；按逻辑序分步、每轮一问。

## 步骤总览（逻辑序）

- **S1** 中立契约程序集（R1）
- **S2** 引擎卡数据管道（cardpack→定义+效果装配+快照读面）（R2）
- **S3** 卡包生命周期归 `EngineHost`（R3）
- **S4** 运行期卡注册 + 运行期效果声明（R5）
- **S5** 词条：json 承载 + 覆盖语义 + `Match` 快照 + 封存注销（R6）
- **S6** `.ui` 插件包（投影 provider + 词条加载）（R7）
- **S7** 宿主新接口面 + 包注入（R8）
- **S8** 前端 bridge 适配 + 清理 + 冻结（R4/R10）
- **S9** 生成器改造 + 合成样本 + 验证（R9）
- **S10** 报告 + 术语登记（R11/R12）

## Unresolved Questions

### P1: 中立契约程序集的组织、命名与"零改动"策略 Completed
- 定案：(b) 拆 `Orc.UI.Contracts`（DTO，namespace `Orc.UI.Contracts`，零引擎依赖）+ `Orc.UI`（插件，引引擎）；前端三工程加 `<Using Include="Orc.UI.Contracts" />` 全局导入 ⇒ `proto/ui/**` 源码零改动。
- 子答：P1a **仅上移 view 三件**（`UiCardDefinition`/`UiCardView`/`UiMatchView` + `Visibility`/`DtoKind`）；P1b **保留**前端 `Kards.Ui.Contracts` 工程；P1c `UiBattleActions` **留前端**（`OrcActionReader` 不动）；P1d `UiCommand` **留前端**。

### P2: 引擎卡数据管道的声明形态与落点 Completed
- 定案：(a) **目录约定**（引擎扫包内 `cards/`、`effects/`、`keywords.json`；`assets[]` 留给显式资产）。
- 子答：P2a 新增 `Orc.Packages` 侧管道类型、复用 `Orc.Game` 既有装载器；P2b **卡 id 保持 `pack/<packId>/<cardId>`**；P2c 引擎跑 `CardDistributionValidator` 双零门、失败隔离整包；P2d 沿用 `PackContent` 形状（定义集+内联预制体+声明+效果库目录）；P2e 支持包内多卡目录。

### P3: 卡包生命周期 API 与"启用集"模型 Completed
- 定案：(a) **无独立启用集**——`EngineHost.Catalog` 即启用集；启/停＝`TryLoadCardPack`/`TryUnloadCardPack`；持久化交前端/宿主。
- 子答（按推荐）：P3a `packs/<id>/<version>/`；P3b **引擎显式锁**（`LockCardPacks`/`UnlockCardPacks`，锁定期 `TryReload*`/`TryUnload*` 明确失败）；P3c 保留按路径 `TryReloadCardPack`（可选加 (id,version) 便捷重载）；P3d 保留 `RestoreFrom`。

### P4: 运行期卡注册的承载与两级语义 Completed
- 定案：(a) **两级容器**——引擎级＝`EngineHost` 持卡数据目录（随快照冻结进对局）＋对局级＝本局追加受控面；同 id 重复（含跨级）＝拒绝。
- 子答（按推荐）：P4a 调用方给 id、重复拒；P4b "新 ID"＝既有水位线机制（构筑之外）；P4c 先声明后实例化；P4d `Preparing`+`InProgress` 允许、`Ended` 拒；P4e 扩 `MatchCardService`。

### P5: 词条快照承载与"覆盖/封存注销"落地 Completed
- 定案：(a) `Match.Initialize` 冻结 `KeywordRegistry` 快照，经既有 `KeywordLoadContext` 下传；定义期校验仍读静态面。
- 子答（按推荐）：P5a 新增 `RegisterOrReplace`（`Register` 语义不动）；P5b `Unregister` 改 `internal`；P5c 沿 `PackKeywordDecl` 形状；P5d `Initialize` 开始冻结；P5e 只影响之后新建对局与之后定义校验。

### P6: `.ui` 插件包的构成（相/入口/身份/art 注入） Completed
- 定案：(a) 一个包两个代码入口——`process`（注册卡包生命周期观察者）+ `match/preCommand`（注册投影 provider）。
- 子答（按推荐）：P6a `orc.builtin.ui`/固定 guid/1.0.0；P6b 观察者驱动、unload 不注销；P6c match/preCommand；P6d 单个；P6e 回调带 `LoadedPackage`；P6f **(i) 工厂式**（插件注册 provider 工厂，前端以 artLookup 构造实例并自持）。

### P7: 投影契约与注册面的归属层 / 依赖方向 Completed
- 定案：(a) `Orc.UI.Contracts`（纯 DTO，零依赖）置于 `Orc.Game` 之下；依赖序 `Orc ← Orc.UI.Contracts ← Orc.Game ← Orc.Packages ← Orc.UI`，无环。
- 子答（按推荐）：P7a 投影接口入 `Orc.Game.Ui`、观察者接口入 `Orc.Packages`；P7b `IMatchPluginContext` 加 `Ui` 属性；P7c `FromDiscovery(..., extraPackages)` 加参；P7d 前端从 `Match` 读注册面、以 artLookup 造 provider。

### P8: 前端 diff 面（删/移/改/留） Completed
- 定案：(a) 严格最小 diff。
- 子答（按推荐）：P8a `DiyCardCompiler` 留前端；P8b `CardCatalog.SetPackCards` 改吃引擎快照投影（`pack/` 前缀不变）；P8c `ReloadPacks`→初始化 `EngineHost`；P8d 前端 `PackSystemTests`/`BuiltinPackTests` 删除、等价验证迁引擎侧；P8e `SourceEffectAssembly`/`NativeScriptHost` 保留；P8f 前端 bridge 新增引 `Orc.Packages`(+`Orc.Script`)。

### P9: 生成器产物 / 文本承载 / 合成样本与验证 Completed
- 定案：P9a (i) `texts.json` sidecar 约定；P9b 生成器固定 packId→guid 表；P9c `assets[]` 留空（纯目录约定）；P9d 合成样本落 `samples/` + `tests/` 同源夹具；P9e 验证五项（引擎 build/test、前端 build、合成包端到端、冻结区零 diff）。

### P10: 报告结构与术语登记 In Progress
- 落点：前端仓库 `docs/`（两份）；术语入根 `CONTEXT.md`（OrC 工作区）。
- 子问：P10a 文件名/落点；P10b 交接报告章节（含"卡包管理 API 用法"章）；P10c 术语登记位置（根 `CONTEXT.md`）；P10d 是否在引擎侧交接报告加"推翻 §8.1"注记。

