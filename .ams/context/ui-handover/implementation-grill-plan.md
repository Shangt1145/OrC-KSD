# Implementation Grill Plan — UI 开发者交接文档

> 交付物：`docs/ui-开发者交接文档.md`（自包含单文档）。
> 实现＝文档编写；实现 grill 聚焦：大纲结构、示例形态、图形式、桥接骨架是否给模板。

## 未决问题
（无）

## 已决问题

### I2: 端到端串联示例的形态 Completed
- 决策：**b** —— 第 5 章＝「调用序列 + Mermaid 时序图」（不写完整 Node 伪代码；`ITargeterBridge` 骨架按 I3=a1 置于第 4.5 章）。

### I5: 是否含「接入自检清单」 Completed
- 决策：**a1** —— 含精简可勾选自检清单。

### I1: 文档大纲结构 Completed
- 决策：**a** —— 认可 `implementation.md` 拟定大纲（0 文档头/术语速览 → 1 通用接入骨架 → 2 事件流消费 → 3 即时更新监听 → 4 UI 输入集成 → 5 端到端串联 → 6 相关文档互链）。

### I3: 是否给「最小 ITargeterBridge 实现骨架」模板 Completed
- 决策：**a1** —— 给可直接照抄的骨架（`CollectCandidatesAsync` + `BeginInteraction(description, responder)` + 手势回调里 `responder.Complete(requestId, selectionsBySlot)` / `Cancel(requestId)`）。

### I4: 图形式 Completed
- 决策：**b2** —— Mermaid 图（GitHub/IDE 可渲染）。
