# 实现 Grill 计划 — 触发器引擎原型开发阶段

> 本 grill 聚焦：以阶段划分（S1~S5，见 实现.md）为纲，对齐每阶段的实现路径、验收与边界。
> 当前执行模式（2026-10-03 用户指示）：**先完成 S2~S5 全部实现 grill；委托串行执行**。
> 当前：S1/S2 ✅验收通过；**S3 委托进行中（155a5a05）**；全部实现 grill 已完成；Lua-DIY 需求 grill 第 1 轮待用户回复。

## 已解决

- I1/I2/I3 {Completed}；执行模式 {Confirmed}；I6/I7/I8 {Completed}。
- S1 {Completed ✅}；I10 S2 {Completed（grill）}；I11 S3 {Completed（grill）}；I12 S4 {Completed（grill）}；I13 S5 {Completed（grill 收口）}。
- S2 {Completed ✅}：交付 + 验收通过（59/59；受控变更与遗留见 实现.md）。

## 待办与队列

### 新需求（需求 grill 中）
- **Filament.Core + Lua（面向社区 DIY）**：研究完成；可行性 ✅；风险 ⚠（许可证未授权占位 + 项目极早期）。需求 grill 第 1 轮（集成路径与许可选择 a/b/c）**待用户回复**。文档见 `Lua-DIY/`。

### 委托队列（串行）
- [进行中] **S3（155a5a05）**
- [队列] S4 → S5（按序，前一个验收后发下一个）
- [暂缓] 事件流阅读工具 .csx tool package——前置条件＝**事件流格式定稿**
- [待办] py 版阅读工具（面向 shell；给其他 agent 用）

### 待确认事项（小）
- S2 遗留①："Stop 后、in-flight 事件中新发起的子执行"行为规格（当前不空转）——待与用户确认（可并入后续确认批次）

### I4: 委托与验收形式 {部分解决}
- S1/S2 流程验证完成（含抽查验收）；S3 进行中

### I5: 阶段内并行性 {Pending}
