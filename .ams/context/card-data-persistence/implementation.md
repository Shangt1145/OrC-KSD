# Implementation — 卡牌数据体持久化与加载

实现 grill 已收束（P1–P12 全部定案），本文件即可执行计划。验收总口径：**构建 0 警告 ＋ 三个测试项目全绿**；允许触动 `src/Orc`。

## Scope

- Target: `src/Orc`（预制体 schema）、`src/Orc.Game`（数据体/组件 loader/加载器/部署效果）、`scripts`（官方导入）、`tests`（迁移与新增）。
- Excludes: 数据导出方向、热重载、展示/本地化字段、对局存档。

## 关键定案（实现时必须遵守）

- 数据体＝**元数据 ＋ `components` 组件定义数组**；相同项对齐官方语义；`type` 为列表；`set` 不入体。
- 组件＝**定义类型（实现 `ICardDataComponentDefinition`）＋ loader**；**静态注册面**注册序＝第一段加载顺序；**两段式**（第一段游戏层注册序、第二段数据体声明序）。
- `CardDefinition` 扩展为"元数据 + 组件定义集"（唯一真源），既有 12 参数构造与读面保留。
- 装载流程：构造期（`typeCategory` 解析选类 → `factionCost`/`battleStats` loader）＋ 加载期（ID 分配 → 第一段 → 第二段 → 广播 `card.load`）；**删除 `RebuildFromPersistence`**。
- 效果：`EffectPrefab.injects`（`{ target, band, event, priority }`）→ 框架侧注入；`EffectSnapshot.SchemaVersion` **1→2**。
- 部署效果：**废弃 A4**；`UnitCard` 新增"部署词条触发器"（默认 band）；重放＝`RetriggerSystem` 直接再调该触发器。
- 内联效果：**先分配 ID → 注册进 `PrefabManager` → 按 ID 引用**（装载链单一路径）。
- 词条：官方英文标识经**代码常量映射表**转 `KeywordIds`；参值后缀显式表拆解；未实现项进**只读集合**（不写日志、不 fail-fast）。
- 运行时组装路径（`DynamicEffectFactory.Instantiate`/`AddEffect`/`MountPassiveEffects`）**不改动**。

## Implementation Steps

### S1: 数据体格式与 JSON 读写
- Status: Todo
- Target: 新增 `src/Orc.Game/Cards/Data/`（`CardDataBody.cs`、`CardDataJson.cs`）
- Approach:
  - `CardDataBody`：`Id`/`Name`/`Components`（`IReadOnlyList<ICardDataComponentDefinition>`，声明序）；id/name 必填非空白。
  - JSON 形态：`{ "schemaVersion": 1, "id": "...", "name": "...", "components": [ { "component": "factionCost", ... } ] }`。
  - `CardDataJson.Deserialize(string)`（fail-fast）/ `TryDeserialize(string, out body, out error)`（结构化，不抛）；`schemaVersion != 1` ＝ `FormatException`（不静默降级）；未知 `component` 名＝单卡内隔离记录（不阻断其余组件，记入返回的告警集）。
  - UTF-8 明文；camelCase；忽略 null。
- Acceptance:
  - [ ] 合法 JSON 读回 `CardDataBody`；组件声明序保留。
  - [ ] `schemaVersion` 不符＝结构化失败；未知 `component`＝隔离记录且其余组件照常。
- Rationale: 对齐 `PrefabJson`（`src/Orc/Cards/PrefabJson.cs`）既有口径。
- Terminology: 卡牌数据体

### S2: 组件定义接口 + loader 注册面 + 加载上下文
- Status: Todo
- Target: 新增 `src/Orc.Game/Cards/Data/`（`ICardDataComponentDefinition.cs`、`CardComponentRegistry.cs`、`CardComponentLoadContext.cs`）
- Approach:
  - `interface ICardDataComponentDefinition { static abstract string ComponentName { get; } static abstract ICardDataComponentDefinition Read(JsonElement e); }`。
  - `enum CardComponentPhase { Construction, Load }`；`delegate void CardComponentLoader(CardBase card, ICardDataComponentDefinition def, CardComponentLoadContext ctx)`。
  - `CardComponentRegistry`（静态、加锁，对齐 `KeywordRegistry` 风格）：`Register<TDef>(CardComponentPhase phase, CardComponentLoader loader)`（类型名取 `TDef.ComponentName`，注册序＝第一段顺序；重复名＝拒绝）；`TryResolve(name, out loader, out phase)`；`ResolveByName(name)`；`Registered` 读面。
  - `CardComponentLoadContext`：`Engine`（`LogicEngine`）＋延迟访问 `KeywordLoadContext? Keywords`、`CardEffectLoadContext? Effects`（可为 null＝独立构造）。
