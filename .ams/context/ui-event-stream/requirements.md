# Requirements — UI 消费桥接（引擎内部信号 → 表现层）

面向游戏前端开发者，定义引擎向 UI 输出"事件流段"的桥接机制：UI 以注册监听 handler 的方式接入，引擎在动作边界自动产出一段事件流并交付，UI 收到后异步播放表现。

> 状态：需求 grill 已收敛（范围＝UI 消费桥接）；联网/跨计算机相关项移出本任务，转为后续。核心条目已 Confirmed。

## Scope
- Includes:
  - **引擎内部信号的段聚合**（动作作用域：一次动作期间的信号归为一段）
  - **段产出**（动作作用域结束点，自动产出自包含段对象）
  - **段订阅口**（UI 运行期注册监听 handler，接收所有段）
  - **段对象形态**与 **UI 异步消费**
- Excludes（后续任务，本任务不做）:
  - 联网 / 跨计算机引用（**统一引用**三形态、**Nexum** 承载）
  - 对局拓扑（服务端权威 / 全端确定性）
  - 稳定 ID 的**跨端一致**方案与快照对齐
  - 表现编排元数据（时长/分组/串并行）

## Constraints
- 内核现状：**同步因果链**（一次外部动作调用内部顺序 `await` 跑完，返回即"结算与更新完结"）；**无内部循环/调度器/计时器**；单线程语义；P2 原则「更新不允许异步」；`Bus.Emit` 内联 `await` 桥/回调/订阅者。
- 部署：**同进程同语言（Godot C#）**；本任务只处理单进程内的桥接。
- 引用安全：`LogEntry` 全字段只读不可变，触发执行结束后其执行流不再被写入 → 段对象持引用安全、无需深拷贝。

## Requirement Items

### R1: 消费对象与形态
- Status: Confirmed
- Scenario/Trigger: UI 需要播放对局视觉表现时。
- Behavior: UI 的消费对象＝**`EventStream`（因果记录树）**的一段，以**自包含"段"对象**形态接收（含内容森林＋元信息），非逐条实时推送的离散 update 列表。
- Acceptance:
  - [ ] UI 收到的是含因果结构的段对象，而非仅离散 update 序列
- Terminology: [事件流](../../../CONTEXT.md)

### R2: 批边界与段起点
- Status: Confirmed
- Scenario/Trigger: 引擎产出一段事件后需要切段交付 UI。
- Behavior: 批边界＝**一次外部动作调用返回**（逻辑边界，非时间阈值/计时器）；段起点＝本段**第一个条目**（不限 kind）。
- Acceptance:
  - [ ] 以动作调用返回作为段边界，不引入时间阈值/计时器
  - [ ] 段起点为本段第一个条目
- Terminology: —

### R3: 事件流段的消费形态（轮询 / 拉取）
- Status: Confirmed
- Scenario/Trigger: UI 希望消费事件流段以播放表现。
- Behavior: 引擎将产出的段暂存于**待取段队列**（FIFO、取走即移除）；UI 以**轮询**方式主动拉取（形如 `TakeSegments()` / `TryTakeSegment(out EventSegment)`），**不依赖精确通知信号**；过滤由 UI 自持。
- Acceptance:
  - [ ] UI 经拉取接口取得段，取走即移除
  - [ ] 段消费不依赖精确通知（可纯轮询）
- Terminology: —

### R7: 段的内容边界
- Status: Confirmed
- Scenario/Trigger: UI 消费段以播放表现。
- Behavior: 段含本段**全部条目**（不限 kind：`update` / `log` / `attach` / 异常与 validation 留痕）与**递归全部子树**（完整因果）；报错以引擎**现有异常记录形态**（`kind=log`、`level=Error`、`source`/`message`/`keywords`/`data`）随段交付 UI。
- Acceptance:
  - [ ] 段为本段完整因果（含报错、含嵌套子流）
  - [ ] UI 可读取报错条目
- Terminology: —

### R6: 即时更新监听（handler 注入）
- Status: Confirmed
- Scenario/Trigger: UI 需对引擎的**即时更新**做即时反馈（区别于"段"的批量表现播放）。
- Behavior: 引擎新增**专用"非阻塞即时更新监听口"**（与 `Subscribe` 并列、互不影响；`Subscribe` 保留不动）；handler 形态**机制上不可阻塞引擎**（不返回可等待对象 / 引擎不 `await`）；监听范围＝**全部 `Emit` 更新**（开放集合，UI 自持过滤）。
- Acceptance:
  - [ ] 新口与 `Subscribe` 并存、互不影响
  - [ ] handler 机制上不可阻塞引擎
  - [ ] 监听全部 `Emit` 更新，过滤由 UI 自持
- Terminology: —

### R4: UI 消费时序
- Status: Confirmed
- Scenario/Trigger: UI 收到段后。
- Behavior: UI **异步播放**，不阻塞引擎后续推进（引擎不等 UI 播放完成）。
- Acceptance:
  - [ ] 引擎一次动作推进不因 UI 未播完而停顿
- Terminology: —

### R5: 动作作用域（段驱动）
- Status: Confirmed
- Scenario/Trigger: 需要产出段时。
- Behavior: 引擎提供**"动作作用域"原语**（显式标注起止）；游戏层对局级操作入口统一包载；引擎在作用域结束点自动产出段。**动作口径**＝对局级操作入口（`Match.Initialize`/`EndTurn`/`ShuffleDeckAsync`、`PlayManager.PlayUnitAsync`/`JoinUnitAsync`/`PlayCommandAsync`/`UseCounterAsync`/`BeginUnitPrePlayAsync`、`CommandManager.BeginCommandAsync`）；`Initialize` 计一次大动作；底层 `Emit`/`DestroyCard` 不算独立动作；**嵌套作用域＝外层优先、内层合并为外层单段**。
- Acceptance:
  - [ ] 动作作用域显式标注，引擎在结束点产出段
  - [ ] 动作口径＝上述对局级操作入口
  - [ ] 嵌套作用域合并为外层单段
- Terminology: —
