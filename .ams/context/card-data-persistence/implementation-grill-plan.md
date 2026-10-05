# Implementation Grill Plan — 卡牌数据体持久化与加载

> 状态：**进行中**（需求 grill 已收束，定稿见 `requirements.md`；本文件记录实现阶段待 grill 的问题）。

## 待 grill 的问题（按实现逻辑顺序）

### P1: 实现批次划分与顺序 {Completed}
- 裁决（2026-10-05）：**确认 S1→S10**（见 `implementation.md`）；依赖：S5 是 S6 的前提；S2/S3 是 S4 的前提。
- 提案（S1→S10）：
  - S1 数据体 schema 定义与 JSON 读写（元数据 + `components`；DTO 与领域对象）
  - S2 组件 loader 注册面 ＋ **"可反序列化组件"接口**（静态注册；契约参照 `ISerializableEffect`）
  - S3 内置组件 loader 实现（`factionCost`/`battleStats`/`tagData`/`typeCategory`/`keywords`/`effects`）
  - S4 卡牌加载器改造（两段式 ＋ 构造期加载；与现有七步的关系）
  - S5 效果预制体 schema 扩展（`TriggerPrefab.Inject`）＋ 装载链框架侧注入（涉 `src/Orc`＋版本升版）
  - S6 部署词条主动触发器 ＋ 部署链 handler ＋ `RetriggerSystem` 执行体改造
  - S7 A4 路径废弃与测试迁移（`DeploymentLogicData`/`Registry`/`LoadContext`/`GenerateDeploymentLogic`/部署链①段）
  - S8 数据体载入器（目录扫描、`LoadReport`、单文件隔离）
  - S9 官方语料导入脚本（官方 JSON → Orc 自定义格式）
  - S10 测试与验收（构建 0 警告、三测试项目全绿）

### P2: 构造期组件定义的载体 {Pending}
- `FactionCostData`/`BattleStatsData` 在构造期装配（`CardBase.cs:36`、`UnitCard.cs:47`），`CardLibrary.Instantiate` 经 `new UnitCard(engine, definition)` 传入（`CardLibrary.cs:132`）。
- 待定：`CardDefinition` 扩展为"元数据 + 组件定义集"，还是引入新的"载入态对象"（`LoadedCardData`）。

### P3: "可反序列化组件"接口契约 {Completed}
- 裁决（2026-10-05）：**b —— 定义 ＋ loader 分离**。每个数据体组件＝一个**定义类型**（实现 `ICardDataComponentDefinition`：静态 `ComponentName` ＋ 静态 `Read(JsonElement)`，反序列化与自校验 fail-fast）＋ 一个 **loader**（定义 → 装配：生成数据组件／填充字段／挂效果）；**静态注册面**注册序＝第一段加载顺序。
- 先例：`ISerializableEffect`（`Effect.cs:367`）；`EffectPrefab.BuildLoadPlan()`（`Prefabs.cs:356`）；`CardDefinition`→`CardBase`／`EffectPrefab`→`DynamicEffect` 的定义/实例分离。

### P2: 构造期组件定义的载体 {Completed}
- 裁决（2026-10-05）：**A —— 扩展 `CardDefinition`**：组件定义集为唯一真源，既有 12 参数构造保留（构造时转为组件定义集），既有读面（`Faction`/`DeployCost`/`Attack`/`UnitTypes`…）保留同名同义、内部从组件定义派生。数据体载入走"直接给组件定义集"入口。

### P4: 两段式加载与现有七步的关系 {Completed}
- 裁决（2026-10-05）：流程＝**构造期**（typeCategory 解析选类 → 构造期 loader：`factionCost`/`battleStats`）＋**加载期**（ID 分配固定环节 → 第一段：注册面 `phase=Load` 项按注册序 → 第二段：剩余组件按数据体声明序 → 广播 `card.load` 固定环节）。
  - P4a＝注册项带 `phase`（`Construction`/`Load`），**单注册表、单顺序**。
  - P4b＝**删除 `RebuildFromPersistence`**（职责已由组件 loader 模型取代；迁移 `tests/Orc.Game.Tests/CardSkeletonTests.cs:183` 的重写者）。
  - P4c＝`typeCategory` 的 loader 为**空操作**（数据在 `CardDefinition` 派生读面；消费点＝实例化选类与单位化填充 `UnitCard.cs:178`）。

