# Implementation — 03 hook 定义（KARDS 游戏层接入点与信号面）

> 执行序：**第 1 份**（前置：无）。独占文件：新增 `src/Orc.Game/GameHooks.cs`、新增 `src/Orc.Game/Output/GameHooksJson.cs`、根 `CONTEXT.md`（加性词条）、`tests/Orc.Game.Tests/GameHooksTests.cs`（扩展既有）。其余文件只读。
> 术语：本文件标题不使用 `hook` 指代广义面；`Hook` 保留内核窄义（触发器构造期声明的更新字符串订阅契约），广义面统一称 **接入点 / 观察面 / 扩展点**（登记 CONTEXT）。
> 实现 grill 结案（2026-10-05）：主问题 a（授权按代码实际修正事实性引用）＋ N2a（27 项以 `docs/kards-diy-可参考语料报告.md` §2.1 为唯一权威）＋ N3a（四词条入根 `CONTEXT.md`，`Hook` 只作指引）＋ N4a（`card.placed` 不入 17 条）＋ N8b（保持三值枚举，`部分` 拆行）。

## Scope

- Target：`src/Orc.Game` 游戏层接入点的**集中定义**（文档清单 ＋ 代码侧常量/导出器 ＋ 一致性核对）。
- Excludes：`src/Orc` 内核改动；27 项待补信号的行为实现（仅登记，不接线）；任何效果表达力实现。

## Implementation Steps

### S1: 术语登记（消除 `hook` 重载）
- Status: Done
- Target：根 `CONTEXT.md`（加性词条）
- Approach：
  - **接入点**：效果/外部对局局内逻辑的受控介入位置（订阅、band 注入、判定器替换、修饰链改写等）。_Avoid_: hook、钩子、插件。
  - **观察面**：外部只读获取对局进展的接口（即时更新监听、段轮询、快照/事件流导出）。_Avoid_: 监听器、事件通知。
  - **扩展点**：装配期注入面（注册表、提供器、构造注入）。_Avoid_: 配置项、插件位。
  - **Hook**：**不重定义**——权威定义已在 `.ams/context/orc-engine/CONTEXT.md:103`（措辞与本文件原有拟稿逐字一致）；根词表只加"仅作指引"词条，指向该处，并禁止以之指代接入点/观察面/扩展点。
- Acceptance:
  - [x] 根 `CONTEXT.md` 含三词条（接入点/观察面/扩展点，各带 `_Avoid_`）＋ `Hook` 指引词条；四者边界互斥、无 `hook` 重载；`Hook` 无第二真源。
- Rationale：三份 kards 报告把 `hook` 用作广义介入点，与内核定义冲突，须先裁决；`Hook` 已在内核词表定稿，重写＝双真源。

