# 收编（A+B）— 基线调研报告（现状与切割建议）

> 来源：CodeAnalysis 委托 aa7f3229（子 Agent 5b0160d4#2），2026-10-04。报告经汇报承载、由功能面管理者誊录落盘。
> 用途：为『判定器收编（A+B 档）』设计提供现状基线（A 类×J2 后形态；B 类×配置面；切割建议；编辑域风险）。
> 快照声明：分析期间工作区存在在途活跃编辑（见 §四）：CommandManager.cs 自 1541→1567 行、Match.cs 自 665→776 行漂移；行号为会话最终读取时点值（搜索＋直读双重核对；标 ≈ 者为单次核对、±2 行容差）。

## 〇、J2 后机制承接面（收编落点基线）

1. 内核判定器面（src/Orc/Core/）：Judicator.cs（基类＋`Judicator<TDelegate>` 强类型适配）；ValidationJudicator.cs（强类型契约 ValidationJudicatorLogic L11；基类 L24——载荷 [refs, subject]、输出 [ValidationVerdict]、允许注入对局级只读设施）；JudicatorBinding.cs（FromRegistration L27 / FromStandalone L40 / InvokeValidation L57）；JudicatorRegistry.cs（Register L30 / Resolve L53 / Invoke L74 / RegisterModing L90 / RegisterModing<T> L118 / UnregisterModing L144）。
2. Trigger 接入（src/Orc/Core/Trigger.cs）：Validate L300 / EvaluateValidation L312 / BindValidation L337 / InvokeAsync L368（验证调用位 L391；CollectReferences L446）。
3. 已有判定器名 6 枚（Judicators/JudicatorNames.cs）：validation.cost.check、validation.counter.use、validation.move.recheck、validation.attack.recheck、deck.top.tag、targeting.candidate.eligibility。
4. 对局装配（Match.cs）：注册表创建 L495；固定内置注册段 L499-510（费用/反制＋复验×2；复验注入只读设施并转发 CommandManager 单源面）；外部装配段 L513；解析转发 ResolveJudicatorBinding L711；CardLibrary 传参 L412；CommandManager 传参 L532。
5. CommandManager 绑定点：UnitMoveTrigger.BindValidation L138-140、UnitAttackTrigger.BindValidation L145-147（subjectProvider＝refs[0]）；独立构造路径 ResolveRecheckBinding L1402 / CreateRecheckJudicator L1408-1416。
6. GameHooks 联动面（跨包注意）：src/Orc.Game/GameHooks.cs L198-234（6 判定器名清单/内置 vs 外部）、src/Orc.Game/Output/GameHooksJson.cs L157-171（名→实现文件映射）——**新增判定器名需同步此两处**。

## 一、A 类 9 项逐项现状（J2 后）