- Acceptance:
  - [ ] 重复注册同名＝拒绝；未注册名＝解析失败（调用侧隔离）。
  - [ ] 上下文不携带数据体/owner。
- Terminology: 组件 loader、"可反序列化组件"接口

### S3: 六个内置组件定义与 loader
- Status: Todo
- Target: 新增 `src/Orc.Game/Cards/Data/Components/`（`FactionCostComponent.cs`、`BattleStatsComponent.cs`、`TagDataComponent.cs`、`TypeCategoryComponent.cs`、`KeywordsComponent.cs`、`EffectsComponent.cs`）
- Approach:
  - `factionCost`（Construction）：`faction`（字面值，`Enum.IsDefined` 校验）、`kredits`（部署费）；loader → `AddData(new FactionCostData(faction, kredits))`。
  - `battleStats`（Construction）：`operationCost`/`attack`/`defense`；loader → `AddData(new BattleStatsData(...))`；**仅单位卡**（非单位卡出现＝隔离记录）。
  - `tagData`（Load）：`rarity`（字面值校验）＋`tags`（开放 tag，可选）；loader → `new TagData(rarity)` ＋ `AddTag` 逐项。
  - `typeCategory`（Construction）：`type` 列表；loader＝**空操作**（数据经 `CardDefinition` 派生读面消费）。拆解规则：类别词 `order`/`countermeasure` 至多一个且列表长度须为 1；单位类型词可多个去重；空列表＝拒绝；未定义词＝单卡隔离。
  - `keywords`（Load）：`attributes`（官方英文标识数组）；loader → 经**映射表**（S3 内新增 `CardAttributeMap`）转为 `KeywordDeclaration`，逐条 `card.Keywords.GrantCore(...)`；未映射项 → `CardDefinition.UnmappedAttributes`（只读集合，不写日志）。
  - `effects`（Load）：`prefabs`（id 引用数组，可选内联见 S8）；loader → 经 `CardEffectLoadContext` 装载（沿用 `CardEffectLoader` 既有链）。
- Acceptance:
  - [ ] 六项 loader 覆盖官方字段与既有组件构造；`kredits`→部署费、`operationCost`→行动费映射正确。
  - [ ] `type` 四条拆解规则（P10）有测试覆盖；`guard` 等未实现项进只读集合。
- Terminology: 卡牌标签（TagData）、词条

### S4: 卡牌加载器两段式改造
- Status: Todo
- Target: `src/Orc.Game/Cards/CardBase.cs`、`CardLibrary.cs`（`CardDefinition` 见 S1 扩展）
- Approach:
  - `CardDefinition`：内部新增 `Components`（组件定义集，声明序）；既有 12 参数构造 → 转换为组件定义集；既有读面（`Faction`/`DeployCost`/`OperateCost`/`Attack`/`Defense`/`Rarity`/`Tags`/`Keywords`/`UnitTypes`/`Category`/`Name`）保留同名同义、从组件定义派生；新增 `UnmappedAttributes` 只读面。
  - `CardLibrary.Instantiate`：按 `typeCategory` 解析出的类别选卡类；注入 `CardComponentLoadContext` 的提供服务（替代原 `deploymentLogicLoadContextProvider`）。
  - `CardBase` 构造期：调 `Construction` 相位 loader（`factionCost`/`battleStats`）。
  - `CardBase.LoadAsync`：`Owner` 装配 → ID 分配 → **第一段**（`Load` 相位 loader，按注册序）→ **第二段**（`Components` 中未被第一段消费的定义，按声明序）→ 广播 `card.load`；**删除** `RebuildFromPersistence` 与 `GenerateDeploymentLogic`。
  - 失败口径：第一段内置组件＝fail-fast；第二段自定义组件＝隔离记录。