### S2: 清单层①——对外信号全集（17 条）
- Status: Done
- Target：本文件（清单表）
- Approach：集中枚举 17 条对外信号（常量源＝`src/Orc.Game/GameUpdates.cs`；行号为 2026-10-05 基线）：

  | 组 | 信号（字面值） | 载荷键 | 发射点（文件·成员） | 发射助手 |
  |---|---|---|---|---|
  | 回合五连 | `turn.start.before` | Player, TurnNumber | `Managers/TurnManager.cs:100` · `BeginTurnAsync` | 直发 |
  | | `turn.start` | Player, TurnNumber | `Managers/TurnManager.cs:101` · `BeginTurnAsync` | 直发 |
  | | `turn.start.after` | Player, TurnNumber | `Managers/TurnManager.cs:111` · `BeginTurnAsync` | 直发 |
  | | `turn.end.before` | Player, TurnNumber | `Managers/TurnManager.cs:86` · `EndTurn` | 直发 |
  | | `turn.end` | Player, TurnNumber | `Managers/TurnManager.cs:87` · `EndTurn` | 直发 |
  | 通用三项 | `card.played` | Card, Player | `Cards/UnitCard.cs:97` · `HandlePlayChainAsync`；`Cards/CommandCard.cs:98` · `HandlePlayAnnounceAsync` | `EmitCardPlayed` |
  | | `card.drawn` | Player, Card | `Managers/PlayerManager.cs:156,162` · `DrawCard` | `EmitCardDrawn` |
  | | `card.stat.changed` | Card, ChangedFields | `Cards/CardModifierComponent.cs:632` · `RequestRerunCoreAsync` | `EmitCardStatChanged` |
  | 加载与手牌 | `card.load` | Card, Player | `Cards/CardBase.cs:177` · `LoadAsync` | `EmitCardLoad` |
  | | `card.hand.add` | Card, Player | `Match/MatchCardService.cs:220` · `PlaceToHandAsync`；`Managers/PlayerManager.cs:163` · `DrawCard` | `EmitCardHandAdd` |
  | | `card.discarded` | Card, Player | `Match/MatchCardService.cs:215` · `PlaceToHandAsync`（烧牌）；`Managers/PlayerManager.cs:255` · `DestroyAndEmitDiscardedAsync` | `EmitCardDiscarded` |
  | 单位系列 | `card.died` | Card | `Commanding/CommandManager.cs:1349` · `ProcessDeathAsync` | `EmitCardDied` |
  | | `unit.joined` | Unit, Position | `Cards/UnitCard.cs:159` · `HandleJoinChainAsync` | `EmitUnitJoined` |
  | | `unit.deployed` | Unit, Position | `Cards/UnitCard.cs:138` · `HandleDeployChainAsync` | `EmitUnitDeployed` |
  | | `unit.position.changed` | Unit, OldPosition, NewPosition | `Commanding/CommandManager.cs:1177` · `HandleUnitMoveAsync` | `EmitUnitPositionChanged` |
  | 洗切 | `deck.shuffled` | Player, Deck | `Match/Match.cs:348` · `ShuffleDeckAsync` | `EmitDeckShuffled` |
  | 类型变更 | `unit.types.changed` | Unit, AddedType | `Cards/UnitCard.cs:266` · `AddUnitTypeAsync` | `EmitUnitTypesChanged` |

  - **发射助手计数＝12（对应 12 条信号）＋1（内核转发）**。turn 五连无助手，经 `TurnManager.EmitTurnAsync`（`TurnManager.cs:116`，`private`）直发 `LogicEngine.Emit`——发射点由此私有助手承载。
  - **边界（N4a）**：`EmitCardPlaced`（`GameUpdates.cs:266`）发射的是**内核**常量 `Updates.CardPlaced`＝`card.placed`（`src/Orc/Core/Updates.cs:15`），**不属本清单 17 条**（内核信号；仅经 `GameUpdates` 提供转发助手）。本清单只收 `GameUpdates` 自有 17 条。
  - 载荷键共 10 条常量（`GameUpdates.cs:129-156`）：Player / TurnNumber / Card / Unit / Position / OldPosition / NewPosition / ChangedFields / Deck / AddedType。
- Acceptance:
  - [x] 17 条信号逐条给出字面值、载荷键、发射点（文件·成员）、发射助手（或"直发"）；与 `GameUpdates.cs` 权威一致。
- Rationale：§4 缺口 #6 要求"信号↔触发器"权威清单；发射点必须可静态定位，故以"文件·成员"落位。

