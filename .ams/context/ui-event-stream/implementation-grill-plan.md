# Implementation Grill Plan — UI 消费桥接

> 全部问题已决，实现 grill 闭合。实现文档（`implementation.md`）已收敛为可执行计划。

## 未决问题
（无）

## 已决问题

### I1: 段交付语义 Completed
- 决策：**非阻塞通知 + UI 拉取**（pull），后明确为 **UI 轮询、不依赖精确信号**。

### I8: 拉取接口与暂存模型 Completed
- 决策：**a** —— 引擎维护**待取段队列**（FIFO、取走即移除）；UI 经 `TakeSegments()`/`TryTakeSegment` 拉取；可纯轮询。

### I2: 即时更新监听的范围与语义 Completed
- 决策：**b** —— 新增**专用"非阻塞即时更新监听口"**（与 `Subscribe` 并列、`Subscribe` 保留不动）。
- 子决策 **r1**：监听范围＝**全部 `Emit` 更新**（开放集合，UI 自持过滤）。

### I3: 动作作用域原语与段产出触发 Completed
- 决策：**a** —— `BeginAction()` 返回可释放作用域（`await using`），引擎以 `AsyncLocal` 维护作用域栈，仅最外层退出封段。
- 子决策 **x1**：异常路径 **`finally` 中仍封段入队**。

### I4: 段对象的内容边界 Completed
- 决策：**a** —— 段＝本段**全部条目**（update/log/attach/异常/validation）＋**递归全部子树**（完整因果）。
- 子决策 **e1**：报错复用现有异常记录形态。

### Q8: 段对象元信息字段 Completed
- 决策：**a** —— 仅**段号**＋**时间戳**（UTC）；不重复放置可由段内条目推导的信息。
- 子决策 **s1**：段号＝**引擎实例内**单调递增。

### I7: 段起点（动作前已有内容）Completed
- 由 S2 游标机制覆盖：段只含"游标之后的新增"，天然不含历史。

### I5: 段队列容量与溢出 Completed
- 决策：**a** —— **无上限**；正常轮询不积压；"丢段/重同步"留给后续联网任务。

### I6: 现有通道关系 Completed
- 决策：段通道与即时更新口均为**新增**；既有 `Subscribe`/`IEngineBridge` **保留不动**；三者在 `Emit` 时点互不干扰（详见 `implementation.md`「通道关系与兼容」）。