- Acceptance:
  - [ ] 既有"先于 `card.load` 广播"契约保持；`MountPassiveEffects` 兜底仍在链末。
  - [ ] 既有代码注册路径与全部既有测试行为不变（`CardDefinition` 12 参数构造零改动）。
- Terminology: 组件 loader

### S5: 效果预制体 `injects` + 框架侧注入 + 版本升版
- Status: Todo
- Target: `src/Orc/Cards/Prefabs.cs`、`PrefabJson.cs`
- Approach:
  - 新增 `ModingPrefab` 同级的 `InjectPrefab(string TargetTriggerName, string? BandName, string EventId, int Priority = 0)`；`EffectPrefab` 新增 `Injects`（声明序）。
  - `PrefabJson`：DTO/读写新增 `injects`；`EffectSnapshot.SchemaVersion` 1 → 2（旧版＝`FormatException`）。
  - 装载链（游戏层）：`DynamicEffectFactory` 实例化时，对 `Injects` 逐项按 `target` 名查**触发器注册表**（查不到＝隔离记录）、按 `band` 名解析枚举（解析不到＝隔离记录），命中后经效果装载链注入（`Effect.Inject` 等价路径，卸载自动撤销）。
- Acceptance:
  - [ ] `injects` 可往返读写；版本不符＝失败。
  - [ ] 注入在装载时建立、卸载时撤销；查不到 target/band＝隔离记录不阻断。
- Terminology: 注入（Inject）

### S6: 部署词条触发器 + 部署链触发 + 重放改造
- Status: Todo
- Target: `src/Orc.Game/Cards/UnitCard.cs`、`RetriggerSystem.cs`
- Approach:
  - `UnitCard` 构造期新增 `DeployKeywordTrigger = new Trigger<CardTriggerView>("部署词条触发器")`（默认 band），公开只读属性。
  - `HandleDeployChainAsync` ①步改为 `await unit.DeployKeywordTrigger.InvokeAsync(_chainEngine, BuildUnitData(unit, view.Player as Player, slot), ct)`（替代 `DeploymentLogicRules.RunEffectSegmentAsync`）。
  - `RetriggerSystem.ExecuteDeployReplayAsync`：执行体改为调用同一触发器（保留重入防护 `_deployExecuting`、终局门禁与入口点）。
  - 部署效果（数据体/效果侧）以 `injects: [{ target: "部署词条触发器", event: ... }]` 接入。
- Acceptance:
  - [ ] 部署链在单位化前触发部署词条效果；重放＝再调同一触发器、不发射 `unit.deployed`。
  - [ ] 多效果按序、单条异常隔离、无效果空转（P8b 口径）。
- Terminology: 主动触发器、注入（Inject）

### S7: A4 路径删除与测试迁移
- Status: Todo
- Target: 删除 `DeploymentLogicData.cs`；改 `CardBase.cs`/`CardLibrary.cs`/`Match.cs`/`UnitCard.cs`/`RetriggerSystem.cs`；迁移测试
- Approach:
  - 删除 A4 全部类型与引用（`DeploymentLogicEntry`/`Data`/`Context`/`Rules`/`Registry`/`LoadContext`、`CardBase.DeploymentLogicLoadContextProvider`、`CardLibrary.deploymentLogicLoadContextProvider`、`Match._deploymentLogicRegistry` 与 lambda、部署链①段）。
  - 迁移 `tests/Orc.Game.Tests/DeathrattleAndRetriggerTests.cs`（`:282`/`:333`/`:360`/`:520`）与 `PlayChainDeploymentTests.cs`（`:28`/`:72`/`:94`/`:123`）到"部署效果 + inject"路径；移除 `CommandTestInfrastructure.CreateCommandMatch` 的 `deploymentLogicRegistry` 参数；同步更新日志 `Source` 断言。
- Acceptance:
  - [ ] 全库 `DeploymentLogic`/`部署逻辑` 零残留（除历史文档）。
  - [ ] 迁移后的测试覆盖同等语义并全绿。
