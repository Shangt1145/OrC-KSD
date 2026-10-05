# 判定器收编（A+B）— 方案（滚动）

> 需求：用户指令（2026-10-04）——推进判定器收编，**范围＝A 档（交战矩阵类 9 项）＋B 档（行动经济类 4 项）**；C/D/E 档不做。
> 方向：依《基线调研报告-游戏层域.md》§五收编方向建议——A＝服务化＋注册面＋句柄（moding）、消除重复；B＝规则配置面。
> 状态：**设计输入调研 ✅**（《收编（A+B）-基线调研报告.md》落盘）；**K0 已发出；K1-K4 主链待『换牌/认输』包冻结后发出**。

## 范围

- A 档 9 项（原清单 1-9）：范围矩阵／被守护攻击资格／轰炸机拦截／守护关系／推进前置／目标合法性／复验三方法／反击豁免表／伏击条件判定。
- B 档 4 项（原清单 10-13）：行动费三读点／部署费校验／HandLimit 烧牌／开局常量。
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
- B12 烧牌统一（共享单元 Burn(emitDrawn) 参数化）——**K0 已发出**。
- B13 起手 4/5 与首回合不抽**配置化**（MatchOptions 首选；槽上限 12 已有先例）；接线待主链。

### 通用
- 编辑域隔离：『换牌/认输』活跃包**零触碰**（Match/MatchOptions/MatchState/MatchLifecycle/CommandManager/CommandTypes/PlayManager/GameEntryPoints/TargetSlots/HandSelectSlot、MulliganManager/MatchPhase/MulliganSelectSlot）；S-C 六文件禁编；其它在途包零触碰；共享文件最小加性并申报。
- 验收口径：行为零变化（CommandRules/CommandCombat/CommandFrontLine/CommandSystem/CommandPoint/CommandLockIn 全绿）＋moding 改写专项；构建 0 警告；全量回归复跑。

## 委托链与时序（调整后）

- **K0：B12 烧牌统一**（🔄 已发出，8c07ba33，2026-10-04）——无重叠子项先行。
- **K1：A 类交战判定族（C1-C6）**——**待『换牌/认输』包冻结后发出**（CommandManager 主战场重叠）。
- **K2：A 类收口（C7 复验消重）**——同上待冻结。
- **K3：B 类余项（B10/B13）**——待冻结（CommandManager/Match/MatchOptions 重叠）。
- **K4：验收核对＋全量回归**——收尾。
- 时序复核机制：K0 汇报到达时**复核热文件活跃性**（『换牌/认输』是否仍有在途痕迹）；冻结确认后再发 K1；否则继续等待/推进其他安全子项。
- 每单 useGrill；串行推进；每单完成后复跑。

## 执行状态（滚动）

- 调研 ✅（aa7f3229/5b0160d4#2）；K0 已发出（8c07ba33）；K1-K4 待冻结复核。
