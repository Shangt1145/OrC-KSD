# Implementation Grill Plan — KARDS 游戏层全面完备

> 状态：**已收敛**（全部未决项结案）。需求见 `requirements.md`；实施文档＝三份（见下方清单）。

## 结案记录

### I1: mulligan 承载与相位模型  Completed
- a：新增 `MatchPhase { Mulligan, Play, Ended }`，与既有二态加性并存；非 Play 相位拒绝动作入口。

### I2: 认输与终局记录  Completed
- `Match.Concede(player,ct)`；与 HQ≤0 共用"置 Ended"单源路径；终局原因枚举加性。

### I3: 单位/指令双层触发器与 targeter 下沉  Completed
- I3-a＝a：新增独立 `BeginMoveAsync`/`BeginAttackAsync`（各自跑限定候选交互、复用同一执行链）。
- I3-b：`CardBase` 不持 targeter；`UnitCard`（指向槽位）/`CommandCard`（指向目标）各自添加；targeter 内**多个选择器槽位、按顺序获取**；三类槽位＝手牌指向／放置后指向／场上单位指向，各带参（起始卡牌）提交前端。

### I4: hook 清单载体  Completed
- c：文档清单 + 代码侧常量（`GameHooks.cs`）+ 导出器（`invariants.json` 式）+ 一致性测试。

### I5: 三文档依赖与执行序  Completed
- a：**串行** ③hook → ②输入接口 → ①完整流程。

## 未决项（01 执行前复审；代码对照发现）

### I6: 01 执行前置与范围  In Progress
- 现状：02 入口（`BeginMoveAsync`/`BeginAttackAsync`/`BeginCommandPrePlayAsync`）与 03 交付（`GameHooks.cs`/导出器）在代码中**均不存在**；01 的 S5 直接调用 02 的入口，且前置声明＝03、02（执行序 03→02→01）。
- 选项：a＝先执行 03、02 再执行 01；b＝只执行 01，S5 改用现有入口（`BeginCommandAsync` 拖拽链/`BeginUnitPrePlayAsync`/`PlayUnitAsync`）；c＝只执行 S1–S4，S5 暂缓。
- 推荐：a（前置＝硬依赖，否则 S5 无法编译/验收）。

### I7: `MatchPhase` 与 `MatchState` 真源关系  In Progress
- 现状：S1 引入 `MatchPhase{Mulligan,Play,Ended}` 与既有 `MatchState{Preparing,InProgress,Ended}`（`MatchLifecycle`＝单一真源）并存；S3/S4 又要求终局"单源" → 出现两个 `Ended`。
- 选项：a＝相位并入 `MatchState`（加 `Mulligan`；`Preparing→Mulligan→InProgress→Ended`），`Match.Phase` 作派生只读视图；b＝`MatchPhase` 独立、`Ended` 以 `MatchLifecycle` 为唯一写入源；c＝其他。
- 推荐：a。

### I8: 门禁注入与改文件边界（`MatchLifecycle`/`Hq.cs`）  Pending
- S3 需 `EndReason{HqZero,Concede}` 且与 HQ≤0 同单源；HQ 归零现经 `Hq.RespondToZero → MatchLifecycle.End(winner)`。01 独占文件清单不含 `MatchLifecycle.cs`/`Hq.cs`。

### I9: 既有测试回归口径  Pending
- 61 处 `.Initialize(` 后立刻依赖"回合 1 已开始"（`TurnNumber==1`/`CurrentPlayer`/点数结算/`EndTurn`）；Initialize 改置 Mulligan 后成片受影响，"受控随改"的随改策略待定。

## 实施文档清单（定稿）

| 序 | 文档 | 内容 | 独占文件 |
|---|---|---|---|
| 1 | `03-hook定义.md` | 信号面/接入点/扩展点清单层 + `GameHooks` 常量 + 导出器 + 一致性测试 + 27 项待补层 | `GameHooks.cs`、导出器、`CONTEXT.md` |
| 2 | `02-输入接口.md` | 预行为入口形态 + 三类选择器槽位 + targeter 下沉 + 独立移动/攻击入口 + 入口全集清单 | `Cards/*`、`Targeting/*`、`PlayManager`、`CommandManager` |
| 3 | `01-完整流程.md` | `MatchPhase` + mulligan + 认输/终局 + 门禁 + 端到端集成测试 | `Match.cs`、`PlayerManager`/`MulliganManager`、`TurnManager` |

