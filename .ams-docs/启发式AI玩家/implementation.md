# Implementation — 启发式简单 AI 玩家

实现 grill 已完成并落地：新增服务层「真实对局创建面」＋ 独立 AI 工程＋示例卡包/样例/测试；全部验收项已跑通（`Orc.Ai.Tests` 13 项、`Orc.Mcp.Tests` 47 项、`Orc.Game.Tests` 1101 项全绿）。

## Scope
- Target: `src/Orc.Mcp`（建局面 ＋ 待决预占）、`src/Orc.Ai`（新工程）、`samples/card-pack-ai`、`samples/Orc.Ai.Sample`、`tests/Orc.Ai.Tests`。
- Excludes: 引擎侧玩家类型分派、`sandbox.create` 语义、自动构筑、AI 的前瞻/搜索、包内词条（`keywords.json`）注册。

## Implementation Steps

### S1: 真实对局创建面（服务层进程内方法）
- Status: Done
- Target: `src/Orc.Mcp/McpService.MatchLaunch.cs`、`Match/MatchLaunchOptions.cs`、`Match/MatchContentLoader.cs`
- Approach: `CreateMatchAsync(MatchLaunchOptions)` 收「包目录列表 ＋ 两个 `MatchSeatConfig`」；`MatchContentLoader` 逐目录判定走包链（`PackageManifest.Parse`＋`CardPackLoader.Load`，含预检双零门）或目录链（`cards`/`cards-*` 发现 ＋ `CardDataLoader.LoadDirectory`）；卡 id 支持「全名或包内原 id 唯一匹配」；卡组必须显式、默认要求 39 张（同名不限）；装配内置插件＋UI 包快照、注册效果、建局、初始化后广播 `MatchChanged(Created)`；另提供 `DisposeMatch()`。
- Acceptance:
  - [x] 包链/目录链均可建局，预检不过＝失败且不留半装载态。
  - [x] 卡组缺失/空/张数不符/未知卡 id/目录不存在＝可读失败。
  - [x] 建局成功后 `Executor.HasMatch == true`、投影可 `Project`。
- Rationale: 建局是进程所有者（宿主）职责——不注册 MCP 工具，player 模式白名单保持 24 条。
- Terminology: [对局执行器](../../CONTEXT.md)、[卡包（Card Pack）](../../CONTEXT.md)

### S2: 待决请求的「预期应答者」预占
- Status: Done
- Target: `Match/TargeterBridgeAdapter.cs`、`Match/MatchExecutor.cs`、`McpService.MatchLaunch.cs`（`ExpectClaimant`）
- Approach: 适配器新增 `ExpectClaimant`；发布待决时清空并预置 `_claimants[sessionId]`；`TryClaim` 增加预留校验；执行器在非 targeting 结局（含 `Detach`）清除预占。
- Acceptance:
  - [x] AI 发起动作链内的待决请求，人类宿主 `TryClaim` 得到可读拒绝（"已预留给 ai"）。
  - [x] 同一动作链多步选择（提交后再次待决）仍保持预占。
- Rationale: `PendingSelection` 无归属字段、`BeginTargeting` 不带发起者；用"动作生命周期内的预期应答者"代替改协议。
- Terminology: [Targeter 桥接]

### S3: AI 工程（单步驱动 ＋ 启发式决策）
- Status: Done
- Target: `src/Orc.Ai/{AiPilot,AiStepResult,HeuristicPolicy}.cs`、`Orc.Ai.csproj`
- Approach: `AiPilot.StepAsync` ＝ 一个主动动作 ＋ 其待决应答；决策顺序＝换牌（不换直接确认）→ 单位 → 指令 → 反制 → 攻击 → 移动 → 结束回合；出牌不查合法性（引擎判定，失败记入回合内排除集），指挥段用 `GetCommandAvailabilityAsync` 作为合法动作源；待决应答按 `SelectorName` 分流（`slot`＝第 1 个、`fieldUnit`＝可击杀→HQ→最强、`option`/其它＝第 1 个、`mulligan`＝不选）。
- Acceptance:
  - [x] AI 回合必然终止（无可行动作 → 结束回合；失败动作不重试）。
  - [x] 只接管自己动作链引起的待决（对手引起的由宿主应答）。
  - [x] 只读座位过滤投影（不看对手手牌/卡组顺序）。
- Rationale: 无后台线程，天然不与服务层单写者门禁竞争；投影不提供 `CanPlayCard`，故合法性以引擎结果为准。
- Terminology: [投影](../../CONTEXT.md)、[指挥点](../../CONTEXT.md)

### S4: 示例卡包、样例与测试
- Status: Done
- Target: `samples/card-pack-ai/**`、`samples/Orc.Ai.Sample/**`、`tests/Orc.Ai.Tests/**`、`OrcEngine.sln(x)`
- Approach: 卡包含 3 单位＋1 指令＋1 反制（带 `manifest.json`，走包链）；样例建局后 AI(1) 单步循环 vs 脚本对手(0)；测试覆盖建局校验、策略单测、整局（AI 胜、`HqZero`）、终局后不动作、同种子可复现。
- Acceptance:
  - [x] `dotnet run --project samples/Orc.Ai.Sample` 稳定跑到终局（AI 胜、对手 HQ=0）。
  - [x] `dotnet test tests/Orc.Ai.Tests` 13 项全绿；既有 `Orc.Mcp.Tests`/`Orc.Game.Tests` 无回归。
- Rationale: 档 3 需要真实存在的指令与反制卡才能验证分支；固定种子保证可复现。
- Terminology: [卡牌数据体](../../CONTEXT.md)

## 已知边界（不在本次范围）
- 换牌策略固定「不换」（`MulliganDone` 直接确认）。
- 指令/反制按费用盲打（`UiCardView` 无效果语义）。
- 反制只在「当前无已激活反制」时点一次（避免激活/取消反复翻转）。
- 卡包内 `keywords.json` 未在建局面注册（需要包生命周期观察者，本次未接）。
- AI 不主动认输；不接管对手动作引起的待决（超时按取消，宿主兜底）。
