# Kb·爆牌独立化与术语更名 — 实现记录

> 委托：CodeImplementation（Kb，「判定器收编（A+B）」链——file 级，useGrill）；依据：《判定器机制/收编（A+B）—方案.md》、《收编（A+B）-基线调研报告.md》§二·B12、《需求文档.md》（本目录）、《实现记录-K0.md》。
> 交付物：爆牌输出独立信号（修改 2 核心）＋全链登记面同步（受控适配）＋测试随改与新增（1）＋术语更名（域内）＋本记录。

## 一、交付概览

**核心机制改动（2）**：

| # | 文件 | 内容 |
|---|---|---|
| 1 | `src/Orc.Game/GameUpdates.cs` | 新增信号常量 `card.burned`（合计 17→18 条）＋发射助手 `EmitCardBurned`（载荷 `{ Card, Player }`） |
| 2 | `src/Orc.Game/HandLimitBurn.cs` | `BurnAsync` 输出改独立信号（销毁 → `card.burned`；不复用 `card.discarded`）；`DestroyAndEmitDiscardedAsync` 保留为弃置路径专用 |

**受控适配（src 5 文件；均为注释/登记面最小同步，逐条见 §四）**：

| # | 文件 | 内容 |
|---|---|---|
| 3 | `src/Orc.Game/Managers/PlayerManager.cs` | 注释随改（爆牌语义＋共享关系更新表述）；**代码零改动**（调用点经共享单元，注入面不变） |
| 4 | `src/Orc.Game/Match/MatchCardService.cs` | 注释随改（爆牌语义）；**代码零改动** |
| 5 | `src/Orc.Game/GameHooks.cs` | 登记面：信号转发 `CardBurned`＋`Signals` 18 条 |
| 6 | `src/Orc.Game/Output/GameHooksJson.cs` | 登记面：权威描述表 18 条＋`card.burned` 条目＋两处"烧牌"字样修正（K0 挂账①）＋触碰行锚点校准 |
| 7 | `src/Orc.Game/Players/Player.cs` | HandLimit 常量注释（最小受控适配——信号失真修正＋更名，见 §四·G） |

**测试（5 文件；随改＋1 新增）**：

| # | 文件 | 内容 |
|---|---|---|
| 8 | `tests/Orc.Game.Tests/HandLimitBurnUnificationTests.cs` | 并排对照断言更新（不发 discarded、发 burned）＋**新增监听消费用例 1**（2→3 用例） |
| 9 | `tests/Orc.Game.Tests/G7DiscardScenarioTests.cs` | ⑥段随改（观测 burned＋反向断言；用例名同步） |
| 10 | `tests/Orc.Game.Tests/CardServiceTests.cs` | `Scenario1_Burn` 随改（同上模式） |
| 11 | `tests/Orc.Game.Tests/GameHooksTests.cs` | 17→18 全链四处（反射核对／发射助手／JSON／注释） |
| 12 | `tests/Orc.Game.Tests/MatchHistoryReadTests.cs` | 信号契约清单 17→18（元素面＋字面值） |

`git diff --stat`（8 个此前干净文件）：221 insertions(+), 93 deletions(-)；另 4 文件（GameHooks／GameHooksJson／GameHooksTests／CardServiceTests）在 K1-K3 遗留改动之上叠加本单最小改动（逐条申报见 §四）。

**零修改清单（申报）**：禁编域全套零触碰（见 §六）；弃置路径 `PlayerManager.DiscardCardAsync` 代码/信号/次序完全不变；`MatchCardService.PlaceToHandAsync` 代码不变；`HandLimitBurn.IsAtLimit`／`emitDrawn` 参数化签名不变。

## 二、信号名定型申报

- **定型**：`card.burned`（沿用 Q3(c) 建议方向落地，无偏离）。
- **形态**：总线信号面＝`GameUpdates` 新常量（第 18 条）＋发射助手 `EmitCardBurned(engine, card, player, ct)`；任意监听方可经既有触发器 hooks 机制挂载监听（新增用例实证）；**不新增其它机制件**（无专用 Trigger 实例/专用机制）。
- **载荷**：`{ Card, Player }`（被爆卡实例在前、玩家在后——对齐"卡的动作事件"族惯例：`card.played`／`card.hand.add`／`card.discarded`／`card.load`）。
- **语义（与 discarded 对齐）**：恰一次；销毁先于信号（观察者所见即终态）；不携带原因/路径（两路差异仅由 `emitDrawn` 与 `card.drawn` 的有无体现）。
- **发射点**：`HandLimitBurn.BurnAsync`（爆牌两路唯一汇聚点）；两路契约：①DrawCard 满手＝`card.drawn` → 销毁 → `card.burned`；②PlaceToHand 满手＝销毁 → `card.burned`。

