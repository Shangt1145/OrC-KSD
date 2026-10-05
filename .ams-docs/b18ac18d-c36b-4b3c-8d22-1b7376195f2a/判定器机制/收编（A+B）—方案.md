# 判定器收编（A+B）— 方案（滚动）

> 需求：用户指令（2026-10-04）——推进判定器收编，**范围＝A 档（交战矩阵类 9 项）＋B 档（行动经济类 4 项）**；C/D/E 档不做。
> 方向：依《基线调研报告-游戏层域.md》§五收编方向建议——A＝服务化＋注册面＋句柄（moding）、消除重复；B＝规则配置面。
> 状态：**设计输入调研 ✅**（《收编（A+B）-基线调研报告.md》落盘）；**K0 已发出；K1-K4 主链待『换牌/认输』包冻结后发出**。

## 范围

- A 档 9 项（原清单 1-9）：范围矩阵／被守护攻击资格／轰炸机拦截／守护关系／推进前置／目标合法性／复验三方法／反击豁免表／伏击条件判定。
- B 档 4 项（原清单 10-13）：行动费三读点／部署费校验／HandLimit 爆牌／开局常量。
- 已知已收编项复核：第 7 项（复验主体）、第 11 项（费用校验验证侧）已随 J2 收编——本批覆盖"剩余面"（A7 剩余重复、B11 扣费侧）。

## 设计定稿（依据调研 aa7f3229）

### A 类切割（C1-C8 语义单元）
- C1 combat.target.legal：（attacker, targetRef）→bool；统一 A1/A2/A3/A6 对外形状（组合判定器）。
- C2 combat.range（或并入 C1）：（UnitCard, Slot）→bool；范围矩阵单源＋**类型组**（炮/战/轰、炮/轰合并为单一类型组配置）。
- C3 combat.guard.eligibility：（attacker）→bool；守护两处；类型组与 C2 共表。
- C4 combat.interception：（UnitCard, Slot, targetIsFighter）→bool；拦截两处。
- C5 combat.counter.eligibility：（attacker, target）→bool；A8 单源＋A9 资格侧。
- C6 combat.ambush.condition：（self, attacker）→bool；伏击条件单源。
- C7 复验补全：抽**共享 leg 条件**（owner/can/suppressed/cost）＋上下文规则，可用性与判定器同调（消除 A7 剩余重复）。
- C8 move.frontline-enemy（按需提升；现转发单源）。
- 形态：①转发壳与②规则外提**混合**；命名域 combat.*／move.*／economy.*；新增判定器名**同步 GameHooks/GameHooksJson 清单**（跨包同步点）。

### B 类
- B10 行动费**统一读取口**（4 读点抽单一取值点；可选判定器化为『费用特例』预留）；扣费写点与读取点分开收。
- B12 爆牌统一（共享单元 `HandLimitBurn` 参数化）——K0 ✅。
- B13 起手 4/5 与首回合不抽**配置化**（MatchOptions 首选；槽上限 12 已有先例）；接线待主链。

### 通用
- 编辑域隔离：『换牌/认输』活跃包**零触碰**（Match/MatchOptions/MatchState/MatchLifecycle/CommandManager/CommandTypes/PlayManager/GameEntryPoints/TargetSlots/HandSelectSlot、MulliganManager/MatchPhase/MulliganSelectSlot）；S-C 六文件禁编；其它在途包零触碰；共享文件最小加性并申报。
- 验收口径：行为零变化（CommandRules/CommandCombat/CommandFrontLine/CommandSystem/CommandPoint/CommandLockIn 全绿）＋moding 改写专项；构建 0 警告；全量回归复跑。

## 委托链与时序（调整后）

- **K0：B12 爆牌统一**（原"烧牌"；用户 2026-10-05 更名）✅（8c07ba33/9dd6b5d2#2）——全量 1113 全绿；在途包袱申报 2 条（sample 构建锁定/GameHooksJson 锚点待同步）。
- **K1：A 类交战合法性判定族（C1-C4）** ✅（b41cc60c/e573308a#2）——全量 1129 全绿；moding 穿透演示通过。
- **K2：A 类反击/伏击（C5+C6）** ✅（be72067b/d9824392#2）——全量 1137 全绿；C5/C6 moding 演示通过；CounterAttackRules 判定器吸收。
- **K3：A 类复验消重（C7+C8）** ✅（54c37276/76ab6fa5#3）——全量 1153 全绿；A7 剩余重复消除；行动费读取单源覆盖。
- **K4：B 类余项（B10+B13）** ✅（1748bde6/c1b0ac07#4）——全量 1178 全绿；OperateCosts 读写同源；MatchOptions 配置化；费用特例面结论＝不判定器化。
- **Kb：爆牌独立化＋术语更名** ✅（6a5b612d/83367640#2）——`card.burned` 独立信号（17→18）；HandLimitBurn 输出改独立（不复用 discarded）；术语零残留；全量 1179 全绿。
- **K5：验收核对＋全量回归** ✅（d30d9c25/13ccd834#2）——六组核对全过（A 档 9/9＋B 档 4/4＋复核/隔离/术语/回归）；零缺口零补测；四套件 1179/0/0＋六套件点名 56/0/0；《验收核对记录-K5.md》落盘；**裁定＝通过**（sample 锁按一贯口径）。
- 冻结判定（2026-10-05）：『换牌/认输』行数快照稳定（CommandManager 1567／Match 776 与调研终值一致）＋功能面完整＋测试全绿 → 判定冻结、K1 发出；后续各单沿用"发出前复核＋隔离申报"。
- 每单 useGrill；串行推进；每单完成后复跑。

## 执行状态（滚动）

- **全部收官 ✅（2026-10-05）**：调研＋K0-K5＋Kb（7 单）；最终四套件 1179/0/0、六套件点名 56/0/0；遗留 6 条（见工作记录）。