- **A1 范围矩阵**：IsLineInRange（private L777≈）；HasAnyLineRange（private static L1460）。规则：炮/战/轰＝任意线；其余＝跨线相邻。调用点：IsLegalUnitTarget（L743）、IsLegalHqTarget（L771）→经 CollectAttackCandidates（L668 起）与 IsAttackTargetLegal（复验转发面 L1420）两条路径；执行段不重算。三处同源现状：单实现（1 处）；可用性/复验已同源（J2 转发）。附注：HasAnyLineRange（炮/战/轰）与 CountsAsBombard（炮/轰）为同一『类型组』概念子集——建议收编时合并为单一类型组配置（UnitType 五型：Infantry/Tank/Artillery/Fighter/Bomber）。
- **A2 被守护攻击资格**：CountsAsBombard（private static L1466-1467）。调用点：IsLegalUnitTarget L729、IsLegalHqTarget L759。
- **A3 轰炸机拦截**：IsBlockedByEnemyFighter（private L1496-1525）；辅助 ResolveLineOf（private L1527-1545）。调用点：IsLegalUnitTarget L736-741、IsLegalHqTarget L765-769——均经候选/复验同源路径。
- **A4 守护关系**：维护型缓存（_guardedUnits/_guardedHqs ≈L84-87）；重算 MaintainGuardState L845（总线订阅驱动全量重算：UnitPositionChanged/UnitJoined/UnitDeployed/CardDied，订阅段≈L160-175）；逐线 MaintainGuardForLine L858、MaintainHqGuard L884、IsGuardianAt≈L897。消费：IsLegalUnitTarget L729、IsLegalHqTarget L759；公开查询 IsUnitGuarded L818、IsHqGuarded(Hq) L826、IsHqGuarded(Player) L834。相邻口径双实现（遗留）：守护路径（BattleLine 索引 ±1 直算）vs GameEnvironment.AreAdjacent L118/GetAdjacentUnits L145（独立同口径）。
- **A5 推进前置**：HasLivingEnemyOnFrontLine（internal L1473-1493；J2 内部化）。调用点：ComputeMoveAvailability（≈L582）；MoveRevalidationJudicator（经注入转发）。**已单源**（可用性/复验同源）。
- **A6 目标合法性**：IsLegalUnitTarget（private L709-743）、IsLegalHqTarget（private L747-775）；统一 ref 入口 IsAttackTargetLegal（internal L1420-1441）。调用点：CollectAttackCandidates 三循环＋复验（转发）。**已单源**。六重编排：归属→存活/在场→烟幕→守护资格→拦截→范围（HQ：归属→占位槽→守护→拦截→范围）。
- **A7 复验三方法（J2 主体已收编）**：RevalidateMove/RevalidateAttack 已迁至逐点判定器（MoveRevalidationJudicator 124 行：条件 L53-124；AttackRevalidationJudicator 95 行：条件 L45-95）；IsAttackTargetLegal internal 保留为转发面。**剩余重复面（收编目标）**：判定器内【owner==current / !destroyed / CanMove·CanAttack / 被压制 / 行动费 / 位置】条件与 ComputeMoveAvailability（L575）/ ComputeAttackAvailability（L628）内联条件逐条重复——仅 A5/A6 两项已单源转发。
- **A8 反击豁免表**：CounterAttackRules.CanCounterAttack（Commanding/CounterAttackRules.cs L24-47；静态类，未判定器化）。调用点：HandleDefaultAttackDamageAsync L1262；AmbushKeywordComponent L135。
- **A9 伏击条件判定**：AmbushKeywordComponent（Cards/KeywordComponents.cs L70 起；改写判定 L100-148：资格＝CanCounterAttack L135；条件＝被攻击单位攻击有效值＞攻击者防御有效值 L141）。机制管道：注册于 AttackDamageTrigger（Mount L79-88）；默认互伤高优先级后执行（DefaultDamagePriority L73）。

## 二、B 类 4 项逐项现状

- **B10 行动费三读点**：读取表达式 4 处（ComputeMoveAvailability ≈L596、ComputeAttackAvailability ≈L650、MoveRevalidationJudicator ≈L97、AttackRevalidationJudicator ≈L88）；扣费写点 2 处（FinalizeMove L1138、FinalizeAttack L1152）。全部同一表达式 `unit.Modifiers.GetEffectiveValue(CardStatFields.OperateCost)`（G5 有效值口径，无旁路直读）。**统一读取口可行性：高**——抽单一取值点；写入点与读取点分开收。可选判定器化（moding 改写费用读取，为『费用特例』预留）。
- **B11 部署费校验（J2 已完成主项——复核）**：CostCheckJudicator（L10-29，零注入）；绑定 CostCheckTrigger L43-45；消费链：PlayManager 预打出 L96→PrePlayPointShortage；打出 L191→PlayVerificationRejected；指令 L316/L343；反制 EvaluateValidation L401→类别映射。**扣费读取仍散点**（UnitCard ≈L108、CommandCard ≈L111、CounterCard 激活 ≈L119；评估侧 EvaluateUse ≈L85-100）。完成度结论：验证侧 100%（含 moding 全局生效）；扣费侧未收（按需）。
- **B12 HandLimit 烧牌两处**：PlayerManager.DrawCard（L148-170）：满手判定≈L153；烧牌＝发 card.drawn→销毁→card.discarded（≈L155-159）；助手 DestroyAndEmitDiscardedAsync≈L247。MatchCardService.PlaceToHandAsync（L199-217）：满手判定≈L210；烧牌＝销毁→card.discarded（无 drawn/hand.add；≈L212-215）。常量：Player.HandLimit＝9（L30）。**语义差异**：DrawCard 路径『算被抽到』（发 drawn）；PlaceToHand 路径『未经手牌』（不发 drawn）；销毁+discarded 亦双重实现。**统一可行性：可行、低风险**——共享烧牌单元（IsAtLimit＋Burn(emitDrawn) 参数化），两处改调。
- **B13 开局常量三处＋配置面**：起手 4/5（Match.cs L30-31 private const→消费 L575-576）；首回合不抽（TurnManager.ShouldDrawForTurn L115）；槽上限 12（ResourceManager L15；**已配置化**＝MatchOptions.MaxPointSlots L9→Match ctor 校验≈L115-119）。配置面候选：① MatchOptions 扩展（首选；先例＝MaxPointSlots、SkipMulligan）；② Match ctor 直参；③ 规则对象/判定器（重，不建议先行）。