## 三、核心机制改动说明（解耦落点）

1. **爆牌输出替换（恰一处、两路径一致）**：`HandLimitBurn.BurnAsync` 尾部由"经 `DestroyAndEmitDiscardedAsync`（销毁＋discarded）"改为"`engine.DestroyCard(card)` → `GameUpdates.EmitCardBurned(...)`"——不复用 `card.discarded`（爆牌不走弃牌路线）。
2. **共享关系更新表述（替代 K0 时代表述）**：弃置＝销毁＋`card.discarded`；爆牌＝销毁＋`card.burned`；**两者仅共享「销毁」步骤**（引擎既有销毁、`card.destroyed` 保留）；信号发射各自独立（不复用、不互发）；爆牌不经弃置动作（语义与信号双解耦）。
3. **弃置路径完全不动**：`DiscardCardAsync`（移除 → 销毁 → `card.discarded`）代码零改动、行为与信号不变；`DestroyAndEmitDiscardedAsync` 保留为弃置路径专用原语（注释更新）。
4. **保留项**：抽牌路径 `card.drawn` 语义（"算被抽到"）与 `emitDrawn` 参数化不变；销毁路径（`card.destroyed`）不变；`CardPlaceStatus.Burned` 等英文标识全部不变。

## 四、受控适配清单（逐条·旧→新对照）

### A. `src/Orc.Game/GameUpdates.cs`（核心改动＋注释）

| # | 位置 | 性质 | 旧 → 新 |
|---|---|---|---|
| A1 | 头注释合计行 | 计数＋叙述 | "合计 17 条" → "、Kb 爆牌一项（`card.burned`）——合计 18 条" |
| A2 | 头注释 G7 段 | 调用点解耦＋Kb 段追加 | "调用点＝弃置动作与烧牌路径〔共享销毁原语与信号〕" → "调用点＝弃置动作"；追加 Kb 段（`card.burned` 新增叙述） |
| A3 | `CardDiscarded` 注释 | 语义更新 | "烧牌＝弃置的自动路径、复用本信号" → "弃置路径专属——爆牌不走弃牌路线、经独立信号 CardBurned" |
| A4 | **新增** `CardBurned` 常量＋`EmitCardBurned` 助手 | 加性 | （新） |
| A5 | `EmitCardDiscarded` 注释 | 更新 | "弃置动作与烧牌路径共用" → "弃置路径专用" |

### B. `src/Orc.Game/HandLimitBurn.cs`（核心改动）

| # | 位置 | 性质 | 旧 → 新 |
|---|---|---|---|
| B1 | 类头注释 | 重写 | "烧牌共享单元……共享处置链与信号" → "爆牌共享单元……输出＝独立信号 card.burned；共享关系（Kb 更新）：仅共享「销毁」步骤、信号各自独立" |
| B2 | `IsAtLimit` 注释 | 更名 | "烧牌裁决前置" → "爆牌裁决前置" |
| B3 | `DestroyAndEmitDiscardedAsync` 注释 | 语义收窄 | "全库单一实现——弃置动作与烧牌共享的处置链" → "弃置路径专用（Kb 解耦后）……爆牌路径已解耦" |
| B4 | `BurnAsync` 注释＋实现 | **行为变更（恰一处）** | "两路均……card.discarded 恰一次；经 DestroyAndEmitDiscardedAsync" → "两路均……card.burned 恰一次；`engine.DestroyCard` → `EmitCardBurned`"；`emitDrawn` 参数化保留 |

### C. `src/Orc.Game/Managers/PlayerManager.cs`（注释随改；代码零改动）

