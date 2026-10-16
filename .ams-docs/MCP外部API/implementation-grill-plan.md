# Implementation Grill Plan — 面向 MCP 的引擎外部 API

> 本轮拷问聚焦：把已对齐的需求（见 `requirements.md`）落成**可执行的实现路径**——按逻辑顺序拆解步骤，逐分支对齐，并用具体场景压力测试、提前排除实现级 BUG。
> 需求类问题若在此冒出，按"中途需求变更"处理（回 `requirements.md` 与需求 grill 计划）。

## 已锁定的实现前置（来自需求 grill）

- 交付物：`src/Orc.Mcp`（协议无关服务层，零 MCP SDK 依赖）+ `samples/Orc.Mcp.Stdio`（`--mode authoring|player`）+ `tests/Orc.Mcp.Tests`；文档集混合生成，工具为 `list-docs`/`read-docs`。
- 引擎侧允许的最小改动：① 引擎托管引用表（**实例级**，挂 `LogicEngine`，`N` 格式 GUID，无 GUID 对象由引擎分配）；② `JsonValueWriter` 公开；③ `ServiceRegistry` 条目名只读枚举面；④ 实现 `EventSegmentJson`；⑤ 校验报告 JSON 导出；⑥ 暴露 `StatUpdateDetection` 字段级差异。
- 统一返回外壳 `{ ok, code, message, details?, engineResult? }`；`isError` 仅用于工具自身故障；取消＝成功。
- 沙盒：默认无卡、逐卡注册、写白名单（唯一写根）、进程级副作用不回滚但可见、promote 默认关闭且要求无活跃沙盒局。
- 对局：单活跃对局；targeter 会话化 pending（可配置超时＝取消，默认 10 分钟；挂起期只允许 `target.*` 与只读工具）。
- 观察面按模式分级；段"取走即移除" + 服务层最近 N 段缓存；调试面只读 + `diag.report` 聚合。

---

## 已完成

### I1: 实现步骤的分解与顺序 【Completed】
- 决策（**顺序 B·闭环优先**）：S1 引擎侧最小改动 → S2 服务层骨架与统一外壳 → S3 对局执行器与 targeter pending → S4 沙盒最小闭环 → **S5 最小文档集**（闭环在此成立）→ S6 资产管线 → S7 调试全景 → S8 文档全量 + sample + 测试。
- 代价已接受：S1 的引擎改动分两次（最小集 + S7 补齐）。

### I5: 引擎侧改动的最小侵入方式 【Completed】
- 天然注册点：`Card` 构造即注册（`src/Orc/Cards/Card.cs:28`）＋销毁即注销（`src/Orc.Game/Cards/CardLifecycle.cs:352` → `LogicEngine.UnregisterCard`）⇒ 引用表接在 `LogicEngine.RegisterCard`/`UnregisterCard` 内即可**零侵入覆盖 Card 及其子类 `Hq`**。
- 决策（选项 丙 + a）：
  - **登记点注册**：`Card`/`Hq`（内核 2 处）；`Player`——在 `PlayerManager` 创建处登记（该处已持有 `_engine`）。
  - **按需分配（懒注册）**：`Slot` / `FormationGap` / `Effect` 等**不扩散构造链**（`BattleLine`/`Battlefield` 均不持有引擎引用，扩散需改 4 处签名），由引用表在需要时分配 GUID（在**该引擎实例内**稳定）。
  - **targeter 候选用会话内序号引用**（候选由引擎给定、外部不得自造）：服务层返回 `[{index, kind, …}]`，agent 以序号提交，服务层映射回 `Ref<Entity>`；`Slot`/`FormationGap` 因此不必进全局表。
- 命名（暂定，S1 定稿）：表类型 `EngineReferences`（挂 `LogicEngine.References`）；托管注册接口暂定 `IEngineManagedReference`。