### P5: loader 的加载上下文 {Completed}
- 裁决（2026-10-05）：**a —— 统一上下文对象 `CardComponentLoadContext`**（聚合 `LogicEngine` ＋ 既有专属上下文的延迟访问）；受控服务集合、非"整个引擎"；**不携带**本卡数据体与 owner（`card.Definition` 已是单一真源，避免第二入口）。
- 约束：词条 loader 需 `KeywordLoadContext`（`KeywordSystem.cs:103`）；效果 loader 需 `CardEffectLoadContext`（`EffectSystem.cs:146`）；两者均为**延迟读取**（`CardLibrary.cs:42-59` 的 provider 模式——服务在卡实例化后才就绪）。
- 待裁决：
  - a. **统一上下文对象** `CardComponentLoadContext`（聚合 `LogicEngine` ＋ 既有专属上下文的**延迟访问**）——loader 签名统一、按需取用（推荐）。
  - b. loader 签名泛型化，注册时绑定专属上下文类型——最严谨、签名与注册面复杂。
  - c. loader 在装配期闭包捕获服务——签名最简，但破坏既有"延迟读取"先例（服务晚于注册就绪）。

### P6: 效果引用（id）与内联两条路径 {In Progress}
- 现状：装载链只有"引用"一条（`CardEffectLoader` ③b：`engine.Prefabs.TryGetPrefab(prefabId)` → `DynamicEffectFactory.Instantiate`，`EffectSystem.cs:248-266`）；`DeclarePrefab` 同卡重复声明＝拒绝（`EffectSystem.cs:124`）。
- 待裁决内联的实现方式：
  - a. **内联＝先注册进 `PrefabManager`、再走引用路径（推荐）**——内联只是"预制体的第三种来源"（既有：程序集 `RegisterHandler` ＋ 目录 `LoadDirectory`）；装载链保持单一路径，审查链/一致性天然覆盖。
    - id 规则提案：内联可**省略 `root.id`**（自动生成 `<cardId>#<序号>`）；若显式给出 id 且已注册＝**拒绝**（对齐 `PrefabManager.RegisterPrefab` 现状）。
  - b. 内联＝直接实例化（不进注册表）——无 id 冲突问题，但装载链出现两条并行路径、绕过预制体注册面。
  - c. 混合（内联也注册，但允许匿名 id 自动生成）＝ a 的变体。

### P7: `inject` 声明在预制体 schema 的落点与版本升版 {Completed}
- 裁决（2026-10-05）：**a —— `EffectPrefab` 级 `injects` 清单**（与 `Modings` 并列）：`[{ target: "<触发器标识>", band: "<band 名>", event: "<eventId>", priority }]`。
- P7a＝`target` 按名查**触发器注册表**（2C 已建立的"底层触发器进注册表"机制）；查不到＝**隔离记录**。
- P7b＝`band` 按名解析目标触发器的 band 枚举成员；无法解析＝**隔离记录**（对齐 `EffectSystem.cs:254` 口径，不 fail-fast）。
- P7c＝`EffectSnapshot.SchemaVersion` **1 → 2**；旧版读出＝结构化失败（`PrefabJson.cs:65`）。影响面小（仓库内无 `.prefab.json` 文件，仅测试内动态 JSON）。

### P8: A4 废弃范围与测试迁移 {Completed}
- 裁决（2026-10-05）：P8a＝**默认 band**（`inject` 可省 band）；P8b＝**同意迁移验收口径**（多效果按序、单条异常隔离、无效果空转；放弃"空 handler 条目"概念）；P8c＝**同批**（S6 建新 → S7 删旧）。
- 实测引用面（`DeploymentLogic|部署逻辑` 命中）：
  - src（7 文件）：`DeploymentLogicData.cs`（46 处，＝A4 全部类型）；`CardBase.cs`（17 处）；`CardLibrary.cs`（9 处）；`Match.cs`（8 处）；`UnitCard.cs`（5 处）；`RetriggerSystem.cs`（3 处）；`DeathrattleSystem.cs`（1 处注释）。
  - tests（3 文件）：`DeathrattleAndRetriggerTests.cs`（4 个场景：`:282`/`:333`/`:360`/`:520`）；`PlayChainDeploymentTests.cs`（5 处手工 `AddData(new DeploymentLogicData())`，`:28`/`:72`/`:94`/`:123`）；`CommandTestInfrastructure.cs`（`CreateCommandMatch` 的 `deploymentLogicRegistry` 参数 `:124`）。