| # | 位置 | 旧 → 新 |
|---|---|---|
| C1 | 文件头注释 | "HandLimit 烧牌裁决……发 card.discarded" → "HandLimit 爆牌裁决……发 card.burned"；"弃置与烧牌共享「销毁」原语与 card.discarded 信号" → "弃置与爆牌仅共享「销毁」步骤；信号发射各自独立（弃置＝card.discarded、爆牌＝card.burned）" |
| C2 | `DrawCard` 注释 | "KARDS 烧牌语义……card.discarded 恰一次……被烧卡引用、烧掉的牌" → "KARDS 爆牌语义……card.burned 恰一次……被爆卡引用、爆掉的牌" |
| C3 | `DrawCard` 局部注释 | "drawn → 销毁 → discarded——经共享烧牌单元" → "drawn → 销毁 → burned——经共享爆牌单元〔K0·B12 统一；Kb 独立信号输出〕" |

### D. `src/Orc.Game/Match/MatchCardService.cs`（注释随改；代码零改动）

| # | 位置 | 旧 → 新 |
|---|---|---|
| D1 | 头注释② | "满手烧牌裁决" → "满手爆牌裁决" |
| D2 | 头注释⑦ | "满手烧牌＝销毁→card.discarded 恰一次（生成路径烧牌口径）" → "满手爆牌＝销毁→card.burned 恰一次（生成路径爆牌口径）" |
| D3 | `CardPlaceStatus` 枚举注释 | "成功 / 烧牌 / 失败" → "成功 / 爆牌 / 失败"；Burned 成员注释："烧牌……KARDS 烧牌语义——不经手牌（直烧）……card.discarded 恰一次" → "爆牌……KARDS 爆牌语义——不经手牌（直爆）……card.burned 恰一次" |
| D4 | 失败原因/结果对象注释（5 处） | "成功/烧牌＝null"、"放置/烧牌/被回收"、"烧牌为成功语义"、"创建「烧牌」结果" → 对应"爆牌"版本 |
| D5 | `PlaceToHandAsync` 注释＋局部注释 | "满手＝烧牌裁决（直烧……card.discarded 恰一次）" → "满手＝爆牌裁决（直爆……card.burned 恰一次）"；"经共享烧牌单元" → "经共享爆牌单元〔K0·B12 统一；Kb 独立信号输出〕" |
| D6 | 复合调用注释 | "烧牌为成功语义" → "爆牌为成功语义" |

### E. `src/Orc.Game/GameHooks.cs`（登记面同步）

| # | 位置 | 旧 → 新 |
|---|---|---|
| E1 | 信号组注释 | "对外信号（17 条）" → "（18 条）" |
| E2 | **新增** `CardBurned` 转发常量 | （新；`GameUpdates.CardBurned`） |
| E3 | `Signals` 全量注释 | "17 条" → "18 条" |
| E4 | `Signals` 列表 | `CardLoad, CardHandAdd, CardDiscarded,` → `……, CardDiscarded, CardBurned,`（稳定序＝定义序） |

### F. `src/Orc.Game/Output/GameHooksJson.cs`（登记面同步＋K0 挂账处置）

| # | 位置 | 旧 → 新 |
|---|---|---|
| F1 | 描述表头注释 | "17 条信号的权威描述表" → "18 条信号的权威描述表" |
| F2 | `CardDrawn` 条目发射点 | "Managers/PlayerManager.cs:156·DrawCard（烧牌）" → "HandLimitBurn.cs:66·BurnAsync（爆牌路径）"；常规锚点 "…:162·DrawCard（常规）" → "…:163·DrawCard（常规）"（触碰行校准并入） |
| F3 | `CardDiscarded` 条目发射点 | "Match/MatchCardService.cs:215·PlaceToHandAsync（烧牌）"、"Managers/PlayerManager.cs:255·DestroyAndEmitDiscardedAsync" → "HandLimitBurn.cs:47·DestroyAndEmitDiscardedAsync（弃置处置链）"（解耦后唯一发射点；旧锚点已失效/K0 申报在案） |
| F4 | **新增** `CardBurned` 条目 | `new SignalEntry(GameHooks.CardBurned, CardPlayerPayload, ["HandLimitBurn.cs:70·BurnAsync（爆牌共享单元）"], [])` |

### G. `src/Orc.Game/Players/Player.cs`（最小受控适配——申报）