### I2: 对局执行器的串行化机制 【Completed】
- 关键陷阱（本轮暴露）：若用"一把锁包住整个工具调用"，需选靶的动作会**持锁直到 agent 提交**，而 `target.submit` 需要同一把锁 ⇒ **死锁**。
- 决策（选项 a）：**单写者信号量 + 动作生命周期门禁**——同一时刻只允许一个未完成动作；动作调用取锁 → 启动动作 → await 到"完成或进入待决" → 释放锁；待决期间动作 Task 挂在后台、执行器标记"忙"，只允许 `target.*` 与只读观察工具；`target.submit` 取锁 → 驱动会话 → await 动作 Task 到"完成或再次待决" → 释放锁（天然支持多步选择）。
- 安全性论证：待决窗口内动作调用栈已展开（挂在 `TaskCompletionSource` 上）、引擎实际空闲 ⇒ 只读调用不与之并发；应答后的续体执行期间持锁 ⇒ 也不与新调用并发。
- 否决项：b 专用线程 + 自定义 `SynchronizationContext`（引擎内大量 `ConfigureAwait(false)` 使续体落线程池，除非改遍引擎否则做不到真同线程）；c 不做串行化（MCP 不保证客户端不并发调用）。
- 关键事实：引擎的"单线程语义"实际靠**不并发**而非**同线程**（`LogicEngine` 用 `AsyncLocal<ActionScope?>`；`TargeterManager` 内部 `lock` + `Queue` + `TaskCompletionSource`；`TargeterSession.NextAsync` 用 `SemaphoreSlim` 且 `ConfigureAwait(false)`）。

### I3: targeter pending 状态机的落点与超时实现 【Completed】
- 决策（选项 a）：动作的最终结果**在最后一次 `target.submit` 返回**（`engineResult` 携带 `CommandResult`/`PlayResult`）；若动作再次待决则返回新的 targeting 信息。不另设"最近动作结果"查询工具，不做推送订阅。
- 落点：`ITargeterBridge` 实现放执行器同组件；`BeginTargeting` 同步入队并立即返回；超时由服务层定时器持有，超时即提交 `CancelEvent `。

### I4: 服务层工具声明结构与 schema 【Completed】
- 决策（选项 a + a）：**集中式目录**（`IToolCatalog` → `ToolDescriptor{name, description, inputSchema, group}`）**+ 每工具一个 handler**（`InvokeAsync(ToolCall, ct)`）；参数为**每个工具一个强类型 `sealed record`**（服务层手写 `JsonDocument → record` 解析，共用 helper 摊薄样板）；输入 schema 手写常量（不引入生成器）。
- 命名前缀（暂定）：`sandbox.*` / `card.*` / `pack.*` / `target.*` / `match.*` / `diag.*` / `docs.*`。

---

## 未决

### I6: 沙盒目录与写白名单的落地 【Completed】
- 决策（选项 a + a）：
  - **单沙盒包**：`<base>/sandbox/pack/` ＝ `manifest.json` + `cards/` + `effects/`（可选 `texts.json`/`keywords.json`）；不引入多包（避免每包独立 guid/version/依赖与预检）。
  - **脚手架随沙盒模式启动即建**：生成 `manifest.json`（`kind=cardpack`、`id=sandbox`、**一次性生成并持久化的 `guid`**、`version=0.0.1`）与空目录——保证 `AssetName`（含包 GUID 前 8 位）的稳定性不随"首次写入"漂移。
  - **统一路径守卫 `SandboxPathGuard`**：全防 `..` 穿越、绝对路径、符号链接逃逸、Windows 大小写绕过、UNC 路径，且**写前对规范化后的最终路径复核**（而非仅校验输入字符串）。
- 既有约定（复用）：`CardPackLoader` 目录约定 `cards/`（可 `cards-*`）+ `effects/` + `texts.json` + `keywords.json`；`CardDataWriter.FileSuffix=".card.json"`；`PackageManifest` 字段 `kind/id/guid/version/dependsOn[]/assets[]`；三个资产管理 API 均接受显式目录（可绕开两个硬编码 `DefaultDirectory`）。

