# Requirements — 启发式简单 AI 玩家

在既有「agent 玩家（MCP 工具面）」与「人类玩家（`IMcpActions` 面）」之外，新增第三种对局驱动方：进程内、无搜索、规则贪心的启发式 AI；并补齐它所依赖的「真实对局创建面」——该面同时可供人类宿主与外部 agent 使用。

## Scope
- Includes:
  - 服务层「对局创建面」：收「包目录列表」＋ 双方卡组名单 → 建局并挂到服务层（有 `manifest.json` 走包链 `PackageDiscovery`＋`CardPackLoader.Load`；否则走目录链 `CardDataLoader.LoadDirectory`）。
  - 新工程 `src/Orc.Ai`：启发式 AI 玩家（占 1 号座位，档 3 决策覆盖）。
  - `samples/Orc.Ai.Sample`：固定种子下 AI(1) vs 脚本对手(0) 跑完整局的示例。
  - `tests/Orc.Ai.Tests`：决策与整局验收测试。
- Excludes:
  - 不做搜索／前瞻／学习／蒙特卡洛；不评估指令与反制的**效果价值**（只按费用）。
  - 不做 AI 主动认输、聊天、卡组构筑优化；换牌固定为「不换」。
  - 不修改 `sandbox.create` 语义；不修改 `Orc.Game.Players.Player`（不引入玩家类型分派）。
  - 不接管「由对手动作引起、需要 AI 决定」的待决选择（归人类宿主；超时按取消）。

## Constraints
- 一切动作经 `IMcpActions`／`McpService` 门禁（单写者 `MatchExecutor`）；AI 不得直接调 `PlayManager`／`CommandManager`。
- 只读座位过滤投影（`IUiProjectionProvider.Project(match, aiPlayer)`）与只读可用性查询；不得读对手手牌明细与卡组顺序。
- 无后台线程：AI 由宿主显式调用 `StepAsync` 推进。
- 决策可复现：同一种子 + 同一读面 ⇒ 同一动作序列。
- 一次只允许一个活跃对局（沿用 `MatchExecutor` 既有约束）。

## Requirement Items

### R1: 真实对局创建面
- Status: Confirmed
- Scenario/Trigger: 宿主需要在无沙盒语义的情况下开一局带真实卡牌的对局。
- Behavior: 服务层提供创建入口，接受「卡包目录列表」与「双方卡组名单」，装配卡定义/效果库后建局、初始化，并广播 `MatchChanged(Created)`。
  卡组**必须显式指定**（由前端在生成玩家/AI 角色时给出，不做自动构筑）。
  入口为**进程内方法**（`McpService.CreateMatchAsync`），**不注册为 MCP 工具**：player 模式 24 条工具白名单不变，外部 agent 由宿主代建对局。
- Acceptance:
  - [ ] 传入含 `manifest.json` 的包目录时，走包链并受分发预检双零门约束；预检不过＝创建失败且不留半装载态。
  - [ ] 传入无 manifest 的卡目录（如 `samples/card-pack-demo`）时走目录链，创建成功。
  - [ ] 卡组名单缺失或为空时，创建失败并给出可读原因（不自动构筑）。
  - [ ] 卡组张数不等于 39 时创建失败（KARDS 默认口径：39 张卡组，总部随玩家自动生成，同名不限）。
  - [ ] 创建成功后 `Executor.HasMatch == true`，投影 provider 可 `Project` 成功。
  - [ ] 工具白名单仍为 24 条（player 模式），无新增调试/建局工具。
- Terminology: [对局执行器](../CONTEXT.md)、[卡包（Card Pack）](../CONTEXT.md)

### R2: AI 以单步方式驱动
- Status: Confirmed
- Scenario/Trigger: 轮到 AI 座位且无其它动作进行中。
- Behavior: `StepAsync` 执行「一个动作 + 该动作引起的待决应答」，返回执行结果；宿主循环调用即可推进 AI 的整个回合。
- Acceptance:
  - [ ] AI 忙期或存在待决请求时调用，不会并发进入引擎。
  - [ ] 每次 `StepAsync` 至多提交一个主动动作（含其派生的选择应答）。