- Terminology: 部署逻辑（术语退场）

### S8: 数据体载入器（目录扫描 + 报告 + 隔离）
- Status: Todo
- Target: 新增 `src/Orc.Game/Cards/Data/CardDataLoader.cs`（对齐 `PrefabManager.LoadDirectory`/`PrefabLoadReport`）
- Approach:
  - `LoadDirectory(string dir)`：扫顶层 `*.card.json`；逐文件 `CardDataJson.TryDeserialize`；单文件失败＝隔离记录、不阻断；返回含成功集（`CardDataBody` 列表）与失败集（文件、错误）的报告。
  - **内联效果**：读取时把内联 `EffectSnapshot` 分配 ID（显式 id 冲突＝拒绝；无 id＝生成 `<cardId>#<序号>`）并注册进 `PrefabManager`；卡的效果声明统一按 ID 引用。
  - 产出"卡定义集（`CardDefinitionEntry`）＋ 预制体注册"，供宿主传给 `Match`。
- Acceptance:
  - [ ] 坏文件不阻断整批，报告可查；内联效果在卡加载前已注册。
- Terminology: 组件 loader

### S9: 官方语料导入脚本
- Status: Todo
- Target: 新增 `scripts/import_kards_cards.py`（或 .NET 工具，二选一，倾向 Python 对齐既有 `scripts/`）
- Approach: 读 `docs/初始设计/kards官方卡牌.json` → 逐条产出 `*.card.json`（Orc 自定义格式）：`id`←`json.id`；`name`←`title["zh-Hans"]`；`type`←`[type]`；`faction`/`rarity` 原样；`kredits`→部署费、`operationCost`→行动费；`attack`/`defense`；`attributes` 原样；丢弃 `set`/`text`/`image`/`permanent_pool`/`can_create`/`exile`/`reserved`/`import_id`；非单位卡不产出 `battleStats`。
- Acceptance:
  - [ ] 1646 张全部产出，无人工干预；样本字段与官方一致。
- Terminology: —

### S10: 测试与验收
- Status: Todo
- Target: `tests/Orc.Game.Tests`（新增）、`tests/Orc.Tests`（预制体 inject）
- Approach: 新增覆盖——数据体读写与版本失败、组件 loader 两段式与相位、`type` 拆解四条规则、词条映射与未实现项留痕、内联效果注册与 ID 引用、部署词条触发器与重放、载入器目录扫描与单卡隔离、官方导入抽样（20–50 张）端到端。
- Acceptance:
  - [ ] 构建 0 警告；`Orc.Tests`/`Orc.Game.Tests`/`Orc.Script.Tests` 全绿。
- Terminology: —

## 进度快照（2026-10-05）

| 步骤 | 状态 | 说明 |
|---|---|---|
| S1 数据体格式与 JSON 读写 | **Done** | `Data/CardDataJson.cs`/`CardDataBody.cs`；单独构建通过（0 警告 0 错误） |
| S2 组件接口 + 注册面 + 加载上下文 | **Done** | `Data/CardComponentDefinition.cs`/`CardComponentRegistry.cs`/`CardComponentLoadContext.cs` |
| S3 六个内置组件定义与 loader | **Done** | `Data/Components/BuiltInCardComponents.cs`、`Data/CardAttributeMap.cs`、`Data/ComponentText.cs` |
| S4 卡牌加载器改造 | In Progress | S4a＝`CardDefinition` 组件集主构造已写完（**待验证**）；S4b＝`CardBase` 两段式待做 |
| S5 预制体 `injects` + 框架侧注入 | Todo | 需 `src/Orc`（冲突解除后） |
| S6 部署词条触发器 | Todo | — |
| S7 A4 废弃与测试迁移 | Todo | — |
| S8 数据体载入器（含内联注册） | Todo | — |
| S9 官方语料导入脚本 | **Done** | `scripts/import_kards_cards.py`；1646 张全部产出至 `outputs/kards-cards/`（0 跳过） |
| S10 测试与验收 | In Progress | `tests/Orc.Game.Tests/CardDataBodyTests.cs` 已写（**待构建验证**）；端到端待 S4b–S8 |