### S3: 清单层②——接入点与扩展点（八类）
- Status: Done
- Target：本文件（清单表）
- Approach：分类枚举，每条给**真实类型/成员**（非文件名）＋"文件:行"权威引用：
  1. **三类接线途径**：
     - `Bus.Mount<TView>(Trigger<TView>)`（`src/Orc/Core/Bus.cs:31`）；游戏层实际调用点：`Commanding/CommandManager.cs:199,210`、`Cards/RetriggerSystem.cs:92`。
     - `Effect.Inject<TView>(...)`（`src/Orc/Cards/Effect.cs:79`）——**`protected`**，仅装载语境 `OnMount` 可用；band 参数类型为 `Enum`。
     - `JudicatorRegistry.RegisterModing`（`src/Orc/Core/JudicatorRegistry.cs:89` 统一签名 / `:117` 泛型强类型面）。
  2. **触发器分层注册表**：`Orc.Game.Triggers.TriggerRegistry.Register<TView>(Trigger<TView>, TriggerLayer)`（`Triggers/TriggerRegistry.cs:23`）＋枚举 `TriggerLayer{LowLevel=0, External=1}`（`Triggers/TriggerLayer.cs:8`）；游戏层唯一注册点＝`Match.Initialize`（`Match.cs:533-536`，当前 4 项均为 `LowLevel`）；查询面 `GetByLayer`（`:45`）/`Entries`（`:41`）。
  3. **判定器**：`Judicators/JudicatorNames` 共 **6** 条——
     - 验证类 4 条：`validation.cost.check`（`:16`）/`validation.counter.use`（`:19`）/`validation.move.recheck`（`:22`）/`validation.attack.recheck`（`:25`）；
     - 示范类 2 条：`deck.top.tag`（`:31`）/`targeting.candidate.eligibility`（`:35`）。
     - 实现类：`CostCheckJudicator.cs:13`／`CounterUseJudicator.cs:15`／`MoveRevalidationJudicator.cs:20`／`AttackRevalidationJudicator.cs:19`／`DeckTopTagJudicator.cs:20`／`TargetEligibilityJudicator.cs:18`；独立构造路径内置解析＝`BuiltInValidationJudicators.cs:21`。
     - **装配面（真实）**：`Match.Initialize` 固定注册段**只注册 4 条验证类**（`Match.cs:499-509`）；2 条示范类经**外部装配段** `judicatorAssembly`（`Match.cs:513`）可选注入（实证：`tests/Orc.Game.Tests/JudicatorDeckTopTagTests.cs:153`）。
  4. **修饰/光环/管线介入面**（均为**类型名**，非文件名）：
     - `Cards/CardModifierComponent.cs:48` `CardModifierComponent`；
     - `Cards/ModifierSystem.cs` 内：`CardStatFields`(:30)、`Modifier`(:130) 及模板子类族、`ModifierExpiry`(:65)、`ModifierMountContext`(:91)；
     - `Cards/AuraSystem.cs` 内：`AuraRegistry`(:121)、`AuraDeclaration`(:31)；
     - `Cards/StatUpdateDetection.cs` 内：`StatUpdateComparison`(:81)、`UnitStateUpdateDetector`(:138)、`DeployCostUpdateDetector`(:259)；
     - `Board/GameEnvironment.cs:170` `CollectAuras`、`:214` `RerunAllCardsAsync`；
     - `Players/Hq.cs:84` `AddDamageRewriter`、`:105` `AddLethalIntervention`、`:129` `RemovePipelineHooksBySource`。
  5. **卡上公开触发器**：`UnitCard.cs:64/67/70/73/76`（`PrePlayTrigger`/`PlayTrigger`/`DeployTrigger`/`JoinTrigger`/`UnitizeTrigger`）；`CommandCard.cs:52/55`（`PrePlayTrigger`/`PlayTrigger`）；`CounterCard.cs:59`（`UseCounterTrigger`）。
  6. **监听 handler 接入面**：`Bus.Mount`＋`Trigger.Register`＋`Effect.Inject` band 注入（自写有状态 handler 模式）。
  7. **扩展点**：
     - `HookRegistry.Register`（`src/Orc/Core/HookRegistry.cs:19`）；
     - `Scripting`：`ScriptRequest`（`src/Orc/Core/Scripting.cs:21`）、`IScriptEvaluator`（`:71`）；
     - `PrefabManager`：`RegisterHandler`(`src/Orc/Cards/PrefabManager.cs:35`)/`RegisterPrefab`(:56)/`LoadDirectory`(:78)/`ResolveHandler`(:114)；
     - `OrchestrationManager.Register`(`src/Orc/Core/Orchestration.cs:33`)/`BuildAuditChain`(:113)；**`LogicEngine.ExportAuditChainJson`**（`src/Orc/Core/LogicEngine.cs:83`——归属为 `LogicEngine`，非 `Orchestration`）；
     - `ActionScope`（`src/Orc/Core/ActionScope.cs:10`）；
     - `CardLibrary` 六个延迟提供器（`Cards/CardLibrary.cs:44-49`）；
     - `Match` 构造注入点（`Match/Match.cs:81-93`）与 `Initialize` 接线点（`Match.cs:384`）。
  8. **观察面**：即时更新＝`LogicEngine.Subscribe`（`LogicEngine.cs:165`）/`OnImmediateUpdate`（`:324`）；拉取式段轮询＝`LogicEngine.TakeSegments`（`:293`）/`TryTakeSegment`（`:306`）；快照/事件流导出＝`src/Orc/Output/*`（`SnapshotJson`/`EventStreamJson`/`AuditChainJson`，均 `public static string Serialize(...)`）。
     - **归属更正**：`TakeSegments`/`TryTakeSegment` **只**定义在 `LogicEngine`（`EventStream` 上无同名成员），且只算**观察面**——不再同时列进第 7 类。