| # | 位置 | 旧 → 新 |
|---|---|---|
| G1 | `HandLimit` 常量注释 | "直烧（移除＋销毁＋`card.discarded` 信号——KARDS 烧牌语义）" → "直爆（销毁＋`card.burned` 信号——KARDS 爆牌语义）" |

- 申报理由：该注释描述本单机制语义（满手裁决信号），本单变更后旧文失真（`card.discarded` 已不是爆牌输出）；且含"烧"字样。经隔离复核：文件无在途改动、不属禁编域，按"共享文件最小改造＋逐条申报"处理（1 行，信号失真修正＋更名；连带删除不准确的"移除＋"描述）。

### H. `tests/Orc.Game.Tests/HandLimitBurnUnificationTests.cs`（并排对照随改＋新增）

| # | 性质 | 旧 → 新 |
|---|---|---|
| H1 | 类注释 | "烧牌统一……先于 card.discarded、card.discarded 恰一次……被烧卡终态" → "爆牌统一（Kb 爆牌独立化与术语更名）……先于 card.burned、card.burned 恰一次、card.discarded 零次（反向锁定）……被爆卡终态"＋附监听消费说明 |
| H2 | 段①断言 | `Count(CardDiscarded)==1` → `Count(CardBurned)==1`；**新增** `Count(CardDiscarded)==0`（反向）；`drawPathDiscarded`（Single 过滤）→ `drawPathBurned` |
| H3 | 段②断言 | 同上（`placePathBurned`） |
| H4 | 并排序列 | `[drawn, destroyed, discarded]` → `[drawn, destroyed, burned]`；`[destroyed, discarded]` → `[destroyed, burned]`；`KeyBurnSignals` 保留 `CardDiscarded` 过滤项（反向锁定：若出现 discarded 将破坏预期序列） |
| H5 | 未满用例 | 新增 `Count(CardBurned)==0`；注释"零销毁/弃置"→"零销毁/爆牌" |
| H6 | **新增用例** | `Burned_Signal_Is_Consumable_Via_Existing_Hooks_Mechanism`——独立信号经既有触发器 hooks 机制挂载可达（自写被动触发器＋归属过滤，收到恰一次、载荷＝被爆卡）；附 `BurnedWatchHandler`／`BurnedWatchView`（仿 G7 ⑦ 监听模式） |
| H7 | using | 加 `using Orc.Cards;`（监听辅助用 Card 类型） |

### I. `tests/Orc.Game.Tests/G7DiscardScenarioTests.cs`（⑥段随改）

| # | 性质 | 旧 → 新 |
|---|---|---|
| I1 | 类注释⑥ | "⑥HandLimit 烧牌……drawn → discarded、hand.add 零次" → "⑥HandLimit 爆牌（直爆……drawn → burned、hand.add 零次；销毁先于信号；不发 discarded）" |
| I2 | 段标题 | "⑥ HandLimit 烧牌" → "⑥ HandLimit 爆牌" |
| I3 | 用例名 | `HandLimit_Burn_Draw_At_Full_Hand_Discards_Newly_Drawn_Card` → `HandLimit_Burn_Draw_At_Full_Hand_Emits_Burned_Not_Discarded`（名主张旧语义被本单推翻；申报为"测试表述"更名） |
| I4 | 断言区 | `Single(CardDiscarded)` → `Single(CardBurned)`；`Count(CardDiscarded)==1` → `Count(CardBurned)==1`＋**新增** `Count(CardDiscarded)==0`（反向）；`IndexOf(CardDiscarded)`×2（drawn 先于／destroyed 先于）→ `IndexOf(CardBurned)`×2；载荷断言随换 |
| I5 | 零改动（复核） | ⑦段弃置监听、①段弃置三路径、洗入类负向断言等不涉爆牌——**不动**（弃置路径行为不变；由其锁定） |

### J. `tests/Orc.Game.Tests/CardServiceTests.cs`（Scenario1_Burn 随改）

| # | 性质 | 旧 → 新 |
|---|---|---|
| J1 | 类注释 | "差异点（满手烧牌/……）" → "（满手爆牌/……）" |
| J2 | 用例名 | `Scenario1_Burn_On_Full_Hand_Destroyed_Then_Discarded_No_HandAdd` → `…_Destroyed_Then_Burned_No_HandAdd` |
| J3 | 断言区 | `Count(CardDiscarded)==1` → `Count(CardBurned)==1`＋**新增** `Count(CardDiscarded)==0`（反向）；`destroyedIndex/discardedIndex` → `destroyedIndex/burnedIndex`（顺序：销毁先、burned 后）；`PayloadOf(CardDiscarded)` → `PayloadOf(CardBurned)` |