## 02-输入接口 执行前复核（**已结案 2026-10-05**）

> **结案记录**：G1＝**a**（不改桥接契约；"按顺序获取"改述为"按声明序呈现、一次提交"）／G2＝a（槽位参数经 `TargetingRequestContext` 请求级绑定，随 `TargetSlotDescription` 交付前端）／G3＝a（改称「手牌起始指向」并登记词表）／G4＝a（S2/S3 措辞修正为"声明面/视图注入"）／G5＝a（不新增 Kind，映射既有 `SingleSelectSlot`＋参数面）／G6＝全部采纳为执行约束／G7＝**a**（比照 03 补入口清单常量＋导出器＋一致性测试）。
> 以下条目保留为决策追溯（状态均已结案，不再变更）。

> 背景：①复核 `02-输入接口.md` vs `src/Orc.Game` 实际行为，发现 6 处不符；②`03-hook定义.md` 已交付完成（S1-S7 全 Done；`dotnet build OrcEngine.sln -warnaserror` 0 警告 / `dotnet test` 全绿：Orc.Game.Tests 621、Orc.Tests 298、Orc.Lua.Tests 157、Orc.Script.Tests 9）。
> 03 带来的新增约束（G6）与"需求-实施文档不一致"（G7）已并入下表。

### G1: 「按顺序获取」语义与桥接契约  Open（已有两处权威反证）
- 冲突：文档 S1 要求"同一请求内多槽位按声明序**依次索取**"。
- 权威反证：
  - `targeter/CONTEXT.md:31`（冻结词表）：「**多槽位**：**一次交互完成全部槽位**、按槽位组织提交」；
  - `ITargeterBridge.BeginInteraction`（L27）注释：「一次 Begin 完成全部槽位选择（多槽位场景）」。
- 待裁决：a＝不改契约（"按顺序获取"＝前端按声明序引导、后端一次校验）；b＝改契约＋改 `targeter/CONTEXT.md` 冻结词表为逐槽位索取。

### G2: 「槽位参数（起始卡牌）」载体  Open
- 现状：`TargetSlotDescription` 无参数字段；`TargetingRequestContext` 仅有 references/validator/listings 三类绑定。
- 待裁决：`TargetingRequestContext.WithSlotParameter(name,value)`（与现有绑定同构、随描述交付前端）vs `TargetSlotDescription` 新增 `Parameters` 字段。

### G3: 术语「手牌指向」与 `HandSelectSlot` 重载  Open
- 现状：`HandSelectSlot`（`targeter/CONTEXT.md:93`）语义＝候选为己方手牌卡引用（玩家从手牌选卡）；文档"手牌指向"语义＝以手牌实例为起始卡参数、目标域为空槽/单位/HQ。同名不同义。
- 待裁决：文档侧改名（建议「手牌起始指向」）并登记词表。

### G4: S2/S3 前提与代码不符（文档修正项）  Open
- `CardBase` 当前**无** targeter 字段/成员（S2 的"移除"是空操作）；`UnitCard`/`CommandCard` 的 `PrePlayTrigger`/`PlayTrigger` **已存在**（S3 的"新增"应改述为"视图注入槽位"）。属文档措辞修正，不涉行为。

### G5: 三类场景槽位是否需要新 Kind  Open（倾向结案）
- 新增证据：targeter 词表「三类候选来源」（`targeter/CONTEXT.md:111`）是**类别**维度、非 Kind 维度；新增 Kind 须改词表。
- 建议：三类场景槽位映射既有 `SingleSelectSlot` ＋参数面，**不新增 Kind**。

