# Requirements Grill Plan — 卡牌数据体持久化与加载

> 状态：**全部结案**（需求 grill 收束）。定稿见 `requirements.md`；实现待办见 `implementation-grill-plan.md`。

## 基线事实（代码调研结论，供实现参考）

- 现状＝**纯代码注册**：`CardLibrary.Register(id, CardDefinition)`（`src/Orc.Game/Cards/CardLibrary.cs:8`）；定义集经 `Match` 构造参数传入并注册（`Match.cs:384-416`）。
- 卡牌数据来源面（均为代码对象）：①定义 `CardDefinition`（构造期 fail-fast，`CardDefinition.cs:18`）；②效果 `CardEffectRegistry` 两条声明路径——`Declare(cardId, effectIds)` / `DeclarePrefab(cardId, prefabIds)`（`EffectSystem.cs:62/102`）；③部署逻辑 `DeploymentLogicRegistry`（`DeploymentLogicData.cs:144`，**将废弃**）。
- 已有持久化范式：`EffectSnapshot` + `PrefabJson`（UTF-8、camelCase、`schemaVersion` 强校验不降级、`Serialize`/`Deserialize`/`TryDeserialize`，`src/Orc/Cards/PrefabJson.cs`）；`PrefabManager.LoadDirectory` 扫 `*.prefab.json`、单文件失败隔离 + `PrefabLoadReport`（`PrefabManager.cs:17/78`）。
- 效果预制体：`TriggerPrefab.Hooks` → `EffectPrefab.MountedHooks`（`Prefabs.cs:314`）；`BuildLoadPlan()`（`Prefabs.cs:356`）；`EventPrefab` 来源＝`CsxSource` 或 `AssemblyKey`（`Prefabs.cs:36`）；被动须有 hooks、主动不得有 hooks（`Prefabs.cs:164`）。
- `Effect.Inject<TView>`＝注册到具名 band、卸载自动撤销（`src/Orc/Cards/Effect.cs:79/199`）；**仅可在装载语境（`OnMount`）调用**（`Effect.cs:91`）。
- 效果装载链：声明解析 → 构造 → 登记 → 预制体装载（③b）→ `MountPassiveEffects` → 残留清理；**主动效果不装载**（`EffectSystem.cs:183/275`）。
- 卡牌加载模板七步：`RebuildFromPersistence`（空实现扩展点，`CardBase.cs:305`）→ ID 分配 → `LoadTagData` → `GenerateDeploymentLogic` → `LoadKeywords` → `LoadEffectsAsync` → 广播 `card.load`（`CardBase.cs:166-178`）。
- 组件装配时机：`FactionCostData`＝构造期（`CardBase.cs:36`）；`BattleStatsData`＝构造期（`UnitCard.cs:47`）；`TagData`＝加载期（`CardBase.cs:198`）；`UnitStateData`/`CommandData`＝单位化时（`UnitCard.cs:177`）。
- 部署链时序（`UnitCard.cs:96-138`）：`card.played` → **A4 段** → 单位化 → `card.placed` → `unit.deployed` → 收尾（扣费/离手/词条落点）。
- `RetriggerSystem`：**再触发仅执行效果动作、不发射链级信号**（`RetriggerSystem.cs:51`）；`ExecuteDeployReplayAsync`（`:175`）；入口点 `RequestDeployAsync`/`RequestDeathrattleAsync`（`GameEntryPoints.cs:253-257`）。
- 视图：`Orc.Cards.CardEventView` 只声明 `Card`/`Effect`（`src/Orc/Cards/CardLifecycle.cs:10`，预制体默认视图）；`Orc.Game.Triggers.CardTriggerView` 声明 `Card`/`Player`/`Position`（`CardTriggerView.cs:15`）。
- 部署更新 `unit.deployed` 载荷＝`{ Unit, Position }`（`GameUpdates.cs:97/141`）。
- 运行动态能力：`MatchCardService.CreateAsync`（`MatchCardService.cs:165`）；`DeclarePrefab` 同卡重复声明＝拒绝（`EffectSystem.cs:124`）。

