# Requirements Grill Plan — 启发式简单 AI 玩家

> 本轮 grill 聚焦：在既有「agent 玩家（MCP 工具面）／人类玩家（IMcpActions 面）」之外，新增第三种对局驱动方——进程内启发式 AI 玩家的**外部行为与边界**。

## 代码基线（已核对，非待决项）

| 事实 | 证据 |
|---|---|
| 仓库内不存在 `AgentPlayer` / `HumanPlayer` / `IPlayer` / `PlayerController` 类型 | `src/Orc.Game/Players/Player.cs`（sealed，纯数据 + internal 注入面）；`PlayerManager.CreatePlayers` 只产 2 个座位 |
| 「agent 玩家」＝运行模式 + 工具白名单，不是类型 | `src/Orc.Mcp/McpServiceOptions.cs:6-13`（`McpServiceMode.Player`）、`AgentSeat`；`src/Orc.Mcp/Tools/PlayerActionTools.cs` |
| 「人类玩家」＝强类型动作面 + 座位过滤投影 | `src/Orc.Mcp/Hosting/HostingContracts.cs:88`（`IMcpActions`）；`samples/Orc.Mcp.HostSample/Program.cs` |
| 唯一建局入口是 `sandbox.create`，且造的是**空卡组**对局 | `src/Orc.Mcp/Tools/SandboxTools.cs:91-100`（`deckA/B` 仅占位卡、`OpeningHandSize=0`、`SkipMulligan=true`） |
| player 模式下"对局从何而来"仍未定案 | `docs/mcp-ui-integration.md:449`（遗留项 Q3-子4） |
| 引擎侧无 AI/启发式代码 | 全仓 `heuristic`/`Bot`/`Minimax`/`Mcts` 零命中 |

## Unresolved Questions

### Q1: AI 玩家在**哪个对局**里行动、与谁同席 Completed
- 已定：**A. 新增服务层「对局创建」面**，沙盒语义不动。
- 已定：AI 座位＝**1 号**。
- 已定：对手位（0 号）**两者皆可**（人类宿主或外部 agent），AI 只认 `AiSeat`。
- 已定（Q1b）：**C. 两条装配链都支持**——入口收「包目录列表」，有 `manifest.json` 走 `PackageDiscovery`＋`CardPackLoader.Load`（双零门），否则走 `CardDataLoader.LoadDirectory`。
- 已定（Q1c，修订）：卡组**必须显式指定**，由前端在生成玩家/AI 角色时给出；**不做自动构筑**。
- 已定（Q1d）：`PacksRoot` 由宿主任意指定；仓库内不必新增正式包，示例可用 `samples/card-pack-demo`（目录链）或 `samples/card-pack-synthetic`（包链）。

### Q2: "简单启发式"的决策覆盖面 Completed
- 已定：**档 3**——换牌／单位出牌与选位／指令（盲打，按费用）／反制（有费就用）／指挥（移动+攻击）／结束回合／待决应答，全覆盖。
- 已核事实：换牌相位是必经（`MatchOptions.SkipMulligan=false` 时），AI 不处理会卡住；反制是**主动入口**（`PlayManager.UseCounterAsync`）。
- 已核限制：`UiCardView` 不含效果语义 ⇒ 指令/反制只能按费用盲打。

### Q3: 驱动方式（谁推动 AI 思考） Completed
- 已定：**A. 宿主显式单步**——`await pilot.StepAsync(ct)` 做「一个动作 + 其待决应答」，宿主循环调用；无后台线程。
- 已定（Q3a）：**i. 只应答自己动作链引起的选择**；对手引起的待决归人类宿主（超时按取消）。
- 已定（Q3b）：**i. 座位过滤投影**（`SelfHand/SelfLine/EnemyLine/EnemyHq/SelfCounterCount`），不看对手手牌与卡组顺序。

### Q4: 评分与优先级规则 In Progress
- 待确认：出牌排序键、槽位选择、攻击目标偏好、移动条件、反制时机、结束回合判据。
- 待确认：是否允许 1 层前瞻（推荐不做——"简单启发式"）。

### Q5: 交付形态与验收 Pending
- 工程落点（`src/Orc.Mcp/Ai` 内嵌 vs 新 `src/Orc.Ai`）、样例、测试口径。
- 待确认：AI vs 对手的验收形态（脚本对手 / 人类宿主模拟）。

### Q3: 驱动方式（谁推动 AI 思考） Pending
- 候选：宿主帧循环显式 `StepAsync`（与 `MatchExecutor` 单写者门禁兼容）／服务层内部后台循环／引擎事件钩子。
- 约束待确认：必须不与「人类动作」「agent 动作」并发进入引擎（`pending-targeting` / `busy` 门禁）。

### Q4: 评分与优先级规则 Pending
- 待定：只用「可打性 + 攻/防/费」的贪心打分，还是允许 1 层前瞻（如"攻击是否会被反杀"）。
- 待定：是否使用 `CommandAvailability` 的候选列表作为唯一合法动作源（避免自造规则）。

### Q5: 信息视野 Pending
- 待定：AI 是否受座位过滤约束（不看对手手牌明细）；是否允许读引擎全知面（同进程可读）。

### Q6: 交付形态与验收 Pending
- 待定：新增工程/命名空间位置（`src/Orc.Mcp` 内 or 新 `Orc.Ai`）、是否需要"自动跑完一整局"的可运行样例、测试口径（确定性种子下同一决策序列）。
