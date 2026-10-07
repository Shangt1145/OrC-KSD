# Implementation Grill Plan — Targeter 桥接重设计

> 本 grill 聚焦：把需求文档落成可执行实现路径（类型形状 → 桥接契约 → targeter 流程宿主 → 校验/重试 → 迁移）。

## Unresolved Questions

### I1: 选择器类型族的泛型形态 Completed
- **a**：`abstract class Selector<TResult>`（定义/模板）＋ 非泛型基接口 `ISelectorInstance`（身份、异构持有）＋ `SelectorInstance<TResult>`（实例：绑定参数、承载 `SelectorResult<TResult>`）。

### I2: 取用与提交的会话形状 Completed
- **b**：选择器实例承载提交（`instance.Submit(...)`）；会话只承载取用。
- 取用签名＝异步 `Task<ISelectorInstance?> NextAsync()`（`null`＝流程结束、随后读 `session.Result`）——**推定**（子问未答）。

### I2a: 谁判定选择器 Result Completed
- **c（语义事件归一）**：前端把原始手势归一为语义事件（`Picked` / `DragCancelled` / `EmptyCommit`）交后端选择器实例判定 `SelectorResult`；前端不含判定逻辑。

### I3: targeter 组装面形状 Completed
- **c（流程函数）**：`manager.RunAsync(Func<ITargeterFlow,...> flow, param)`；`flow.Step(选择器模板, 参数) → SelectorResult<T>`；静态函数即"targeter 模板"。
- 附带（推定）：`CreateTargeter(...)` 由 `RunAsync` 取代。

### I4: 后端业务校验层的落点 Completed
- **a（选择器实例内）**：`Submit` 一并完成语义判定与业务校验，不通过＝`Failed(InvalidSelection)`。

### I7: 重试的 API 形态 Completed
- **ii（`flow.Retry()`）**：显式重入当前选择器实例；上限由 `flow` 计数，超限返回 `Failed`。

### I5: TargeterManager 改造范围 Completed
- **a**：`RunAsync` 入队、执行时经桥接交付**会话**；前端在会话上拉取；终局出队。
- 交付形态＝同步 `void`（推定，子问未答）。

### I6: 迁移顺序与回归策略 In Progress
- 约束：Q2＝一次性替换（无兼容层）；现役 4 处调用方 + `Targeter*Tests` + `MockTargeterBridge` + `DemoTargeterBridge` + 文档。
- 三选：a) 严格一次性（中途不编译）；b) 分两批（S1–S2 纯新增、旧测试保绿 → S3–S7 整体切换一次收口）；c) 临时兼容桥接、逐步迁移。

### I5: TargeterManager 改造范围 Pending
- FIFO 队列/终局门禁/留痕如何与新"拉取式"契约共存（谁来 await、谁迁出队列）。

### I6: 迁移顺序与回归策略 Pending
- 先类型后契约再调用方？测试如何保绿（分步替换 vs 一次性）；每步验收。