### I7: 文档集生成器的落地 【Completed】
- 决策（选项 a + a）：手写文档放仓库目录并声明为**构建期内嵌资源**（改文档需重编译，但 agent 只读，无使用侧影响；规避"相对工作目录"隐患，仓库已有两个同类坑）；事实表**每主题一条**（`engine.events`/`engine.entry-points`/`engine.judicators`/`engine.keywords`/`engine.components`/`engine.hooks`/`engine.triggers`）——代码生成，随代码自动更新。生成器放在 `src/Orc.Mcp` 内。
- 已对齐的需求侧约束：`list-docs` 返回扁平清单 `{id, title, category, summary, updatedAt}` ＋ 分节目录（由 md 标题 + 行号解析）；`read-docs(id, offset?, limit?)`；手写叙述中的源码以"引用路径＋行号＋关键片段摘录"呈现。

### I8: sample 与测试的形态 【Completed】
- 决策主问（选项 a）：**最小可用集**——`initialize`（`protocolVersion` + `capabilities.tools` + `serverInfo`）、`notifications/initialized`、`tools/list`、`tools/call`、`ping`。不实现 `resources/*`（文档已由 `list-docs`/`read-docs` 工具提供）、不做 prompts/logging/roots/sampling。
- 决策子问（选项 a + 用户补充"保留开箱即用"）：**sample 手写最小 JSON-RPC，不引 MCP SDK 依赖**；"开箱即用"落成三条硬要求：① `dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring` 一条命令即可启动；② 随 sample 附**可直接复制的 MCP 客户端配置片段**（stdio 命令 + 参数）；③ 使用方**不需要编写任何适配代码**。
- 子问题二（暂定，按推荐）：stdio 层**不做**进程级冒烟测试；端到端闭环测试直接在服务层跑。

### I9: 引擎侧改动的边界与回归 【Completed】
- 决策（选项 a + a）：**严格按现有分层归位**（内核只收通用机制：引用表、可见性、段 JSON、注册表枚举；游戏层相关留在 `Orc.Game`/`Orc.Packages`）；**全量回归 + 新增面补单测**。
- 落地结果：`0 错误 0 警告`；全量测试 **1523 通过 / 0 失败**（既有 1488 零回归 + 新增 35）。
- 实现期新增偏离（详见 `implementation.md`）：① 沙盒局以**占位卡**满足引擎"卡组非空"契约（默认无卡按"不含功能卡"落地）；② 字段级数值差异（R3 第 4 处缺口）**未交付**；③ 新增源文件未加许可头（与仓库现状一致）。

---

## 实现 grill 已退出（第一批：MCP 服务层）

全部实现分支（I1~I9）已对齐并落地；交付记录、偏离与验收对照见 `implementation.md`。

---

## 第二批：UI 宿主集成（形态 A2）—— 需求/实现混合 grill

> 本批为**需求变更**（新增 UI 集成能力域），故先在需求层对齐 U1/U2（已完成），再逐项对齐三处仲裁与交付细节。
> 已锁定的技术前提：沙盒建局当前只装配 `orc.builtin.core`（`BuiltInPluginPackage.CreateSnapshot()`，`BuiltInPluginPackage.cs:95`），
> 而投影提供者由 `orc.builtin.ui` 在 `match/preCommand` 相注册（`UiPluginPackage.cs:92-102`）⇒ **不补装 UI 包，UI 看不到沙盒局**。

### U1: 编辑者对局的归属与输入权 【Completed】
- 决策（选项 **A2**）：**服务层建局（沙盒局），UI 与 agent 都能输入**（人机同局）；代价是必须新增三处仲裁（U3/U4/U5）。

### U2: 交付边界 【Completed】
- 决策（选项 b）：`Orc.Mcp` 增宿主集成面 + 《UI 侧接入指南》+ 可运行参考宿主 `samples/Orc.Mcp.HostSample`；**不**改对方维护的 Godot 仓库。

### U3: 桥接仲裁（选靶归谁应答） 【Completed】
- 硬约束：`Match` 构造只接受**一个** `ITargeterBridge`（`Match.cs:88`）。沙盒局当前注入服务层的 `TargeterBridgeAdapter`；A2 下 UI 也需要参与选靶。
- 候选：
  - **B-a**：服务层持有唯一桥，新增**待决请求订阅面**（UI 订阅 pending → 渲染候选 → 回调服务层 `SubmitTarget`）；UI **不再实现** `ITargeterBridge`。仲裁＝服务层内优先级策略。
  - **B-b**：新增**仲裁桥**（实现 `ITargeterBridge`，内部分派给 UI 桥与 MCP 适配器）；UI 保留既有桥模型。
  - **B-c**：**谁的动作谁应答**（agent 动作→agent 应答；UI 玩家动作→UI 应答），需"当前动作发起者"标记；归属不明时按显式优先级。