**阻塞（已解除）**：并发任务（判定器机制）改动完成后，构建恢复；S4b–S8 已全部落地并验证。

## 验收结果（2026-10-05）

| 项 | 结果 |
|---|---|
| 构建 | **0 警告 / 0 错误**（`Orc` + `Orc.Game` + 三测试项目；`Orc.Game.Sample` 因进程占用未纳入） |
| `Orc.Tests`（内核） | **298 通过 / 0 失败** |
| `Orc.Game.Tests`（游戏层） | **703 通过 / 0 失败**（含迁移后的部署效果用例 ＋ 新增数据体用例 16 条） |
| `Orc.Script.Tests` | **9 通过 / 0 失败** |
| 官方导入脚本 | `scripts/import_kards_cards.py` → 1646 张数据体全部产出（0 跳过） |

### 交付物清单

- **新增（`src/Orc.Game/Cards/Data/`）**：`CardDataBody.cs`、`CardDataJson.cs`、`CardComponentDefinition.cs`、
  `CardComponentRegistry.cs`、`CardComponentLoadContext.cs`、`ComponentText.cs`、`CardAttributeMap.cs`、
  `CardDataLoader.cs`、`Components/BuiltInCardComponents.cs`（六个内置组件定义 ＋ loader）。
- **改动（内核 `src/Orc/Cards/`）**：`Prefabs.cs`（`InjectPrefab`/`EffectPrefab.Injects`/`SchemaVersion` 1→2/装载计划 Inject 步骤）、
  `PrefabJson.cs`（injects 读写）、`DynamicEffect.cs`（handler 收集 ＋ `TryGetEventHandler` ＋ 反射注入/撤销）、
  `Effect.cs`（`InjectByFramework`）。
- **改动（游戏层）**：`CardDefinition.cs`（组件集为唯一真源 ＋ 派生读面 ＋ `UnmappedAttributes`）、
  `CardBase.cs`（构造期 loader ＋ 加载期两段式 ＋ 具名触发器登记/查找；删除 `RebuildFromPersistence`/`LoadTagData`/`LoadKeywords`）、
  `UnitCard.cs`（`DeployKeywordTrigger` ＋ 部署链①段改触发器 ＋ 具名触发器登记）、`RetriggerSystem.cs`（重放改调触发器）、
  `EffectSystem.cs`（`CardEffectLoader` 的框架侧注入 `ApplyFrameworkInjects`）、`CardLibrary.cs`/`Match.cs`（A4 参数移除）。
- **删除**：`src/Orc.Game/Cards/DeploymentLogicData.cs`（A4 路径整体退场）。
- **测试**：新增 `tests/Orc.Game.Tests/CardDataBodyTests.cs`（16 条）；迁移 `PlayChainDeploymentTests.cs`、
  `DeathrattleAndRetriggerTests.cs`（部署效果改经"效果 + 注入部署词条触发器"）、`CardSkeletonTests.cs`、`CommandTestInfrastructure.cs`；
  适配 `tests/Orc.Tests/DynamicEffectTests.cs`（版本升版）。
- **脚本**：`scripts/import_kards_cards.py`。

### 遗留（如实申报）

1. ~~未做端到端集成测试~~ → **已补**：`tests/Orc.Game.Tests/OfficialCardDataSmokeTests.cs`——1646 张全量读入（0 失败）＋ 装配进 `Match` ＋ 一张官方卡成功打出（部署/单位化/数值就绪）。官方语料无效果字段，故只覆盖元数据/花费/数值/词条层。
2. **`inject` 仅支持默认区段**：预制体 `injects.band` 非空＝隔离记录（专门枚举区段名的解析未实现）。
3. ~~CONTEXT 术语登记未做~~ → **已完成**：根 `CONTEXT.md` 新增 7 条（卡牌数据体／组件定义／组件 loader／可反序列化组件／两段式加载／部署词条触发器／注入目标声明）；本域决策摘要见 `.ams/context/card-data-persistence/CONTEXT.md` 并已登记入 `CONTEXT-MAP.md`。
4. **`Orc.Game.Sample` 进程**（PID 38632 起）仍在运行并锁定 dll——全解决方案构建需先关闭。