### K. `tests/Orc.Game.Tests/GameHooksTests.cs`（登记面 17→18 四处）

| # | 旧 → 新 |
|---|---|
| K1 | `Assert.Equal(17, signals.Count)` → `18`（反射核对全量常量↔Signals） |
| K2 | helpers `12→13`＋新增 `[GameHooks.CardBurned] = "EmitCardBurned"`；注释"12 条经 GameUpdates.Emit* 助手"→"13 条"；"// 12 + 5 = 17" → "// 13 + 5 = 18" |
| K3 | JSON `signals` 数组 `17→18` |
| K4 | 待补层注释"17 信号或流程位白名单" → "18 信号……"（对位∈Signals 断言自动适应） |

### L. `tests/Orc.Game.Tests/MatchHistoryReadTests.cs`（契约清单 17→18）

| # | 旧 → 新 |
|---|---|
| L1 | 注释 17 条→18 条（2 处）；`signalLiterals` 新增 `GameUpdates.CardBurned`；`Assert.Equal(17, …Length)`／`Distinct().Count()` → `18`；**新增**字面值断言 `Assert.Equal("card.burned", GameUpdates.CardBurned)` |

### 更名替换清单（词根"烧"→"爆"；全域复核见 §六）

- 变体映射：**烧牌→爆牌**（机制名统一）；**直烧→直爆**（口径）；**被烧卡→被爆卡**；**烧掉的牌→爆掉的牌**；"共享烧牌单元"→"共享爆牌单元"；"KARDS 烧牌语义"→"KARDS 爆牌语义"。
- 改前命中（src＋tests）：52 行／12 文件（src 41：GameUpdates 3／HandLimitBurn 8／Player.cs 2／PlayerManager 9／MatchCardService 17／GameHooksJson 2；tests 11：G7 5／HandLimitBurnUnification 3／CardService 3）。
- 改后命中：**0**（全域搜索复核，证据 `_work/scan-report.txt`）。
- 英文标识**全部不变**：HandLimitBurn／BurnAsync／CardPlaceStatus.Burned／emitDrawn／card.burned（英文为代码契约）。

## 五、K0 挂账锚点处置说明

| 项 | 处置 |
|---|---|
| ①两处"烧牌"字样（GameHooksJson L120/L131） | **随本单修正**（→"爆牌"）✅ |
| ②其余锚点行号失真的全面校准 | **继续留待**（Prefab 冻结后统一维护；本单未扩大）✅ |
| ③凡本单必然触碰的行，校准并入 | CardDrawn／CardDiscarded 两条目行（本单注定触碰——爆牌解耦改变发射点）：锚点校准为当前真实（`HandLimitBurn.cs:66/47`；常规 drawn 行 162→163）；CardHandAdd 条目未被触碰——保持不扩大（其漂移留待）✅ |

## 六、零触碰与隔离复核

**（1）禁编域零触碰（编辑后复核，`_work/isolation-after.txt`）**：