### G6: 03→02 交叉约束（本轮新增）  Open
1. **行号漂移**：03 文档以行号引用 02 独占文件（`UnitCard.cs:63/66/69/72/75`、`CommandCard.cs:51/54`、`CommandManager.cs:1009/1181`）；02 改造后须同步修订 03 文档行号（03 的一致性测试是反射按成员名，不受影响）。
2. **判定器协作**：候选域/筛选须经 `Judicators/JudicatorSelectionRule`（`AsFilter` 细筛接入）复用 `targeting.candidate.eligibility`；该类型明文"不改变 `Targeter`/`TargetFilter`/`TargeterManager` 既有语义"。
3. **术语边界**：02 的"输入入口"≠ 03 定义的「接入点/观察面/扩展点」（根 `CONTEXT.md:102-116`）；02 文档须显式声明边界，防词表重载。
4. **防双真源**：引用判定器名/信号名一律转发 `GameHooks`/`JudicatorNames` 常量，不复制字面值。
5. **回归基线**：02 以 03 交付后的 **621 例** `Orc.Game.Tests` 为"总数不减"参照。

### G7: R2「清单集中导出并有测试」未落到 S6（本轮新增）  Open
- 冲突：`requirements.md` R2 验收要求"输入入口全集清单（现有＋新增）**集中导出并有测试**"；02 的 S6 仅"本文件（清单表）"，S7 只覆盖新增入口，**无代码侧常量/导出器/一致性测试**。
- 待裁决：a＝比照 03 补入口清单常量 + 导出器 + 一致性测试；b＝改 R2 措辞为"文档清单＋文档审查"。

## 01-完整流程 执行前复核（**已结案 2026-10-05**）

> **结案记录**：H1＝**a**（严格按 A1：`Initialize` 置 `Mulligan`、先手回合延后；测试侧约 400 处机械随改）／H2＝a（`MatchState` 末尾加性追加 `Mulligan`；`Match.Phase` 为派生投影、不新增独立存储）／H3＝a（相位门禁统一到 `MatchLifecycle` 单一判定面）／H4＝a（加性新增 `MatchEndReason{HqZero, Concede}`）／H5＝a（`Concede` 仅 `Play` 相位可调）。
> **中途需求变更（M1-M3）**：mulligan 换牌经**特制 targeter 选择器槽位**（交互式），替代原「索引数组」形态。
> **结案（2026-10-05）**：M1＝**b**（新增专用槽位类型 `MulliganSelectSlot` ＋ `TargetSlotKind`／`TargetSlotPresentation` 加性追加 `MulliganSelect`——供前端识别"开局换牌"并播放**专属动画**）／M2＝a（`BeginMulliganAsync` 唯一换牌入口＋`MulliganDone`＝不换牌确认；索引形态取消，01 文档 S2 签名已改写）／M3＝同意（`min=0`／`max=`手牌数；换牌静默仅 `deck.shuffled`；空手牌零交互确认）。
> **02 侧加性变更登记**：见 `02-输入接口.md` 的 A6（不改既有槽位类别语义与桥接契约）。

### M1: mulligan 选择器槽位形态  Open（主问题·需求变更）
- 新要求：mulligan 换牌经**特制** targeter 选择器槽位（交互式）。
- 冲突：①01 文档 S2 冻结签名 `MulliganReplace(player, handIndexes, ct)`＝**索引数组**形态，与"交互产出＝卡引用"互斥；②02 已交付的 G5 裁决＝**不新增 `TargetSlotKind`**，而"候选＝己方手牌引用＋域判定〔仍在手牌〕"恰是既有 `HandSelectSlot` 的定义（`targeter/CONTEXT.md:93`）。
- 待裁决：a＝**复用 `HandSelectSlot`**（多选 `min=0`／`max=`手牌数；`WithSlotReferences`＝手牌快照；`WithSlotDomainValidator`＝仍在手牌；**特制**＝专用槽位名 `mulligan` ＋ 槽位参数＝该玩家；呈现＝`HandSelect`）；b＝新增专用 Kind（须改 `targeter/CONTEXT.md` 冻结词表＋槽位族＋校验分支，与 G5 冲突）。