## 三、A 类收编切割建议（C1-C8 语义单元表）

两种形态（可混合）：**形态①转发壳**（J2 先例：判定器薄壳＋注入转发 CommandManager 读取面；收益＝可寻址/moding 通道＋调用点单源；规则仍私有）；**形态②规则外提**（纯规则移入判定器/规则单元、CommandManager 内联删除；收益＝重复消除彻底；成本高）。

- **C1 combat.target.legal**：（attacker:UnitCard, targetRef:Ref<Entity>）→bool；调用点＝CollectAttackCandidates＋IsAttackTargetLegal（＋可选执行前）；统一 A1/A2/A3/A6 对外形状（组合判定器）。
- **C2 combat.range**（或并入 C1）：（UnitCard, Slot）→bool；范围矩阵单源＋类型组。
- **C3 combat.guard.eligibility**：（UnitCard attacker）→bool（炮/轰）；守护两处；类型组与 C2 共表。
- **C4 combat.interception**：（UnitCard, Slot, bool targetIsFighter）→bool；拦截两处。
- **C5 combat.counter.eligibility**：（UnitCard attacker, UnitCard target）→bool；默认互伤 L1262＋伏击资格 L135；A8 单源＋A9 资格侧。
- **C6 combat.ambush.condition**：（UnitCard self, UnitCard attacker）→bool；AmbushKeywordComponent；伏击条件单源。
- **C7 move.recheck / attack.recheck**（已存在）：**补全**——抽共享 leg 条件（owner/can/suppressed/cost）＋move/attack 上下文规则，可用性与判定器同调（消除 A7 剩余重复）。
- **C8 move.frontline-enemy**（或保留转发）：（Player）→bool；A5 已单源，按需提升。
- 命名域：A 类 combat.*/move.*（与 validation.* 并列）；B 类若判定器化用 economy.*；均入 JudicatorNames（冻结契约）＋**同步 GameHooks/GameHooksJson 清单**。
- **A7 消除重复目标结构**：可用性（ComputeXAvailability）与复验（XRevalidationJudicator）→共享『leg 资格』＋『上下文规则』两层；候选枚举（CollectAttackCandidates/移动候选）保留于 CommandManager、经 C1 过滤。

## 四、编辑域风险评估（关键调度约束）

在途活跃工作包（会话实测；已知四包＋一个未列包）：

1. **[未列] 换牌/认输（A1·R1）——活跃编辑中**：新文件 Managers/MulliganManager.cs（290 行）、Match/MatchPhase.cs、Targeting/MulliganSelectSlot.cs；修改 **Match.cs（665→776）、MatchOptions.cs（SkipMulligan）、MatchState.cs（Mulligan=3）、MatchLifecycle.cs（IsActionAllowed）、CommandManager.cs（门禁 IsActionAllowed：L260/L328/L388/L443）、CommandTypes.cs、PlayManager.cs（PhaseBlocked）、GameEntryPoints.cs、TargetSlots/HandSelectSlot**。→ **与 K1/K2（CommandManager）与 K3（Match/MatchOptions/TurnManager）直接重叠。处置：等该包冻结后再发 K 链主件**。
2. **Prefab（卡牌预制）**：src/Orc/Cards/Prefabs.cs、src/Orc.Game/Cards/EffectSystem.cs、GameHooks.cs/Output/GameHooksJson.cs。→ 主体零重叠；**跨包同步点**：GameHooks.cs L198-234 与 GameHooksJson.cs L157-171 判定器名/实现映射。
3. **S-C 审查链**：src/Orc/Core/{Identifiers,HookRegistry,TriggerMetadata,Orchestration}.cs＋tests——零重叠（禁编）。
4. **DynamicEffect**：src/Orc/Cards/DynamicEffect.cs——零重叠。
5. **Script**：src/Orc.Script / Orc.Script.Tests——零重叠。
6. 热文件清单：CommandManager.cs（高）、Match.cs（高）、MatchOptions.cs（高）、PlayManager.cs（中）、KeywordComponents.cs（本链 A9 目标；未见他包迹象）、JudicatorNames/GameHooks（中）。

