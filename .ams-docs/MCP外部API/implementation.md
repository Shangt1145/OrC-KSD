# Implementation — 面向 MCP 的引擎外部 API

> 实现 grill 状态：**已退出**（顺序 B「闭环优先」执行完毕）。本文件＝实现计划与交付记录。
> 交付证据：全解决方案 `0 错误 0 警告`；全量测试 **1523 通过 / 0 失败**（既有 1488 零回归 ＋ 新增 35）。

## Scope

- **Target**（已交付）：
  - `src/Orc.Mcp`：协议无关服务层（工具目录 + 统一返回外壳 + 对局执行器 + targeter pending 状态机 + 沙盒 + 资产管线 + 观察/调试/文档读取）。
  - `samples/Orc.Mcp.Stdio`：stdio 适配示例（手写最小 JSON-RPC，零第三方依赖；`--mode authoring|player`）。
  - `tests/Orc.Mcp.Tests`：35 个测试（契约断言 + 路径守卫 + 引用表 + 端到端最小闭环）。
  - 引擎侧最小改动：`EngineReferences`（实例级引用表）+ `LogicEngine.References` 挂接 + `Player.ReferenceKey`；`JsonValueWriter` 公开；`ServiceRegistry.Names`；`EventSegmentJson`（新）；`CardValidationReportJson`（新）。
  - 内嵌文档集 7 篇（`src/Orc.Mcp/Docs/**`）：卡牌数据体、效果、词条、卡包目录、对局与观察、目标选择、沙盒工作流。
- **Excludes**（未做，与需求一致）：内核改写面（判定器/触发器 moding）、插件热重启、写入型诊断、player 模式的对局注入路径、GUI。

## Implementation Steps（全部 Done）

### S1: 引擎侧最小改动 【Done】
- Target: `src/Orc/Core/EngineReferences.cs`（新）、`src/Orc/Core/LogicEngine.cs`、`src/Orc/Output/JsonValueWriter.cs`、`src/Orc/Core/UnifiedServices.cs`、`src/Orc.Game/Players/Player.cs`、`src/Orc.Game/Managers/PlayerManager.cs`
- Approach:
  - 引用表接在 `LogicEngine.RegisterCard`/`UnregisterCard` 内（卡牌创建即登记、销毁即注销——零侵入覆盖 `Card`/`Hq`）。
  - `Player` 在 `PlayerManager.CreatePlayers` 处经 `EngineReferences.Assign` 分配引用键并写入 `Player.ReferenceKey`。
  - `JsonValueWriter` 类与顶层 `Write` 放开为 public；`ServiceRegistry<,,>.Names` 增加只读条目名枚举。
- Acceptance: ✅ 引用表可注册/解析/幂等/拒绝覆盖/注销；✅ 表随引擎实例；✅ 既有 4 个测试工程全绿（1488）。

### S2: 服务层骨架与统一返回外壳 【Done】
- Target: `src/Orc.Mcp/{McpService.cs,McpServiceOptions.cs,SegmentCache.cs,Contracts/*}`
- Approach: `McpToolDescriptor`/`IMcpToolHandler`/`McpToolResult`（`{ok,code,message,details,engineResult}`）/ `McpCodes`（受控结果码）；集中式目录（`AddTool`）+ 强类型参数 record + `ToolArgs` 解析 helper；模式与挂起期门禁集中在 `InvokeAsync`。
- Acceptance: ✅ 工具清单可枚举且顺序稳定；✅ 成功/失败同构；✅ 取消＝成功（`cancelled`）、业务失败 `ok:false`。

### S3: 对局执行器与 targeter pending 【Done】
- Target: `src/Orc.Mcp/Match/{MatchExecutor.cs,TargeterBridgeAdapter.cs}`
- Approach: 单写者 `SemaphoreSlim` + 动作生命周期门禁（动作 → 等到"完成或待决" → 释放；待决期间只允许 `target.*` 与只读工具；应答后等到"完成或再次待决"）；桥接同步 `BeginTargeting` 接住会话、内部驱动循环逐次 `NextAsync`/发布待决/等应答/`Submit`；超时提交 `CancelEvent`（按取消处理）；状态变更用**可替换信号**（`ChangeTask`）唤醒执行器，不忙转。
- Acceptance: ✅ 无死锁（动作与应答不互相持锁等待）；✅ 多步选择可用；✅ 超时按取消、对局不卡死。