### 官方语料实测（`docs/初始设计/kards官方卡牌.json`，1646 条）

- 键：`id/set/text/type/image/title/rarity/faction/kredits/reserved/import_id` 各 1646；`attack/defense/operationCost` 959；`attributes` 560；`permanent_pool` 410；`can_create` 195；`exile` 16。
- `type`：`order` 636 / `countermeasure` 51 / `infantry` 527 / `fighter` 141 / `tank` 139 / `bomber` 95 / `artillery` 57。
- `faction` 11 值、`rarity` 4 值——与项目枚举逐字一致（`TagData.cs:11/52`）；`kredits` 0..12（部署费）、`operationCost` 0..6（行动费）。
- `attributes` 英文标识；未实现项：`guard` 124 / `OnlySpawnable` 44 / `shock` 32 / `bond` 26 / `alpine` 18 / `heavyArmor2` 11 / `covert` 11 / `salvage` 9 / `BecomesVeteran:`/`VeteranOf:` 各约 16。
- **官方语料无任何效果字段**（无 effects/trigger/ability）。

## 结案记录

### Q1: "卡牌数据体"范围与用途 {Completed}
- 裁决：范围＝定义＋效果（**b**）；用途＝构建期卡包产出 ＋ 社区 DIY 明文（**①②**）；展示/本地化字段与对局存档不入本需求。

### Q7: schema 取向 {Completed}
- 裁决：以**官方语义为基准的自定义数据体**（同项对齐官方）；官方语料后续用脚本全量导入；Orc 额外项另加。

### Q7a: 词条标识与枚举字面值 {Completed}
- 裁决：数据体词条写**官方英文标识**；载入期经映射/别名表转为项目 `KeywordIds` 中文标识（不改 `KeywordIds`）；参值后缀（`heavyArmor1`→`{重甲,1}`）在载入期拆解；枚举用字面值。

### Q7b: 字段清单结构 {Completed}
- 裁决：结构＝**元数据 ＋ `components` 组件定义数组**；`set` 不要；`type` 为**列表**且**进组件**；`kredits`/`attack` 等进组件定义。

### Q8: 官方未实现词条处置 {Completed}
- 裁决：**保真 + 载入期分流**（不 fail-fast、留痕）；`name` 取 `title["zh-Hans"]`；载体元数据（`permanent_pool`/`can_create`/`exile`/`reserved`/`import_id`）本批不入体；`guard` **一视同仁**（不特别映射 `isGuard`）。

### Q9 / Q9a / Q9b: 组件 loader 模型 {Completed}
- Q9＝**a**：游戏层**静态注册面**（组件类型名 → loader）；卡牌加载器遍历 `components` 按名解析。
- Q9a＝**两段式**：第一段按游戏层 loader 定义顺序（防依赖丢失）；第二段对剩余组件按数据体声明序；要求可入数据体的组件实现**"可反序列化组件"接口**。
- Q9b＝**允许构造期加载组件**。

### Q10: "全部加载"语义 {Completed}
- 裁决：维持现状——实例化＋登记＋**被动挂载**；主动效果仅登记、施放时调用。

### Q11: 对局中动态加载范围 {Completed}
- 裁决：**c**——新预制体 ＋ 新卡 ＋ **给已有卡追加效果预制体**；**hook 不重放**（已错过的 hook 不回填）。

### Q12 / Q12a: 效果段引用形态与代码效果路径 {Completed}
- Q12＝**a**：数据体只写 `prefabs`（单一路径）。
- Q12a＝**a**：代码效果路径保留并存；**"运行时组装"路径保留**（先实例化效果预制体再独立挂载，测试不必套预制体实例化）。