- Acceptance:
  - [x] 八类每类 ≥1 条权威引用；引用均为真实类型·成员（非文件名）；`TakeSegments` 仅归观察面。
- Rationale：`invariants.json` 式"清单＋可核对"；避免"文件名当成员名"的伪引用。

### S4: 代码侧常量类 `GameHooks`
- Status: Done
- Target：新增 `src/Orc.Game/GameHooks.cs`
- Approach：
  - **聚合索引（转发引用，不复制字面值）**：17 信号常量 → `GameUpdates.*`；10 载荷键 → `GameUpdates.Payload*`；6 判定器名 → `JudicatorNames.*`；触发器分层 → `TriggerLayer` 成员列表 `TriggerLayers`（`Enum` 值转发；分层名由 `ToString` 派生，防字面值第二真源）。
  - **新定义分类枚举**：`GameHookAccessPath{Subscribe, BandInject, JudicatorModing}`、`GameHookObservation{ImmediateUpdate, SegmentPoll, Snapshot}`、`GameHookExtensionKind{Registry, Provider, ConstructorInjection}`。
  - **待补层**：27 项 kards 触发器名常量（`unitDeployed`…，见 S7）＋状态枚举 `GameHookPendingStatus{Available, Deferred, NotPlanned}`（＝已具备/后置/不做，**三值**）＋`PendingTriggers` 表（`KardsTrigger`、`Status`、对位信号）。
  - **明示排除**：不含 `card.placed`（内核信号，见 S2 边界）。
- Acceptance:
  - [x] 编译通过（0 警告）；转发常量与 `GameUpdates`/`JudicatorNames`/`TriggerLayer` 逐条一致（S6 断言）；`PendingTriggers.Count == 27`。
- Rationale：§4 缺口 #6；避免双真源（转发引用而非复制字面值）。

### S5: 导出器（`invariants.json` 式映射）
- Status: Done
- Target：新增 `src/Orc.Game/Output/GameHooksJson.cs`
- Approach：静态类 + `public static string Serialize(bool indented = false)`，产出 JSON：
  ```
  { "signals": [ { "name": "...", "payloadKeys": [...], "emitters": [...], "subscribersHint": [...] } ],
    "judicators": [ { "name": "...", "implementations": [...] } ],
    "triggerLayers": [ "LowLevel", "External" ],
    "pending": [ { "kardsTrigger": "unitDeployed", "orcStatus": "已具备|后置|不做", "orcCounterparts": [...] } ] }
  ```
  - **不复用** `src/Orc/Output/JsonValueWriter`——其为 `Orc` 程序集 `internal`，且约束"`src/Orc` 本批不触动"；本导出器**自持** `System.Text.Json.Utf8JsonWriter` 手写写出（定深结构、无递归，故无需深度上限）。