- 删除清单：`DeploymentLogicEntry`/`Data`/`Context`/`Rules`/`Registry`/`LoadContext`（整文件）；`CardBase` 的 `DeploymentLogicLoadContextProvider` ＋ `GenerateDeploymentLogic()` ＋ `LoadAsync` 第⑤步；`CardLibrary` 的 `deploymentLogicLoadContextProvider` 参数；`Match` 的 `_deploymentLogicRegistry` 与 `DeploymentLogicLoadContext` lambda；`UnitCard.HandleDeployChainAsync` ①段。
- 新增/改造：`UnitCard` 新增"部署词条触发器"（构造期创建 ＋ 部署链调用）；`RetriggerSystem.ExecuteDeployReplayAsync` 执行体改调该触发器；效果的 `inject.target` 标识＝该触发器名。
- 留痕口径变化：日志 `Source`（原"部署逻辑"/"部署逻辑生成"）随新体系改名，相关断言（`DeathrattleAndRetriggerTests.cs:355`、`PlayChainDeploymentTests.cs:111`）同步更新。
- 待裁决：
  - P8a **部署词条触发器形态**：`UnitCard` 上新增 `Trigger<CardTriggerView>`，band 用**默认**（inject 声明可省 band）（推荐）／用**专门枚举**（`inject` 必须写 band）。
  - P8b **迁移验收口径**：原断言覆盖的"多条按登记序执行、单条异常隔离、空 handler 无效条目、无组件＝跳过"须在效果路径下等价保留（多条＝多事件/handler、序＝band 优先级＋注册序）。
  - P8c **删除时机**：**同批（S6 建新 → S7 删旧，不长期并存）**（推荐）／分批（先并存后清理）。

### P9: 未实现词条分流与标识映射 {Completed}
- 裁决（2026-10-05）：P9a＝**a1 代码常量映射表**（统一表："数据体标识 → `KeywordIds`／参值规则"，含官方标识与 Orc 自有英文标识：`suppressed`/`inhibited`/`forecast`/`immune`/`cannotBeSuppressed`/`cannotBeInhibited`/`deathrattle`）；P9b＝**b1 只读集合留痕**（如 `CardDefinition.UnmappedAttributes`，不参与机制、可查询、**不写日志**）；P9c＝**c1 显式前缀表**（`heavyArmor2`→`{重甲,2}`、`intel3`→`{情报,3}`）。
- 注：未实现项**不走** `CardDefinition.ResolveKeywords` 的 fail-fast（`CardDefinition.cs:98`）。
- 现状：项目词条标识为**中文**（`KeywordIds` 15 枚：闪击/奋战/烟幕/伏击/被压制/被抑制/动员/钳击/预报/免疫/重甲/情报/无法被压制/无法被抑制/亡计，`KeywordSystem.cs:28-68`）；官方 `attributes` 为英文（`blitz`/`fury`/`smokescreen`/`ambush`/`pincer`/`mobilize`/`heavyArmor1..2`/`intel1..3`/`guard`/`shock`/`bond`/`alpine`/`covert`/`salvage`/`OnlySpawnable`/`BecomesVeteran:<id>`/`VeteranOf:<id>`）。
- 待裁决：
  - P9a **映射表载体**：a1 **代码常量映射表**（统一表：数据体标识 → `KeywordIds`／参值规则；含官方标识与 Orc 自有标识）（推荐）／a2 外部配置文件／a3 给 `KeywordIds` 加英文名自动映射。
  - P9b **未实现项留痕面**：b1 定义/实例上的**只读集合**（不参与机制）／b2 仅写引擎总流日志／b3 两者。
  - P9c **参值拆解**：c1 显式"前缀 → 词条 + 数字后缀 → 参值"表（`heavyArmor2`→`{重甲,2}`、`intel3`→`{情报,3}`）（推荐）／c2 数据体改结构化写法（违背"对齐官方语义"）。
- 关联：`CardDefinition.ResolveKeywords`（`CardDefinition.cs:98`）现对未注册标识 fail-fast；分流后未实现项**不走**该 fail-fast。

### P10: `type` 列表拆解规则 {In Progress}
- 映射：`order`→`Command`、`countermeasure`→`Counter`、`infantry|tank|artillery|fighter|bomber`→`Unit`＋`UnitTypes`（`CardCategory.cs:9`、`UnitType.cs:8`）。
- 提案（待确认）：①类别词（`order`/`countermeasure`）**至多一个**且列表长度必须为 1（不可与单位类型混用）；②单位类型词可多个、去重（重复＝拒绝）；③**空列表＝拒绝**（无法确定类别；数据体必须显式给类别）；④未定义字符串＝**单卡隔离记录**。

### P11: 运行时组装路径保留的保障 {In Progress}
- 提案（待确认）：①不改动 `DynamicEffectFactory.Instantiate`／`AddEffect`／`MountPassiveEffects` 的可见性与语义；②测试层保留至少一个"手动组装"用例（现有 `EffectSystemTests.cs:215-221` 一类即满足）。

### P12: 工程红线与验收口径 {Completed}
- 裁决（随 Q27 定）：**允许触动 `src/Orc`**；验收＝构建 0 警告 ＋ 三个测试项目全绿。