### Q13: 预制体就绪前提 {Completed}
- 裁决（默认）：装配约定＝**注册面先全量就绪**；未命中＝单卡隔离记录（不中断整批）。

### Q14: 文件粒度 {Completed}
- 裁决（默认）：单卡一文件（`*.card.json`）＋ 目录扫描。

### Q15: 校验与失败策略 {Completed}
- 裁决（默认）：`schemaVersion` 强校验不降级；**单卡隔离 + 报告**；未知组件类型＝隔离记录；单卡内重复组件＝拒绝。

### Q16: 加载入口与时机 {Completed}
- 裁决（默认）：宿主在对局外载入目录 → 结果传给 `Match`（对齐构造注入 `cardDefinitions`）。

### Q17: 与既有注册表关系 {Completed}
- 裁决（默认）：载入**生成** `CardLibrary`/`CardEffectRegistry`/`PrefabManager` 注册项；不建平行装配路径。

### Q18: 范围排除 {Completed}
- 裁决（默认）：排除水位线、实例运行态组件、构筑数据。

### Q19: 交付物形态 {Completed}
- 裁决：**b —— 设计 + 实现**（含数据体载入器、组件 loader、加载器改造、官方导入脚本、测试）。

### Q20: 可入数据体的组件清单 {Completed}
- 裁决：第一段有序清单＝`factionCost`（`faction`/`kredits`）→ `battleStats`（`operationCost`/`attack`/`defense`，仅单位卡）→ `tagData`（`rarity`）→ `typeCategory`（`type` 列表）→ `keywords`（`attributes`）→ `effects`（`prefabs`）；元数据段＝`schemaVersion`/`id`/`name`/`tags`。

### Q21: 部署逻辑（A4）缺陷重审 {Completed}
- 裁决：**b —— 收敛废弃 A4 段**。理由：生产路径零使用者（`new DeploymentLogicRegistry()` 只在测试）、数据组件承载 handler 违背分层、与效果系统平行、数据体无法表达、失败口径不对称。

### Q22: "部署定向重放"能力 {Completed}
- 裁决（用户口径）：**不是重发信号、不是数据组件 handler 列表**——"重放部署"＝**单独重新触发"部署词条"这个主动触发器的效果**；实现＝①"触发单位部署"做成独立主动主触发器（带卡牌/效果参数），②打出链路里加 handler 触发它，③重放＝再次直接调用同一主动触发器（定向、不广播）。

### Q23: 部署链 handler 的注册主体 {Completed}
- 裁决：**a —— 效果预制体声明 `inject`（注入目标触发器/band）**，由装载链框架侧执行注入（效果作者不写 `OnMount`、不要求被动形态）。
- 连带：效果预制体 schema 新增 `inject`（`PrefabJson`/`EffectSnapshot.SchemaVersion` 涉升版，受控变更）；`RetriggerSystem.ExecuteDeployReplayAsync` 执行体改调主动触发器。

### Q24: 卡数据体与效果预制体的文件关系 {Completed}
- 裁决：**c —— 两者皆可**；引用效果 id 时**必须保证加载/构造卡牌前效果已加载**（装配顺序硬约束）；也允许内联效果预制体文本。

### Q25: 热重载 {Completed}
- 裁决：**a —— 不做热重载**，仅显式重载（对齐既有先例）。

### Q26: 导出方向 {Completed}
- 裁决：**a —— 先只做数据导入**（Orc 自定义格式 ＋ 脚本从官方数据生成 Orc 自定义格式）；数据导出先不做。

### Q27: 工程红线（`src/Orc` 是否可触动） {Completed}
- 裁决（2026-10-05）：**a —— 允许触动 `src/Orc`**（受控变更单独记账）；验收口径＝构建 0 警告 ＋ 三个测试项目全绿。
- 涉及：`src/Orc/Cards/Prefabs.cs`（`TriggerPrefab` 新增 `inject`）、`src/Orc/Cards/PrefabJson.cs`（字段与版本升版）。