- Acceptance:
  - [x] 导出 JSON 可解析（`JsonDocument.Parse` 成功）、覆盖全部 17 信号、6 判定器、2 分层、27 待补项。
- Rationale：`invariants.json` 对位；`Orc/Output` 风格对齐但不可复用（可见性/约束）。

### S6: 一致性核对测试
- Status: Done
- Target：`tests/Orc.Game.Tests/GameHooksTests.cs`（**扩展既有文件**——该文件为 2A 验收锚点⑤，约束"迁移非删除、总数不减"）
- Approach：新增断言组（不动既有用例）：
  - **转发一致**：反射枚举 `GameUpdates`/`JudicatorNames` 的 `public const string` 字段，断言 `GameHooks` 逐条转发同名值；`GameHooks` 信号集合恰 17 条、载荷键恰 10 条、判定器名恰 6 条。
  - **发射助手下落**：12 条信号 ↔ `GameUpdates.Emit*`（反射存在）；5 条 `turn.*` ↔ `TurnManager.EmitTurnAsync`（`NonPublic|Instance` 反射存在）。
  - **判定器装配（修正口径）**：`Match.Initialize` 后 4 条验证类经 `match.Judicators.Resolve(name)` 可达；2 条示范类**默认不可达**（`Resolve` 抛 `KeyNotFoundException`），仅在注入 `judicatorAssembly` 后可达。
  - **触发器分层**：`Enum.GetValues<TriggerLayer>()` ≡ `GameHooks.TriggerLayers`；`Initialize` 后 `match.TriggerRegistry.GetByLayer(LowLevel).Count >= 4` 且各项 `Layer` 为已定义枚举值。
  - **待补层**：`PendingTriggers.Count == 27`、kards 名唯一、全部有状态；`Available` 项的对位信号 ∈ 17 信号集。
- Acceptance:
  - [x] 新增用例全绿；既有用例不回归（总数不减）；构建 0 警告。
- Rationale：`effect-contract.js` 式自检；把"文档断言"变成"可执行断言"，防止清单与代码漂移。