### M2: mulligan 入口与应答语义  Open（需求变更）
- 冲突：02 确立"统一两段式预行为：每个动作对外只暴露一个 `Begin*` 入口"；01 文档 S2 对外则是 `MulliganReplace`＋`MulliganDone` 两个"参数式"入口。
- 待裁决：a（推荐）＝对外 `BeginMulliganAsync(player, ct)`（唯一 Begin；确认＝执行换牌并**自动 done**）＋ `MulliganDone(player, ct)` 保留为"不换牌直接确认"；取消＝零副作用（不换、不 done；可重发或改调 `MulliganDone`）；**索引形态取消**（同步修订 01 文档 S2 与 `requirements.md` R1 措辞）。

### M3: mulligan 选择数量与静默口径  Open（确认项）
- 待裁决：`min=0`／`max=`手牌数（空选＝不换牌、合法、构成确认）；换牌静默（不发 `card.drawn`／`card.hand.add`，仅 `deck.shuffled`）；交互本身不产生 `GameUpdates` 信号（仅留痕）。

> 背景：02 已交付（`Orc.Game.Tests` 636 全绿）；复核 `01-完整流程.md` 与 `src/Orc.Game` 实际行为，发现 4 处"文档 vs 代码"差距（见下）。

### H1: A1「Initialize 置 Mulligan」的落地口径  Open（主问题）
- 冲突：A1 授权「`Initialize` 序列＝…→置 `Mulligan`、先手回合延后到双方确认」；但契约「受控随改（迁移非删除、总数不减）」面对的真实影响面＝测试中 `.Initialize(` 共 **62 个文件、约 400 处**（`tests/Orc.Game.Tests` 全量）。缺省改相位后这些用例普遍因「回合未开始／相位门禁」失败，须逐处补「双方确认」。
- 待裁决：a＝默认启用＋约 400 处机械随改；b＝**开关化缺省关**（`MatchOptions` 增 `Mulligan`，缺省 false＝旧序列逐字不变；开启＝完整 mulligan 流程）；c＝双入口（`Initialize` 不变＋新增 `InitializeWithMulliganAsync`）。
- 影响：选 b/c 需把 A1 登记**收敛**为「可选装配／新增入口」，并同步修订 `requirements.md` Constraints 措辞。

### H2: `MatchPhase` 与既有 `MatchState` 的关系  Open
- 事实：既有 `MatchState` 为**三态**（`Preparing=0`／`InProgress=1`／`Ended=2`，`Match/MatchState.cs`）；`MatchPhase.Ended` 与之语义重复（双真源风险）；I1 记载的"与既有二态并存"与代码不符。
- 待裁决：`MatchState` **加性追加 `Mulligan`**（置于末尾、不改既有值 0/1/2）＋ `Match.Phase` 为**派生投影**（单真源） vs 新增独立 `MatchPhase` 存储 vs 取消该类型（改 R1 措辞）。

### H3: 相位门禁的承载  Open
- 事实：「仅进行态可用」有 **4 处独立载体**：`Match.cs`（`State` 检查）、`MatchLifecycle.IsEnded`（各 Manager 消费：Play/Command/Turn/Targeter）、`MatchCardService.cs:527`、`RandomService.cs:194`（各自 `state == MatchState.InProgress`）。
- 待裁决：统一到 `MatchLifecycle` 单一判定面（4 处改读） vs 各点各自扩展（易漏出新相位漏洞）。

### H4: 终局原因枚举  Open
- 事实：`MatchLifecycle.End(Player)` 无原因字段；HQ≤0 经 `Players/Hq.cs:219-225` 调用（`lifecycle.End(winner)`）。
- 待裁决：加性新增 `MatchEndReason{HqZero, Concede}`（`End(winner, reason)` 重载） vs 不加（S3 明确要求加；建议加）。

### H5: 认输门禁口径  Open
- 待裁决：`Concede` 仅 `Play` 相位可调（mulligan 期间拒绝，与 S2「mulligan 期间认输一律拒绝」一致）——确认。

## 受控变更登记（已授权）

