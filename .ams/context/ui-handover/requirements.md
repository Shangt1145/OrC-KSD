# Requirements — UI 开发者交接文档

面向 UI 开发者（前端表现层）的交接文档，聚焦三条主线的接入与使用：①事件流消费；②即时更新 handler 监听；③UI 输入集成。

## Scope
- Includes:
  - 事件流消费（段轮询：`BeginAction` / `TakeSegments` / `TryTakeSegment` / `EventSegment`）
  - 即时更新 handler 监听（`OnImmediateUpdate` 非阻塞注册；并列 `Subscribe` / `IEngineBridge`）
  - UI 输入集成（动作入口调用、结果对象、目标选择应答链路 `ITargeterBridge` / `ITargetingResponder`）
- Excludes（待 grill 确认）:
  - 联网/跨机引用（统一引用）、表现编排元数据等（沿用 ui-event-stream 排除项）

## Constraints
- 交付物为**自包含**单文档，位于 `docs/`。
- 受众＝**Godot C# 前端开发者 + 同进程同语言**（沿用 ui-event-stream 部署前提）；示例仅依赖引擎公开面。
- 术语以根 `CONTEXT.md` 为准（段轮询、即时更新监听、接入点、观察面、扩展点等；Hook 为内核窄义词，不得泛用）。
- 与既有 `ui-integration-guide.md` 不重复造第二真源：可引用其结论。

## Requirement Items

### R1: 文档落点与形态
- Status: Confirmed
- Behavior: 新建自包含交接文档于 `docs/`（`docs/ui-开发者交接文档.md`）；覆盖三重点；一、二部分复用现有 guide 结论并补齐，第三部分为新增。
- Acceptance:
  - [ ] UI 开发者仅读本文档即可完成三条主线的接入
  - [ ] 与既有 guide 不产生冲突的第二真源

### R2: 三重点覆盖
- Status: Confirmed
- Behavior: 文档主体须明确分章覆盖①事件流消费 ②即时更新 handler 监听（并列对比 `OnImmediateUpdate` / `Subscribe` / `IEngineBridge`，措辞＝UI 注册 handler）③UI 输入集成。
- Acceptance:
  - [ ] 三重点各有独立章节，含入口签名、时序、约束、常见坑

### R3: UI 输入集成范围
- Status: Confirmed
- Behavior: 覆盖核心四类入口（对局 / 出牌 / 指挥 / 目标应答）；明确标注 `PendingDelivery` 三项（`Concede` / `MulliganReplace` / `MulliganDone`）为「签名冻结、实现待交付」；不含门户·重发等非 UI 主动输入面。
- Acceptance:
  - [ ] 四类入口均有入口签名与调用时序
  - [ ] 待交付入口显式标注状态，不误导为可用

### R4: 集成逻辑深度
- Status: Confirmed
- Behavior: 第 3 重点须含前置门禁、结果对象三态读法、目标应答异步倒置全流程（收集→Begin→Complete/Cancel）、常见坑；含「Match → 各 Manager 速查表 + 预览面」。
- Acceptance:
  - [ ] 覆盖异步倒置全流程（`requestId` 配对、拒绝可重试、终局恰好一次）
  - [ ] 结果对象按 `Status`+类别化原因读取，不依赖文本

### R5: 交付形态
- Status: Confirmed
- Behavior: 每重点＝结构化说明 + 关键 API 签名表 + 最小代码示例；另含一条端到端串联示例（回合开始 → 出牌含目标选择应答 → 帧轮询取段播放）与一张时序图。
- Acceptance:
  - [ ] 三重点各有说明/表/示例
  - [ ] 端到端串联示例可串起三条主线

### R6: 与既有文档的关系
- Status: Confirmed
- Behavior: 自包含 + 互链 + 单真源声明；信号清单＝精简快照表（注明以代码常量为权威）并指向原 guide；不改动既有文件。
- Acceptance:
  - [ ] 文末「相关文档」互链
  - [ ] 不产生两份会漂移的信号清单
  - [ ] 不修改既有文件