验收锚建议：行为零变化以 tests/Orc.Game.Tests/{CommandRules,CommandCombat,CommandFrontLine,CommandSystem,CommandPoint,CommandLockIn}Tests.cs 全绿；moding/改写专项仿 JudicatorValidationTests 模式；K3 配置项仿 CommandPointTests＋ResourceManager 先例。

## 五、关键定位速查（快照行号）

- CommandManager.cs（1567 行）：绑定段 L138-147｜GetCommandAvailability L534｜ComputeIneligibility L546｜ComputeMoveAvailability L575｜ComputeAttackAvailability L628｜CollectAttackCandidates L668｜IsLegalUnitTarget L709（守护729/拦截736/范围743）｜IsLegalHqTarget L747（守护759/拦截765/范围771）｜IsLineInRange L777｜IsUnitGuarded L818｜IsHqGuarded L826/L834｜MaintainGuardState L845｜MaintainGuardForLine L858｜MaintainHqGuard L884｜IsGuardianAt≈L897｜HandleCommandFlowAsync≈L976｜DispatchSelectedAsync≈L1031｜DispatchMoveAsync≈L1065｜DispatchAttackAsync≈L1104｜FinalizeMove L1133（扣费1138）｜FinalizeAttack L1147（扣费1152）｜HandleUnitMoveAsync≈L1164｜HandleUnitAttackAsync≈L1185｜HandleDefaultAttackDamageAsync L1231（反击表1262）｜ProcessDeathAsync≈L1317｜ResolveRecheckBinding L1402｜CreateRecheckJudicator L1408-1416｜IsAttackTargetLegal L1420｜EnemyOf L1443｜HasUnitType L1456｜HasAnyLineRange L1460｜CountsAsBombard L1466｜HasLivingEnemyOnFrontLine L1473｜IsBlockedByEnemyFighter L1496｜ResolveLineOf L1527｜IsDead L1547。
- Match.cs（776 行）：OpeningHandSize L30-31｜起手装载 L575-576｜_judicators L495｜内置注册 L499-510｜外部段 L513｜CommandManager 创建≈L532｜ResolveJudicatorBinding L711。
- Judicators/：CostCheckJudicator L10-29｜CounterUseJudicator L11-29｜MoveRevalidationJudicator（124 行）｜AttackRevalidationJudicator（95 行）｜BuiltInValidationJudicators L21-27｜JudicatorNames（6 名）。
- Triggers/：CostCheckTrigger L28-45｜CounterUseTrigger L27-42。
- Cards/：UnitCard（触发器 L50-51；扣费≈L108）｜CommandCard（L43-44；扣费≈L111）｜CounterCard（EvaluateUse≈L85-100；激活扣费≈L119）｜KeywordComponents.cs（伏击 L70-148；资格 L135；条件 L141）。
- Managers/：PlayerManager（LoadOpeningHand L126-138；DrawCard L148-170；助手≈L247）｜TurnManager（抽牌≈L105-108；ShouldDrawForTurn L115）｜ResourceManager（L15/L36-40）。
- Match/：MatchOptions（L9/L16）｜MatchCardService（PlaceToHandAsync L199-217）｜Players/Player.cs（HandLimit L30）｜Board/GameEnvironment.cs（GetPositionOf L72｜IsOnFrontLine L90｜GetLineOf L101｜AreAdjacent L118｜GetAdjacentUnits L145｜CollectAuras L170）。
- 测试锚：tests/Orc.Game.Tests/{CommandRulesTests,CommandCombatTests,CommandFrontLineTests,CommandSystemTests,CommandPointTests,CommandLockInTests,CommandTriggerAddressabilityTests}.cs；Judicator*=J2/J3 新测试。