### A1: 初始化序列受控变更  Completed
- a（已授权）：2A 冻结「初始化序列＝…→先手回合开始序列→置进行」正式变更为"置 `Mulligan`、先手回合延后到双方确认后执行"；旧→新差异须在实施汇报中逐条登记（**已落地**：见 `01-完整流程.md` 的 A1 序列差异登记表）。

## 三文档交付状态（2026-10-05）

| 序 | 文档 | 状态 | 证据 |
|---|---|---|---|
| 1 | `03-hook定义.md` | **Done** | `dotnet build -warnaserror` 0 警告；`Orc.Game.Tests` 621 |
| 2 | `02-输入接口.md` | **Done** | 0 警告；`Orc.Game.Tests` 636（+15，零删除） |
| 3 | `01-完整流程.md` | **Done** | 0 警告；`Orc.Game.Tests` **647**（+11，零删除）；端到端"建局→mulligan→回合循环→终局"全绿 |

- 全部 grill 未决项已结案（I1-I5／A1／G1-G7／H1-H5／M1-M3）；`requirements.md` 已同步修订（R1 验收措辞：mulligan 入口改为交互式特制槽位）。
- 行号同步（G6 义务）已两次落地（02 后、01 后）：`03-hook定义.md` 与 `src/Orc.Game/Output/GameHooksJson.cs` 保持一致。

## 二轮结案（`03-hook定义.md` 实现 grill · 2026-10-05）  Completed

> 触发：对 `03-hook定义.md` 执行实现 grill，逐条比对代码基线，发现 8 处「文档断言 ↔ 代码实际」矛盾（N1/N2 可致 S6 测试必红）。已全部结案并落地（验证：`-warnaserror` 0 警告 / 0 错误；`dotnet test` 全绿）。

### N1: S6 判定器装配断言与真实注册面不符  Completed
- a：断言口径改为「4 条验证类在 `Match.Initialize` 固定注册段恒可达；2 条示范类经 `judicatorAssembly` 外部装配段、默认不可达（fail-fast）」。S3-3 同步更正为 6 条。

### N2: S7「27 项」权威来源与清单行不符  Completed
- a：以 `docs/kards-diy-可参考语料报告.md` §2.1（`invariants.json:96-124`）为唯一权威，S7 表重排为 27 行（补齐 `friendlyAttacked`/`attacked`/`afterAttackHQ`；剔除 `death`/`order`/`counter`/`counterSet`/`movedToFrontline`/`damaged`）；来源引用同步更正。结论分布＝6 已具备 / 13 后置 / 8 不做。

### N3: S1「根 `CONTEXT.md`」目标歧义  Completed
- a：四词条写入根 `d:\OrC-KSD\CONTEXT.md`（接入点/观察面/扩展点）；`Hook` 只加"指引"词条指向 `.ams/context/orc-engine/CONTEXT.md:103`，不重定义（无双真源）。

### N4: S2「13 个发射助手」与 17 信号映射不对齐  Completed
- a：清单只收 17 条 `GameUpdates` 信号；`EmitCardPlaced`/`card.placed` 作"内核信号转发"脚注、不入清单；助手计数更正为 **12（信号）+1（内核转发）**。

### N5: S3 权威引用「文件名≠类型名」/归属错  Completed
- a：八类引用全部改为真实类型·成员；`ExportAuditChainJson` 归属 `LogicEngine`；`TakeSegments`/`TryTakeSegment` 仅归观察面（第 8 类），从第 7 类移除。

### N6: S5 导出器无法复用 `src/Orc/Output` 地基  Completed
- a：`Orc.Game/Output/GameHooksJson.cs` 自持 `Utf8JsonWriter` 手写写出（定深结构、无递归），不触 `src/Orc`。

### N7: S6「新增测试文件」与既有 `GameHooksTests.cs` 关系未定  Completed
- a：扩展既有 `tests/Orc.Game.Tests/GameHooksTests.cs`（迁移非删除、总数不减）；新增 7 组一致性断言。

### N8: S7 结论枚举含 R3 未授权值「部分」  Completed
- b：保持 R3 三值枚举（已具备/后置/不做）；原表 `counter`/`counterSet` 不属权威 27 项、已剔除；未回改 `requirements.md`。
