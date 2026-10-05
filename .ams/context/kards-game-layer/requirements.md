# Requirements — KARDS 游戏层全面完备

把 `src/Orc.Game`（KARDS 风格游戏层）做到"全面完备"：以 kards-diy 为基线，补齐①完整对局流程 ②全部对外输入接口 ③全面的 hook 定义，出口目标＝**先让游戏可完整运行一场对局**。本文件为需求 grill 定稿；实现分三文档（`01-完整流程.md`／`02-输入接口.md`／`03-hook定义.md`），分别交子 agent 执行。

## Scope

- Includes：
  - ①完整对局流程：建局→mulligan→回合循环→行动（出牌/移动/攻击/结束回合）→战斗→死亡→胜负/认输→终局。
  - ②对外输入接口：统一"预行为（Begin）"入口形态；三类选择器槽位；新增独立移动/攻击入口；指令两段式。
  - ③hook 定义：对外信号全集＋介入点/观察面/扩展点清单（文档层）＋代码侧常量/导出器（`invariants.json` 式）＋"信号↔触发器"一致性核对。
- Excludes：
  - 表达力缺口 G1-G19 的效果实现。
  - §4 缺口 #1（JSON 卡牌载入）／#4（效果注册表外部装配）／#5（效果覆盖层）。
  - 天气、疲劳、烧牌、开局特殊卡（startInHand/autoUse）、老兵升级。
  - UI/AI/网络实现（宿主职责）；`ai.js`/`netsync.js`/`net.js` 移植。
  - 与 kards-diy 的数值对拍。

## Constraints

- 构建 0 错误 / 0 警告；三测试项目全绿；`Orc.Game.Tests` 受控随改（迁移非删除、总数不减）。
- `src/Orc` 内核本批不触动；如需触动＝暂停申报。
- 规则参数以 OrC 现行为准：战场 4/5/4（HQ 占支援线槽 0）＋现指挥点曲线，不对拍 kards-diy（其 6/5）。
- 在其上继续既有 `game-environment` 冻结决策（2A/2B/2C 终态），除已授权受控变更（新增独立移动/攻击入口、`CardBase` targeter 下沉、`Initialize` 置 `Mulligan` 并延后先手回合）外不改动。
- 术语：`hook` 保留内核窄义（触发器订阅契约）；广义面称"介入点/观察面/扩展点"并登记 CONTEXT。

## Requirement Items

### R1: 完整流程搭建
- Status: Confirmed
- Scenario/Trigger: 创建并初始化对局后推进到终局。
- Behavior: 系统应支持 建局→起手（静默）→mulligan（双方确认后进对局）→回合循环→（HQ≤0 或某方认输）→终局 的完整流程，且在非对局相位拒绝全部动作入口。
- Acceptance:
  - [ ] 对局相位模型 `MatchPhase{Mulligan,Play,Ended}` 就位（**派生投影**；单一真源＝`MatchState` 加性追加 `Mulligan`）。
  - [ ] `BeginMulliganAsync`（**特制槽位**交互：确认＝换牌并自动确认该方）／`MulliganDone`（不换牌确认）生效：仅 Mulligan 相位、该方未确认时可换；双方确认后进 Play 并执行先手第 1 回合。
  - [ ] `Concede` 生效：置对手为胜者、相位置 Ended。
  - [ ] 终局后动作入口全部拒绝、只读面可用、更新流冻结。
  - [ ] 端到端集成测试（脚本化桥接）从建局跑到终局为绿。
- Terminology: [对局](../game-environment/CONTEXT.md)

### R2: 输入接口
- Status: Confirmed
- Scenario/Trigger: UI/玩家/AI 发起动作或应答目标选择。
- Behavior: 系统应对每个动作只暴露一个"预行为（Begin）"入口；引擎在入口内完成验证与目标交互，桥接（UI）应答驱动后续执行；targeter 内含多个选择器槽位、按顺序获取；单位与指令均为"预打出＋打出"双层触发器；`CardBase` 不持 targeter，由 `UnitCard`/`CommandCard` 各自添加；新增独立 `BeginMoveAsync`/`BeginAttackAsync`。
- Acceptance:
  - [ ] 三类选择器槽位（手牌指向／放置后指向／场上单位指向）各自带参（如起始卡牌）并提交前端，按顺序获取。
  - [ ] `CardBase` 无 targeter 依赖；`UnitCard` 持槽位选择器、`CommandCard` 持目标选择器。
  - [ ] 指令改为两段式（预打出＋打出）。
  - [ ] `BeginMoveAsync`/`BeginAttackAsync` 各自跑限定候选的交互，确认后复用同一执行链；`BeginCommandAsync` 保留。
  - [ ] 输入入口全集清单（现有＋新增）集中导出并有测试。
- Terminology: [Targeter](../targeter/CONTEXT.md)

### R3: hook 定义
- Status: Confirmed
- Scenario/Trigger: 外部/效果需要介入或观察对局。
- Behavior: 系统应集中定义游戏层全部接入点与信号面——对外信号全集、触发器分层、判定器、修饰/光环/管线介入点、监听接入面、引擎扩展点、UI 观察面；并提供代码侧常量与"信号↔触发器"导出核对，及对齐 kards-diy 27 项监听触发器的待补层清单（标后置/不做）。
- Acceptance:
  - [ ] 清单层：全部信号／介入点／扩展点集中枚举（零行为改动）。
  - [ ] 代码侧 `GameHooks.cs` 常量类 + 导出器（JSON）。
  - [ ] 一致性测试：信号↔触发器/判定器映射与注册表一致。
  - [ ] 待补层：27 项对齐表，逐项标"已具备/后置/不做"。
- Terminology: [Hook](../orc-engine/CONTEXT.md)