### S4: 沙盒最小闭环 【Done】
- Target: `src/Orc.Mcp/{Sandbox/*,Tools/SandboxTools.cs,Tools/CardTools.cs,Tools/ShapeTools.cs,Tools/ObserveTools.cs,Tools/MatchTools.cs,Tools/TargetTools.cs}`
- Approach: 脚手架随沙盒模式启动创建（manifest 的 `guid` 一次性持久化）；建局用 `SnapshotPackageActivator(BuiltInPluginPackage.CreateSnapshot())` + `SkipMulligan` + 起手 0 张；`card.load` 逐卡注册（跳过已在库 id）；塑造工具 `spawn/kill/damage/set_points/emit`。
- Acceptance: ✅ 端到端闭环测试通过（建局→写卡→注册→放置→观察→投递→销毁→销毁对局）。

### S5: 最小文档集 【Done】
- Target: `src/Orc.Mcp/Docs/**`（7 篇，内嵌资源）+ `Tools/DocsTools.cs`
- Approach: `docs.list` 返回扁平清单（id/title/category/summary/lines/分节目录）；`docs.read(id, offset, limit)` 按行段读取。
- Acceptance: ✅ 7 个条目可列出与分段读取；✅ 事实表类清单由 `diag.*` 代码生成（`diag.hooks`/`diag.entry_points`）。

### S6: 资产管线 【Done】
- Target: `src/Orc.Mcp/Tools/AssetTools.cs`
- Approach: `card.write`/`effect.write`（先校验后规范化写出、默认不覆盖、写路径经 `SandboxPathGuard`）；`asset.validate`（逐文件 + `CardDistributionValidator`）；`file.list`/`file.read`；`asset.promote`（默认关闭、要求无活跃沙盒局、目标须在 `--packs` 根内且已存在、不隐式覆盖）。
- Acceptance: ✅ 越界写拒绝；✅ 校验失败不落盘；✅ promote 三重门控。

### S7: 调试全景与缺口补齐 【Done（3/4）】
- Target: `src/Orc/Output/EventSegmentJson.cs`（新）、`src/Orc.Game/Output/CardValidationReportJson.cs`（新）、`Tools/DiagTools.cs`、`Tools/ObserveTools.cs`
- Approach: `EventSegmentJson.Serialize/SerializeAll`（复用 `EventStreamJson` 的条目与节点写法，单一真源）；`CardValidationReportJson` 手写 writer 导出报告；`observe.segments` 每段附完整 `json`；`diag.report` 聚合摘要（引用式）+ `diag.audit_chain`/`diag.hooks`/`diag.entry_points`。
- Acceptance: ⚠️ 4 处缺口中 3 处已交付（`EventSegmentJson` 实现、报告 JSON 化、`JsonValueWriter` 公开）；**字段级数值差异（`StatUpdateDetection`）未交付**——见「偏离」。

### S8: 文档全量 + sample + 测试 【Done】
- Target: `samples/Orc.Mcp.Stdio/*`、`tests/Orc.Mcp.Tests/*`
- Approach: 手写最小 JSON-RPC（`initialize`/`notifications/initialized`/`tools/list`/`tools/call`/`ping`），业务失败内嵌、仅 `internal` 置 `isError`；BOM 容错；不转义非 ASCII。测试三层：契约断言、路径守卫、端到端闭环。
- Acceptance: ✅ `dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring` 一条命令启动；✅ 冒烟测试 4 条请求（initialize / service.info / sandbox.create / docs.list）全部成功；✅ 35 个新测试通过。

## 实现期发现与偏离（须显式记录）

1. **「沙盒局默认无卡」被引擎契约限缩**：`Match` 在创建期**硬拒空卡组名单**（`src/Orc.Game/Match/Match.cs:99-107`，且既有测试锁定该行为）。故沙盒局改为：脚手架生成一张**占位卡**（`sandbox.placeholder.card.json`，对 agent 可见/可改/可删）作为双方卡组底，并以 `OpeningHandSize=0` 保证它不进手牌。
   - 影响：R0g 验收项"不含任何卡实例"按"不含任何**功能卡**实例"落地；偏离已在工具返回与文档中说明。
   - 备选（未采用）：改 `Match` 允许空卡组——会破坏既有契约与测试，且空卡组下抽牌路径存在未验证风险。
2. **`Player` 无 `Entity.Id`**：引用键由引用表分配（Q22b），新增 `Player.ReferenceKey`（同一对局内稳定、重建后变化——已在文档与 `service.info` 提示）。
3. **字段级数值差异未交付**（R3 第 4 处缺口）：`StatUpdateDetection` 的比较结果仅在数值管线内部产生，对外暴露需要改动"管线写入路径"（热点路径、触及收敛不变量）。收益可由 `observe.snapshot`（组件级字段）部分覆盖，故**本轮不交付**。
4. **`card.load` 对已注册 id 跳过而非失败**：`MatchCardService.RegisterCard` 为"只追加不覆盖"（`MatchCardService.cs:159-186`）。已注册 id 返回在 `alreadyRegistered` 列表中（与 Q21「改卡＝新 id」一致）。
5. **新增源文件未加许可头**：仓库现有源文件（如 `src/Orc/Core/Entity.cs`）均无文件级许可头，许可由 `LICENSE`/`LICENSE-EXCEPTION.md` 覆盖。新增文件遵循仓库现状，未引入头注释（R8 该项按"与仓库一致"处理）。
6. **player 模式仅交付模式位与工具过滤**：`--mode player` 下沙盒专属工具**不登记**（工具清单中不存在），但**对局注入路径未实现**（Q3-子4 仍待对齐；player 模式不提供建局/连接能力）。