- Terminology: [动作作用域](../CONTEXT.md)

### R3: 完整回合决策覆盖
- Status: Confirmed
- Scenario/Trigger: AI 座位行动中。
- Behavior: 依次处理换牌、单位出牌、指令出牌、反制、攻击、移动、结束回合，规则如下：
  - 换牌：不换，直接确认完成。
  - 单位出牌：`EffectiveCost` 降序 → (`EffectiveAttack+EffectiveDefense`) 降序；槽位＝支援线最靠前的空槽。
  - 指令：单位出牌结束后处理，`EffectiveCost` 降序。
  - 反制：手牌含 `Counter` 且点数足够时使用，`EffectiveCost` 降序。
  - 攻击：`CanAttack` 单位按 `EffectiveAttack` 降序；目标优先级＝可一击击杀的敌单位（击杀中攻最高）→ 敌方 HQ → 其它敌单位（攻降序）。
  - 移动：无单位可攻击、支援线有单位、前线有空槽、点数≥行动费时，推进攻击力最高的单位。
  - 结束回合：重新投影后无任何可行动作，或点数不足以支付任何动作。
- Acceptance:
  - [ ] 每个动作完成后重新读取投影，不使用过期视图决策。
  - [ ] 一回合内动作序列必然终止（不空转、不死循环）。
- Terminology: [前线](../CONTEXT.md)、[指挥点](../CONTEXT.md)

### R4: 待决选择的应答
- Status: Confirmed
- Scenario/Trigger: AI 自己发起的动作返回 `targeting`。
- Behavior: AI 认领并提交候选；按 `SelectorName` 分流：
  - `slot`＝最靠前空槽；`fieldUnit`＝攻×血最高的敌单位；`option`＝第 1 项；`mulligan`＝空选择；其它＝第 1 个候选。
- Acceptance:
  - [ ] AI 发起的动作不会滞留在 `pending` 直到超时。
  - [ ] 只认领自己动作链引起的待决请求；对手引起的待决不由 AI 接管。
- Terminology: [Targeter 桥接]

### R5: 信息视野受限
- Status: Confirmed
- Scenario/Trigger: AI 做任何决策。
- Behavior: 只用座位过滤投影字段（`SelfHand` / `SelfLine` / `EnemyLine` / `SelfHq` / `EnemyHq` / `SelfKredits` / `SelfMaxKredits` / `ActivePlayerSide`）与只读可用性查询。
- Acceptance:
  - [ ] AI 决策路径不读取 `Player.Hand`／`Deck` 的对手侧对象，也不读对手手牌数量以外的隐藏信息。

### R6: 座位与共存
- Status: Confirmed
- Scenario/Trigger: 同一对局中同时存在人类宿主与 AI。
- Behavior: AI 座位可配置（默认 1 号）；0 号座位可由人类宿主或外部 agent 驱动，AI 不关心其实现。
- Acceptance:
  - [ ] AI 仅在 `AiSeat` 座位上行动。
  - [ ] 人类在 0 号座位操作时，AI 不发动作、不抢待决应答。

### R7: 终止与失败处理
- Status: Confirmed
- Scenario/Trigger: 对局结束，或动作返回失败/取消。
- Behavior: 对局结束（`State=Ended`）后 AI 不再动作；单个动作失败时记为该动作不可用并继续下一候选，不重试同一动作。
- Acceptance:
  - [ ] 终局后继续调用 `StepAsync` 不产生任何动作。
  - [ ] 同一动作连续失败不会导致无限循环。

### R8: 交付与验收
- Status: Confirmed
- Scenario/Trigger: 运行样例或测试。
- Behavior: 独立工程 `src/Orc.Ai`（引用 `Orc.Mcp` 与 `Orc.UI.Contracts`）；样例 `samples/Orc.Ai.Sample`；测试 `tests/Orc.Ai.Tests`。
- Acceptance:
  - [ ] 固定种子的样例可稳定跑完一局，终局原因可读（预期 `hq-zero`）。
  - [ ] 测试断言：AI 回合必然结束、动作合法（失败率为 0 或仅有可解释的拒绝）、决策可复现。
