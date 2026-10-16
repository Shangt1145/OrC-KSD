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

## 实现 grill 已退出

全部实现分支（I1~I9）已对齐并落地；交付记录、偏离与验收对照见 `implementation.md`。