## 交付物与运行方式

| 交付物 | 位置 | 说明 |
|---|---|---|
| 服务层 | `src/Orc.Mcp` | 协议无关；零 MCP SDK 依赖 |
| stdio 适配示例 | `samples/Orc.Mcp.Stdio` | `dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring [--sandbox <dir>] [--packs <dir>] [--promote] [--csx] [--seed N]` |
| 测试 | `tests/Orc.Mcp.Tests` | 35 个（契约/守卫/引用表/端到端） |
| 文档集 | `src/Orc.Mcp/Docs/**` | 内嵌资源，经 `docs.list`/`docs.read` 读取 |

### 工具清单（authoring 模式，共 24 个）

- `service`：`service.info`、`service.tools`
- `sandbox`：`sandbox.create`、`sandbox.dispose`、`sandbox.state`、`sandbox.spawn`、`sandbox.kill`、`sandbox.damage`、`sandbox.set_points`、`sandbox.emit`
- `card`/`pack`：`card.load`、`card.list`、`card.write`、`effect.write`、`asset.validate`、`pack.info`、`file.list`、`file.read`、`asset.promote`
- `match`：`match.end_turn`、`match.concede`
- `target`：`target.submit`、`target.cancel`
- `observe`：`observe.view`、`observe.snapshot`、`observe.segments`
- `diag`：`diag.report`、`diag.audit_chain`、`diag.hooks`、`diag.entry_points`
- `docs`：`docs.list`、`docs.read`

## Terminology

- [引擎托管引用表](./requirements.md)（`EngineReferences`，引擎实例级，N 格式 GUID）
- [沙盒模式](./requirements.md)（服务层运行模式）
- [对局执行器](./requirements.md)（单写者串行化 + 动作生命周期门禁）
- 既有术语见 [`CONTEXT.md`](../../CONTEXT.md)

---

# 第二批：UI 宿主集成（形态 A2 —— 人机同局）

> 状态：**已完成**。需求对齐见 `requirements.md` 的 R9a~R9d；实现分支 V1~V3 见 `implementation-grill-plan.md`。
> 交付证据：全解决方案 `0 错误 0 警告`；全量测试 **1535 通过 / 0 失败**（新增 12 个 UI 集成/白名单测试）；参考宿主实跑通过。

## V0: 沙盒局补装 UI 插件包 【Done】
- Target: `src/Orc.Mcp/Tools/SandboxTools.cs`
- Approach: 建局快照由"仅 `orc.builtin.core`"改为 `AssetCatalogSnapshot.FromPackages([BuiltInPluginPackage.Load(pluginsRoot), UiPluginPackage.Load(pluginsRoot)])`——否则 `Match.UiProjections` 无 provider，UI 无法投影。
- Acceptance: ✅ 沙盒局 `UiProjections.HasProvider` 为真（测试 `SandboxMatch_ExposesUiProjectionProvider` 断言）；✅ pluginsRoot 可经选项覆盖。

## V1: 宿主集成面与强类型动作面 【Done】
- Target: `src/Orc.Mcp/Hosting/HostingContracts.cs`（`IMcpHost`/`IMcpActions`/`MatchLifecycleEventArgs`/`SegmentConsumer`）、`src/Orc.Mcp/McpService.Hosting.cs`、`src/Orc.Mcp/Match/{CardResolver.cs,EngineResultMapper.cs}`
- Approach:
  - `IMcpHost`：`ActiveMatch`/`TryGetActiveMatch`、`MatchChanged`（Created 在**对局就绪后**广播）、`SubscribeTargetingRequests`（UI 订阅待决）、`TrySubmitTargeting(..., out reason)`、`TryClaimTargeting`（双向接管）、`SegmentConsumer`/`SetSegmentConsumer`/`PullSegments`/`IngestSegments`。
  - `IMcpActions`：16 个强类型动作（换牌/出牌/指挥/查询），全部经 `MatchExecutor`（与 MCP 工具**共用同一门禁与 pending 状态机**）。
  - 三态结果统一映射：`Success`→completed、`Cancelled`→**cancelled（成功语义）**、`Failed`→failed + 引擎原因（`EngineResultMapper`）。
