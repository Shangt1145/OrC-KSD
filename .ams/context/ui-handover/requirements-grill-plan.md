# Requirements Grill Plan — UI 开发者交接文档

> 任务：产出一份面向 UI 开发者的交接文档，聚焦①事件流消费 ②注入 handler 监听即时更新 ③UI 输入集成。
> 交付物：`docs/ui-开发者交接文档.md`（自包含）。
> 基线材料：`.ams/context/ui-event-stream/ui-integration-guide.md`（已覆盖①②）、`docs/orc-用户输入点分析报告.md`、代码侧 `Orc.Game.GameEntryPoints` / `GameHooks` / `Orc.Core.LogicEngine` / `Orc.Game.Targeting.*`。

## 未决问题
（无）

## 已决问题

### Q7: 第 3 重点「集成逻辑」的深度覆盖项 Completed
- 决策：**a + a1** —— 完整集成逻辑：①前置门禁（相位/归属/终局）②结果对象三态读法（`PlayResult`/`CommandResult`/`TargetingResult`；不抛、类别化判断）③目标应答异步倒置全流程（收集→Begin→Complete/Cancel、requestId 配对、拒绝可重试、终局恰好一次）④常见坑；并含「Match → 各 Manager 速查表 + 预览面（`GetCommandAvailability`/`CommandAvailability`）」，不展开各 Manager 全部读方法。

### Q6: 与既有文档的关系 Completed
- 决策：**a + a1** —— 自包含 + 互链 + 单真源声明：一二部分基于现有 guide 重述精华；信号清单给「精简快照表（组别+信号名+载荷键，注明快照、以代码常量为权威）」并指向原 guide §8；文末「相关文档」互链；**不改动任何既有文件**。

### Q5: 交付形态要求 Completed
- 决策：**a** —— 查表 + 上手示例 + 端到端串联：每重点给「结构化说明 + 关键 API 签名表 + 最小代码示例」；另给一条端到端串联示例（回合开始 → 出一张牌含目标选择应答 → 帧轮询取段播放）与一张时序图。

### Q1: 文档落点与与现有 guide 的关系 Completed
- 决策：**a** —— 新建自包含交接文档于 `docs/`（`docs/ui-开发者交接文档.md`）；一、二部分复用/引用现有 `ui-integration-guide.md` 结论并补齐，第三部分「UI 输入集成」为全新内容。

### Q2: 受众与技术栈假设 Completed
- 决策：**a** —— Godot C# 前端 + 同进程同语言（沿用 ui-event-stream 部署前提）；示例用 Godot `Node`/`_Process` 风格伪代码，仅依赖引擎公开面、不引入具体控件库。

### Q4: UI 输入集成的范围 Completed
- 决策：**b** —— 核心四类（对局/出牌/指挥/目标应答）＋在文档中明确标注 `PendingDelivery` 三项（`Concede`/`MulliganReplace`/`MulliganDone`）为「签名冻结、实现待交付」；**不含**门户·重发等非 UI 主动输入面。

### Q3: 「注入 handler 监听即时更新」的用词与范围 Completed
- 决策：**a** —— 以 `OnImmediateUpdate`（术语＝「即时更新监听」）为主，同一章并列对比 `OnImmediateUpdate` / `Subscribe` / `IEngineBridge`（可 await 与否、单点 vs 多注册、异常隔离），并**明确纠正措辞**：是 UI 向引擎**注册**非阻塞 handler，不是"向引擎注入 handler"。
