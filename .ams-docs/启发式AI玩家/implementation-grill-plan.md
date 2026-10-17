# Implementation Grill Plan — 启发式简单 AI 玩家

> 本轮 grill 聚焦：建局面与 AI 的**内部结构**、与既有门禁/待决状态机的接线方式、验收形态。需求见 `requirements.md`。

## 相关代码基线（已核）

- `McpService` 单例承载唯一桥＋唯一执行器（`src/Orc.Mcp/McpService.cs`；`McpService.Hosting.cs`）。
- `MatchExecutor.RunAsync/SubmitAsync` 单写者＋忙期门禁（`src/Orc.Mcp/Match/MatchExecutor.cs`）。
- 建局现状仅在 `SandboxTools.CreateAsync`（空卡组、`SkipMulligan=true`、起手 0）。
- 投影提供者由 `.ui` 包在 `match/preCommand` 相注册；建局须同时装内置插件与 UI 包快照，否则 `Match.UiProjections` 无 provider。
- `AttachMatch` 目前 `internal`；`PlayerTools` 白名单固定 24 条（`tests/Orc.Mcp.Tests/ToolWhitelistTests.cs`）。

## 已定（需求阶段带入）

- AI 占 1 号座位；对手位不限；驱动＝宿主显式 `StepAsync`；只接管自己动作链的待决；座位过滤视图。
- 交付＝`src/Orc.Ai` ＋ `samples/Orc.Ai.Sample` ＋ `tests/Orc.Ai.Tests`。

## Unresolved Questions

### D1: 建局面 API 形状 Completed
- 已定：**A. 进程内方法** `McpService.CreateMatchAsync(...)`，不注册 MCP 工具；白名单保持 24 条。

### D2: 建局参数与卡组来源 Completed
- 已定：入参为 `IReadOnlyList<string> PackDirectories` ＋ 两个 `MatchSeatConfig(int Seat, IReadOnlyList<string> Deck, string? Name)`；卡组**必须显式**，缺失＝失败。
- 已定：多包按列表顺序装载，卡 id 冲突＝fail-fast。
- 已定：`SkipMulligan=false`、起手 4/5（`MatchOptions` 默认）、`Seed`/`MatchId` 可配。

### D4: AI 内部结构 Completed
- 已定：`StepAsync`＝一次主动动作＋其待决应答；返回 `AiStepResult { Performed, Kind, Outcome, TurnEnded }`；不提供 `RunTurnAsync`（宿主自行循环）。

### D5: 待决抢占 Completed
- 已定：适配器加「预期应答者」预占（发起动作前声明，pending 发布时写入 `_claimants`）；人类 `TryClaim` 得可读拒绝。

### D3: 装配细节 Completed
- 已定：**C. 只抽装载段**——新建 `MatchContentLoader`（包链/目录链→定义集＋效果声明＋内联＋效果库），`sandbox.create` 保持原样（沙盒是编辑中状态，不应被预检双零门卡住）。
- 已定：装配顺序＝进程级注册 → 装载 → `CardEffectRegistry.DeclarePrefab` → 建局（内置插件＋UI 包快照）→ 内联/效果库注册 → `Initialize` → 广播 `MatchChanged(Created)`。
- 已定：`BuiltInPlugin.RegisterProcessDefaults()` 须**早于装载**（否则预检报未知组件）。
- 已定：包链的卡 id 带 `pack/{id}/` 前缀；建局面接受「全名或包内原 id」（唯一匹配）。

### D6: 测试与样例 Completed
- 已定：**B. 新增 `samples/card-pack-ai/`**（3 单位＋1 指令＋1 反制，带 manifest，走包链），卡组 39 张、同名不限。
- 已定：样例＝建局 → AI(1) 单步循环 vs 脚本对手(0)（只结束回合）→ 终局。
- 已定：测试＝建局校验 5 项 ＋ 策略单测 5 项 ＋ 整局 3 项（胜/终局后不动/同种子可复现）。