- Acceptance: ✅ 工具与强类型入口同实现（`ToolAndActionFaces_StayInSync`）；✅ 取消按成功（`TargetingCancel_IsReportedAsCancelledSuccess`）。

## V2: 玩家动作面扩充与两模式白名单 【Done】
- Target: `src/Orc.Mcp/Tools/{PlayerActionTools.cs,MatchTools.cs,TargetTools.cs,CardTools.cs,SandboxTools.cs,ObserveTools.cs}`
- Approach: 新增 16 个玩家入口工具（`mulligan.*`2 + `play.*`7 + `command.*`5），**两模式共有**；调试类工具统一 `sandboxOnly`；`target.claim` 提供接管；`observe.view` 按模式分流（沙盒＝全知摘要；player＝按座位过滤的投影视图，需 `AgentSeat`）。
- Acceptance: ✅ `ToolWhitelistTests`：player 模式工具清单**恰好**等于玩家白名单；authoring 与 player 的差集**恰好**等于调试工具清单（防"新工具忘了标 sandboxOnly"）；✅ 实测修正 3 处漏标（`card.list`/`pack.info`/`sandbox.state`）。

## V3: 参考宿主与接入指南 【Done】
- Target: `samples/Orc.Mcp.HostSample/{Orc.Mcp.HostSample.csproj,Program.cs}`、`docs/mcp-ui-integration.md`、`src/Orc.Mcp/Docs/engine/host-integration.md`
- Approach: 控制台"伪 UI"参考宿主，完整演示 6 条接线：会话/门控、`MatchChanged`→投影 provider、每帧 `Project`、待决请求订阅 + 认领 + 帧内提交、段消费（Host 声明 + 喂回服务层）、强类型动作面；接入指南面向 UI 团队，内嵌文档面向 agent。
- Acceptance: ✅ 实跑输出验证：`投影 provider 就绪（hasProvider=True）` → agent 预打出 `status=targeting` → `玩家点选 → 提交成功` → 每帧投影 → `取段 2 条 → 喂给服务层` → `EndTurnAsync → status=completed`。

## 第二批的偏离与修正（实现期发现，均已修）

1. **`MatchChanged(Created)` 的时序缺陷**：最初在 `Executor.Attach` 时广播，此时对局尚未 `Initialize`、UI 包激活相未跑 ⇒ UI 建 provider 拿到 null（参考宿主实测 `hasProvider=False`）。**修**：装配与广播分离——`AttachMatch` 只装配，`NotifyMatchReady()` 在 Initialize 完成后广播。
2. **桥回调内同步提交不可用**：在 `SubscribeTargetingRequests` 回调里同步 `TrySubmitTargeting`（Hang 在内层锁等待上）会失败。**修**：参考宿主改为"回调只渲染+认领，提交发生在帧循环"（也是真实 UI 的形态）；并为 `TrySubmitTargeting` 增加 `out reason` 便于诊断。
3. **3 处工具漏标 `sandboxOnly`**（`card.list`/`pack.info`/`sandbox.state`）：由白名单护栏测试与实跑发现并修正。
4. **`player` 模式观察面**：`observe.view` 改为两模式共有并按模式分流（沙盒全知 / player 按 `AgentSeat` 过滤投影）；`AgentSeat` 未配置时返回 `mode-denied` 并说明原因。

## 第二批交付物

| 交付物 | 位置 | 说明 |
|---|---|---|
| 宿主集成面 | `src/Orc.Mcp/Hosting/HostingContracts.cs` | `IMcpHost` + `IMcpActions` |
| 集成实现 | `src/Orc.Mcp/McpService.Hosting.cs` | 事件/订阅/接管/段消费/强类型动作 |
| 玩家动作工具 | `src/Orc.Mcp/Tools/PlayerActionTools.cs` | 16 个入口（两模式共有） |
| 参考宿主 | `samples/Orc.Mcp.HostSample` | `dotnet run --project samples/Orc.Mcp.HostSample` |
| 接入开发手册 | `docs/mcp-ui-integration.md` | **自包含**：7 步开发流程（每步带验收点）＋ 完整 API 签名参考（10 节）＋ 故障排查表（11 节）＋ 反模式清单（12 节）＋ DoD 自检清单（9.2） |
| agent 说明 | `src/Orc.Mcp/Docs/engine/host-integration.md` | 内嵌；经 `docs.read` 读取 |
| 集成测试 | `tests/Orc.Mcp.Tests/{HostIntegrationTests.cs,ToolWhitelistTests.cs}` | 仲裁链路 + 白名单护栏 |