### S7: 待补层——kards-diy 27 项监听触发器对齐表
- Status: Done
- Target：本文件（对齐表）
- Approach：**权威源＝`docs/kards-diy-可参考语料报告.md` §2.1（`invariants.json:96-124`，共 27 项）**，逐项判定（判定规则见下）：
  | # | kards trigger | OrC 对位 | 结论 |
  |---|---|---|---|
  | 1 | `turnStart` | `turn.start.before` / `turn.start` / `turn.start.after` | 已具备 |
  | 2 | `turnEnd` | `turn.end.before` / `turn.end` | 已具备 |
  | 3 | `unitDeployed` | `unit.deployed` | 已具备 |
  | 4 | `attack` | `UnitAttackTrigger`「攻击执行」流程位 | 已具备（流程位） |
  | 5 | `orderPlayed` | `CommandCard.PlayTrigger`「打出宣告」＋ `card.played` | 已具备 |
  | 6 | `shuffle` | `deck.shuffled` | 已具备 |
  | 7 | `unitMobilized` | 无信号（移动仅 `unit.position.changed`） | 后置 |
  | 8 | `attacked`（被攻击） | 无被攻击方专用流程位/信号 | 后置 |
  | 9 | `afterAttack` | 无攻击收尾流程位/信号 | 后置 |
  | 10 | `afterAttackHQ` | 无 | 后置 |
  | 11 | `friendlyDeath` | 无（`card.died` 为全局、无阵营过滤） | 后置 |
  | 12 | `friendlyAttacked` | 无 | 后置 |
  | 13 | `friendlyDamaged` | 无（伤害走 `AttackDamageTrigger` band、无信号） | 后置 |
  | 14 | `hqDamaged` | 无（HQ 伤害走数值路径 `Hq` 管线） | 后置 |
  | 15 | `enemyKilled` | 无 | 后置 |
  | 16 | `afterKill` | 无 | 后置 |
  | 17 | `targetedByOrder` | 无 | 后置 |
  | 18 | `kreditsGained` | 无（指挥点增减无信号） | 后置 |
  | 19 | `unitLeft`（非消灭离场） | 无 | 后置 |
  | 20 | `friendlyPinned` | 无（压制机制未实现） | 不做 |
  | 21 | `friendlySuppressed` | 无（压制机制未实现） | 不做 |
  | 22 | `friendlySilenced` | 无（沉默机制未实现） | 不做 |
  | 23 | `friendlyRetreated` | 无（撤退机制未实现） | 不做 |
  | 24 | `friendlySurvived` | 无（存活判定未实现） | 不做 |
  | 25 | `unpinned` | 无（压制解除机制未实现） | 不做 |
  | 26 | `impactUsed` | 无 | 不做 |
  | 27 | `chargeNow` | 无（充能机制未实现） | 不做 |

  - **判定规则**（可复核）：**已具备**＝存在同名/同义对局信号（`GameUpdates.*`）或专用流程触发器可直接介入；**后置**＝语义可对位但缺信号/流程位；**不做**＝依赖未实现机制（压制/沉默/撤退/存活/充能），本批明确排除。
  - 合计：已具备 6、后置 13、不做 8（**恰 27**）。
  - **枚举值保持三值**（N8b）：原表 `counter`/`counterSet` 的"部分"不属本 27 项（已剔除）；如未来需"部分"，拆成"已具备行＋缺口行"两行，不新增枚举值。
  - **来源更正**：原 S7 引「`可hook点清单报告.md` §A2」有误——该节列 ~44 个事件名、自指权威副本为 `invariants.json` 96 项白名单；「27 项」的真正定义在 `可参考语料报告.md` §2.1。
- Acceptance:
  - [x] 27 项逐条有结论；后置/不做项写入 `GameHooks` 待补层常量。
- Rationale：Q6=c 的"待补层"；逐条可验证（可执行断言见 S6）。

## 交付物

- 本文件（清单层 + 待补层 + 实施计划）。
- `src/Orc.Game/GameHooks.cs`、`src/Orc.Game/Output/GameHooksJson.cs`、根 `CONTEXT.md` 词条、`tests/Orc.Game.Tests/GameHooksTests.cs` 扩展用例。

## 实施记录（2026-10-05）

- S1：根 `CONTEXT.md` 新增词条「接入点 / 观察面 / 扩展点」+「Hook（指引，不重定义）」；`Hook` 权威定义仍在内核词表、未生第二真源。
- S2：17 条信号清单入本文件（字面值／载荷键／发射点／发射助手）；更正"13 助手"为 **12 助手 + 5 条 turn 直发**；`card.placed` 明确排除（内核信号）。
- S3：八类权威引用全部改为**真实类型·成员**（`CardModifierComponent`/`AuraRegistry`/`AuraDeclaration`/`Modifier` 族/`UnitStateUpdateDetector` 等；`ExportAuditChainJson` 归属 `LogicEngine`；`TakeSegments`/`TryTakeSegment` 仅归观察面）；判定器更正为 **6 条**并写明"4 内置固定段 + 2 外部装配段"。
- S4：新增 `src/Orc.Game/GameHooks.cs`（17 信号 + 10 载荷键 + 6 判定器名 + 分层表，全部转发引用；3 分类枚举；`PendingTriggers` 27 项）。
- S5：新增 `src/Orc.Game/Output/GameHooksJson.cs`（自持 `Utf8JsonWriter`；覆盖 signals/judicators/triggerLayers/pending）。
- S6：扩展 `tests/Orc.Game.Tests/GameHooksTests.cs` 新增 7 组一致性断言（转发一致／发射点反射存在／判定器装配口径／分层注册／27 项状态／导出 JSON 可解析）。
- S7：27 项对齐表按 `可参考语料报告.md` §2.1 权威源重排（6 已具备 / 13 后置 / 8 不做）。