- 子问 U3a：是否提供"接管"能力（人接手 agent 的待决请求 / agent 接管人的）。
- 子问 U3b：归属不明时的默认优先级（UI 优先 / agent 优先 / 先到先服务）。

### U3-决策细节 【Completed】
- 主问（选项 **B-a**）：服务层持有唯一桥 + 新增**待决请求订阅面**；UI 订阅 pending 后回调服务层提交，**UI 不再实现 `ITargeterBridge`**。
- U3a（选项 a）：**双向可接管**（人可接手 agent 的待决请求；agent 亦可接管人的）。
- U3b（选项 c）：**先到先服务**；已被应答的请求拒绝接管。

### U4: 段消费唯一者 【Completed】
- 决策（选项 **C-a** + U4a=a）：**服务层作唯一 `TakeSegments()` 消费者**（`SegmentCache` 复用已有），UI 从服务层订阅/拉取段——理由＝**更好支持无头服务器运行形态**；宿主**显式声明**是否担任（默认服务层负责）。

### U5: 门禁统一 【Completed】
- 决策（选项 **D-a** + 子问 b）：**服务层作唯一驱动面与唯一门禁**——UI 的对局动作也经服务层（**强类型 C# API**），不再直调 `Match`/`PlayManager`/`CommandManager`；强类型 API 与 MCP 工具**共用同一执行器、门禁与 pending 状态机**。
- 连带工作量（已暴露）：服务层动作面须从"编辑者闭环子集"扩展到 **UI 所需的玩家入口**（见 U5b）。

### U5b: 服务层动作面扩充清单 【Completed】
- 决策（选项 a + a）：**UI 可玩集**——`match.end_turn`/`match.concede`、`mulligan.begin/done`、`play.unit_pre/unit/join/command_pre/command/counter`、`command.begin/move/attack/leave`、`command.availability`、`play.can_play`（+ `target.claim`）；两模式共有，调试类仍 `sandboxOnly`。
- 验收（选项 a）：以玩家白名单与 `GameEntryPoints` 对齐，双向比对（多＝越权、少＝能力缺失）⇒ 落地为 `ToolWhitelistTests` 三组断言。

### V1: 宿主集成面与强类型动作面 【Completed】
- 决策（选项 a）：`IMcpHost`（集成面）+ `IMcpActions`（强类型动作面），由 `McpService` 实现；MCP 工具为其薄封装（同一执行器/门禁/状态机）。

### V2: 动作面扩充与两模式白名单 【Completed】
- 决策（选项 a）：新增 16 个玩家入口（两模式共有）；白名单护栏测试。

### V3: 参考宿主与接入指南 【Completed】
- 决策（选项 a）：`samples/Orc.Mcp.HostSample` + `docs/mcp-ui-integration.md` + 内嵌 `engine/host-integration.md`。

### 实现期修正（详见 `implementation.md`）
- `MatchChanged(Created)` 时序（改为"对局就绪后广播"）；桥回调内同步提交不可用（参考宿主改为帧内提交 + `out reason`）；3 处工具漏标 `sandboxOnly`；`observe.view` 按模式分流 + `AgentSeat`。

---

## 实现 grill 已退出（第二批）

UI 集成的全部实现分支（V1~V3）已对齐并落地；交付记录、偏离与验证证据见 `implementation.md`「第二批」。

### U6: 沙盒局 UI 可见性 【Completed】
- 决策：服务层暴露**宿主集成面**（查询活跃 Match、对局创建/销毁事件）；**投影 provider 由 UI 自建**（`CreateProvider`）；UI 不持有对局销毁权。

### U7: 座位与人机分工 【Completed】
- 决策（选项 **F-c** + U7a=a）：由服务层模式决定——**沙盒模式：agent 不占座位、全知视图（不隐藏信息）、调试工具可用**；**player 模式：agent 占座位，操作能力与真人玩家完全等同**（无调试/越权入口），视图按座位隐藏信息。
