# 实现 Grill 计划 — 触发器引擎原型开发阶段

> 本 grill 聚焦：以阶段划分（S1~S5，见 实现.md）为纲，对齐每阶段的实现路径、验收与边界。
> 当前执行模式（2026-10-03 用户指示）：**先完成 S2~S5 全部实现 grill；委托串行执行**。
> 当前：S1~S3 ✅验收通过；**S4 委托进行中（e0ddd234）**；Lua 委托进行中（e8802a93，独立无交集）；S5 待发。

## 已解决

- I1/I2/I3 {Completed}；执行模式 {Confirmed}；I6/I7/I8 {Completed}。
- S1 {Completed ✅}；I10 S2 {Completed ✅}；I11 S3 {Completed ✅}；I12 S4 {Completed（grill）}；I13 S5 {Completed（grill 收口）}。
- S3 验收：100/100 测试；受控变更（Trigger 构造追加可选参数）已标注；知悉项（挂载优先级降序）已记录。

## 待办与队列

### 新需求（进行中）
- **Lua 动态 handler（Orc.Lua）**：需求 grill 完成；实现委托进行中（e8802a93）；完成后验收。

### 委托队列
- [进行中] **S4（e0ddd234）**；[进行中] Lua（e8802a93）——两者无写入交集
- [队列] S5（待 S4 验收后发）
- [暂缓] 事件流阅读工具 .csx tool package——前置条件＝**事件流格式定稿**
- [待办] py 版阅读工具（面向 shell；给其他 agent 用）

### 待执行修正（排队）
- **Emit 排序改升序（越小越先）**（用户 2026-10-03 指令）：修改 Bus.Emit 排序方向 + UpdatePriorities 调整（拟 High=100/Normal=200/Low=300）+ 受影响测试更新 + 全量回归。**执行时机：S4 完成后**（避免写入交集），届时唤醒 S3 子 Agent（8e3eda9a）。

### 待确认事项（小）
- S2 遗留①："Stop 后、in-flight 事件中新发起的子执行"行为规格（当前不空转）