**验证证据**
- `dotnet build OrcEngine.sln -warnaserror` → **0 警告 / 0 错误**。
- `dotnet test OrcEngine.sln` → 全绿：`Orc.Game.Tests` 621、`Orc.Tests` 298、`Orc.Lua.Tests` 157、`Orc.Script.Tests` 9（`GameHooksTests` 既有 8 例不回归，新增 7 例）。

### 行号同步（02 输入接口执行后，2026-10-05）

02 改动 `UnitCard`／`CommandCard`／`CommandManager` 造成行号漂移，S2 表格与 S3 第 5 类已同步：

- `UnitCard`：触发器声明 `63/66/69/72/75` → **`64/67/70/73/76`**；`card.played`（`EmitCardPlayed`）`89` → **`97`**；`unit.deployed`（`EmitUnitDeployed`）`130` → **`138`**；`unit.joined`（`EmitUnitJoined`）`151` → **`159`**；`unit.types.changed`（`EmitUnitTypesChanged`）`258` → **`266`**。
- `CommandCard`：触发器声明 `51/54` → **`52/55`**；`card.played`（`EmitCardPlayed`）`90` → **`98`**。
- `CommandManager`：`unit.position.changed`（`EmitUnitPositionChanged`）`1009` → **`1151`**；`card.died`（`EmitCardDied`）`1181` → **`1323`**。
- 同一同步已落到 `src/Orc.Game/Output/GameHooksJson.cs` 的发射点字符串（导出物与本文档一致）。
- 其余引用（`TurnManager`／`PlayerManager`／`MatchCardService`／`Match`／`CardBase`／`CardModifierComponent`／`JudicatorNames`）02 未触碰，行号不变。
- S6 一致性测试为**反射按成员名**断言，不受行号漂移影响（02 后仍全绿；`Orc.Game.Tests` 636）。

### 行号同步（01 完整流程执行后，2026-10-05）

01 改动 `Match.cs`／`TurnManager.cs`／`CommandManager.cs` 造成行号再次漂移，S2 表格与 S3 引用已同步：

- `TurnManager`：`turn.start.before` `98` → **`100`**；`turn.start` `99` → **`101`**；`turn.start.after` `109` → **`111`**；`turn.end.before` `84` → **`86`**；`turn.end` `85` → **`87`**（相位门禁扩展所致）。
- `CommandManager`：`unit.position.changed`（`EmitUnitPositionChanged`）`1151` → **`1177`**；`card.died`（`EmitCardDied`）`1323` → **`1349`**（相位门禁统一＋01 新增入口段所致）。
- `Match`：`deck.shuffled`（`EmitDeckShuffled`）`324` → **`348`**；`Initialize` 签名 `360` → **`384`**；构造注入点 `80-92` → **`81-93`**；判定器固定注册段 `475-485` → **`499-509`**；外部装配段 `_judicatorAssembly?.Invoke` `489` → **`513`**；触发器分层注册点 `509-512` → **`533-536`**。
- 同一同步已落到 `src/Orc.Game/Output/GameHooksJson.cs`（turn 五连／`card.died`／`unit.position.changed`／`deck.shuffled`）。
- 其余引用（`Cards/*`／`PlayerManager`／`MatchCardService`／`CardBase`／`CardModifierComponent`／`JudicatorNames`／`Bus.Mount` 调用点 `CommandManager.cs:199,210`）01 未触碰或位于改动点之前，行号不变。
- 01 后全绿：`Orc.Game.Tests` **647**（新增 `MulliganFlowTests` 9＋`MatchEndToEndTests` 2）。