- S-C 六文件：零触碰；DynamicEffect／Prefab 域（src/Orc/Cards/*）：零触碰（其 M 状态为并行包既有改动，非本单）；Script 域：零触碰。
- 『换牌/认输』域（Match/MatchOptions/MatchState/MatchLifecycle/CommandManager/CommandTypes/PlayManager/GameEntryPoints/TargetSlots/HandSelectSlot、MulliganManager/MatchPhase/MulliganSelectSlot）：零触碰。
  - 附带复核：GameEntryPoints.cs 的 `DrawCard`（"Task<CardBase>（card.drawn）"）与 `DiscardCardAsync`（"Task（card.discarded）"）条目描述在 Kb 后仍正确（爆牌发 drawn；弃置发 discarded）——**无需变更**。
- 在途活跃包域（Cards/Data、EffectSystem、RetriggerSystem、CardLibrary、Match.cs、KeywordComponents、Prefabs 等）：零触碰。
- 本单 12 改动文件与并行包写入面零交集；未删除任何文件。

**（2）"烧"字样全域搜索复核（收口）**：

- src＋tests：**0 命中**（scoped 全文扫描；含注释/断言/登记面）。
- 域外留存（按 Q2(c) 裁定**维持不动**，零触碰申报）：`docs/kards-diy-游戏流程分析报告.md:72`（KARDS 原版分析历史文本"烧牌进弃牌堆"）；`docs/初始设计/kards官方卡牌.json`＋`outputs/*`（卡名"燃烧的天空/燃烧弹"——非机制术语，不涉）；`.ams-docs` 历史记录（如实记录旧称）。

**（3）`card.discarded` 观测点收口复核（Q4(c)）**：全库清点 117 行引用（`_work/scan-discarded-report.txt`）——按三类分流：

- 改为观测 burned 的（爆牌路径）：H2/H3/H4/H5、I4、J3 ✅
- 保持不动的（弃置路径／负向断言／无关）：G7①/⑦弃置用例、G7HandOperationsTests、C2ReferenceAndFailureTests（回收负向）、CardServiceTests L455/L655（转换/回收负向）、TurnPhaseEffectTests（效果自持语义）✅
- 契约清单类：F3/K2/L1（同步 18 条全链）✅

## 七、在途痕迹检查结果（编辑前）

- **检查面**：本单编辑域目标文件＋与编辑域直接相关的在途包文件（GameUpdates／GameHooks*／HandLimitBurn／PlayerManager／MatchCardService 等）。
- **结果**：HandLimitBurn／GameUpdates／PlayerManager／MatchCardService／Player／HandLimitBurnUnificationTests／G7DiscardScenarioTests／MatchHistoryReadTests 开工时**干净**（非 M、非半成品）；GameHooks／GameHooksJson／GameHooksTests／CardServiceTests 处于 K1-K3 **已完成形态**（完整、可构建、测试全绿——非施工中）。
- **构建输出**：无"非本单、非已知包袱的新错误"；已知包袱＝sample 交互会话锁定（K0 起在途）＋xUnit2013×3（K4 遗留）。
- **结论**：无在途半成品冲突；未触发"停手申报"情形。**"最小增量＋逐条申报"推进。**

## 八、验证记录

### 8.1 构建（`-t:Rebuild`；非 sample 面口径）

| 范围 | 结果 | 证据 |
|---|---|---|
| 改造前基线（本单编辑前） | 非 sample 面 8 工程全数成功——**0 CS 警告 0 CS 错误**；既有分析器警告 xUnit2013×3（K4 遗留，MatchOpeningConfigTests.cs L142/143/150）；sample 面＝在途包袱（4 错误） | `_work/build-baseline.log`／`baseline-analysis.txt` |
| 全 sln Rebuild（改后） | **与基线完全同构**：非 sample 面 8 工程全数成功（`Orc`／`Orc.Lua`／`Orc.Game`／`Orc.Script`／四个 tests）——0 CS 诊断；xUnit2013×3（非本单）；sample 面 4 错误（MSB3027/MSB3021×2）＋MSB3061×8/MSB3026×40——`Orc.dll`/`Orc.Game.dll` 复制至 sample 输出被**在途交互会话**（`dotnet run --project samples/Orc.Game.Sample -- --interactive`，PID 38632）锁定 | `_work/build-rebuild-kb-1.log`／`rebuild-kb-analysis.txt` |
| 增量构建（src／tests 逐项目） | `Orc.Game` 0/0；`Orc.Game.Tests` 0 错误（警告＝xUnit2013×3 既有） | `_work/build-game-1.log`／`build-gametests-1.log` |

> sample 处置：沿用 K0/K4 口径——**非 sample 面 0/0＋samples 在途包袱申报留待**（不阻断）。无法顺带纳入（会话仍存活、锁定持续）。

### 8.2 全量回归（四套件，`--no-build`，同一快照）

| 套件 | K4 收尾基线 | 本单 | 实测 |
|---|---|---|---|
| Orc.Tests | 298 | — | **298 通过 / 0 失败** |
| Orc.Lua.Tests | 157 | — | **157 通过 / 0 失败** |
| Orc.Game.Tests | 714 | +1（新增监听消费用例） | **715 通过 / 0 失败** |
| Orc.Script.Tests | 9 | — | **9 通过 / 0 失败** |
| 合计 | 1178 | +1 | **1179 通过 / 0 失败 / 0 跳过** |

- 改造前基线说明：本单未在编辑前重跑四套件；基线口径＝K4 收尾 1178 全绿（K4 记录在案）——本单除测试断言更新（数量不变）与 1 新增用例外，未改动任何测试逻辑；计数完全吻合（714+1=715）。
- 专项复跑：爆牌/弃置/卡片服务三组 **40 通过 / 0 失败**（`test-burn-core-1.log`）；hooks/历史/弃置操作四组 **40 通过 / 0 失败**（`test-hooks-core-1.log`）；`HandLimitBurnUnificationTests` 单类 **3/3**（含新用例 `Burned_Signal_Is_Consumable_Via_Existing_Hooks_Mechanism`，`test-burn-class-kb.log`）。
- 覆盖勾稽（q4(c) 口径）：随改清单逐条可回指（§四·H/I/J/K/L）；无断言丢失（均"替换＋反向新增"）；覆盖不减少（＋1）。

### 8.3 验收口径对照

| # | 验收点 | 结果 | 证据 |
|---|---|---|---|
| ① | 爆牌不发 discarded、发独立信号（名称定型申报） | ✅ | §二／§三；H2-H5/I4/J3 正反双断言 |
| ② | 抽牌路径 card.drawn 语义保留（emitDrawn 参数化不变） | ✅ | B4（签名不变、drawn 发射保留）；H2 段① |
| ③ | 销毁路径保留（card.destroyed） | ✅ | B4（engine.DestroyCard 保留）；H2-H5 顺序断言 |
| ④ | HandLimitBurnUnificationTests 等随改后全绿（并排对照更新） | ✅ | §8.2（3/3；序列 `[drawn,destroyed,burned]`/`[destroyed,burned]`） |
| ⑤ | 术语更名（本单域内零残留） | ✅ | §六（52→0；域外留存已申报） |
| ⑥ | 构建非 sample 面 0 警告 0 错误 | ✅（口径同 K4：CS 诊断＝0；xUnit2013 既有×3 申报） | §8.1 |
| ⑦ | 全量回归不破坏 | ✅ | §8.2（1179/0） |
| ⑧ | 实现记录落盘 | ✅ | 本文件 |

## 九、边界与非验收项说明

- 判定器面（GameHooksJson 判定器清单等）本单零触碰（K1-K3 范围）；`GameHooksJson` 仅信号面条目同步。
- 行号锚点的**全面校准**不属本单（K0 挂账②；留待 Prefab 冻结后统一维护）——本单仅触碰行校准并入＋新增条目。
- G7 ⑦ 弃置监听用例、G7HandOperationsTests 保持零修改（弃置路径不变的锁定锚）。
- `docs/`与 `.ams-docs` 历史文本、卡名"燃烧*"：维持不动（非机制术语/历史如实记录）。
- 空卡组抽牌、HandLimit 配置化等：沿用既有语义（非本单范围）。

## 附录：过程日志留档（`_work/`）

- 构建：`build-baseline.log`（改造前基线）、`build-rebuild-kb-1.log`（改后全 sln）、`build-game-1.log`／`build-gametests-1.log`（增量）、`baseline-analysis.txt`／`rebuild-kb-analysis.txt`（归因）。
- 回归：`test-burn-core-1.log`／`test-hooks-core-1.log`／`test-burn-class-kb.log`（专项）、`test-full-{orctests,luatests,gametests,scripttests}-kb.log`（四套件）。
- 搜索复核：`scan-report.txt`（"烧"52→0／card.burned 33／CardBurned 27）、`scan-17-report.txt`（17 强绑定面）、`scan-discarded-report.txt`（117 行引用清点）、`scan-more-report.txt`（samples/scripts/docs/outputs 复核）。
- 对照与留档：`kb-full-diff.txt`（12 文件全量 diff）、`diffstat-kb-1.txt`、`line-map.txt`（锚点行号）、`isolation-after.txt`（编辑后复核）。
- 脚本：`analyze_build.py`／`scan_burn.py`／`scan_17.py`／`scan_discarded.py`／`scan_more.py`／`scan_lines.py`／`tail_log.py`／`isolation_check.py`／`diffcheck.py`／`check_ctx.py`／`scan_trigger.py`。
