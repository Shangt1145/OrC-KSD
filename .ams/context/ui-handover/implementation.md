# Implementation — UI 开发者交接文档

本文档即实现计划：交付 `docs/ui-开发者交接文档.md`。**待 grill 确认大纲后定稿。**

## 拟定章节大纲

```
0. 文档头：状态/日期/受众（Godot C# 前端）/范围（三条主线）＋本任务排除项
   ＋ 一句话模型 ＋ 术语速览（表：事件流段/段轮询/即时更新监听/接入点/观察面/扩展点/Hook 禁用说明）

1. 通用接入骨架：如何取得 Match.Engine；注册时机（Initialize 前）；帧循环位；生命周期（Dispose）

2. 【重点一】事件流消费（段轮询）
   2.1 模型与两条 UI 通道对比（段轮询 vs 即时更新）
   2.2 API 签名表（BeginAction / TakeSegments / TryTakeSegment / EventSegment）
   2.3 EventSegment / LogEntry 结构
   2.4 最小示例（帧循环取段入播放队列）
   2.5 报错消费（Level==Error / Keywords 含 exception:）
   2.6 约束与坑（段无上限、必须持续取、同进程直接引用、段不含历史）
   2.7 信号来源精简快照表（组别+信号名+载荷键）＋「以代码常量为权威」声明 ＋ 指向原 guide §8

3. 【重点二】即时更新监听（注册 handler）
   3.1 措辞澄清：UI 向引擎「注册」非阻塞 handler，非"注入"
   3.2 三通道对比表（OnImmediateUpdate / Subscribe / IEngineBridge）
   3.3 API 签名表 + 最小示例
   3.4 约束与坑（非阻塞、监听全部更新自过滤、异常隔离、Dispose 幂等）

4. 【重点三】UI 输入集成
   4.1 输入模型：动作入口 + 异步倒置应答
   4.2 前置门禁（MatchState / MatchPhase / 终局）
   4.3 核心四类入口签名表（对局/出牌/指挥/目标应答），PendingDelivery 三项显式标注
   4.4 结果对象三态读法（PlayResult / CommandResult / TargetingResult / CommandAvailability）
   4.5 目标应答异步倒置全流程（CollectCandidatesAsync → BeginInteraction → Complete/Cancel；
       requestId 配对、拒绝可重试、终局恰好一次）＋ 桥接实现要点
   4.6 Match → 各 Manager 速查表 ＋ 预览面（GetCommandAvailability / CommandAvailability）
   4.7 约束与坑

5. 端到端串联示例：回合开始 → 出一张牌（含目标选择应答）→ 帧轮询取段播放 ＋ 时序图

6. 相关文档（互链）：ui-integration-guide.md / docs/orc-用户输入点分析报告.md /
   GameEntryPoints / GameHooks ＋ 单真源声明；不改动既有文件
```

## 拟采用的事实基线（写入文档需与代码一致）

- 段通道：`LogicEngine.BeginAction()`（`ActionScope`，`await using`，嵌套合并，最外层产出，零发射不产段）→ `TakeSegments()` / `TryTakeSegment(out EventSegment)`（FIFO、取走即移除、无上限）。
- 即时更新：`LogicEngine.OnImmediateUpdate(ImmediateUpdateCallback)`（`void`、非阻塞、全部 `Emit`、异常隔离、返回 `IDisposable`）；并列 `Subscribe(EngineUpdateCallback)`（`Task`、会被 await）与 `LogicEngine.Bridge`（`IEngineBridge`、单点强类型）。
- 输入四类：对局（`Match.Initialize/EndTurn/ShuffleDeckAsync`）、出牌（`PlayManager.BeginUnitPrePlayAsync/BeginCommandPrePlayAsync/PlayUnitAsync/JoinUnitAsync/PlayCommandAsync/UseCounterAsync`）、指挥（`CommandManager.BeginCommandAsync/BeginMoveAsync/BeginAttackAsync/GetCommandAvailability`）、目标应答（`TargeterManager.CreateTargeter` / `Targeter.Targeting` / `ITargeterBridge` / `ITargetingResponder`）。
- PendingDelivery：`Match.Concede` / `Match.MulliganReplace` / `Match.MulliganDone`（签名冻结、实现待交付）。
- 结果对象：三态 + 类别化原因；`TargetingResult.Outcome`（`TargetOutcome`：`Single`/`List`/`GetSelection`/`GetIdentifiers`/`GetSlotKind`）。

## Steps

### S1: 大纲与示例形态定稿
- Status: Done —— I1=a（大纲认可）、I2=b（第 5 章＝调用序列 + Mermaid 时序图）、I3=a1（第 4.5 章给 `ITargeterBridge` 骨架）、I4=b2（Mermaid 图）、I5=a1（含自检清单）。

### S2: 编写 `docs/ui-开发者交接文档.md`
- Status: Done —— 按大纲产出 0–7 章；事实与代码一致；未改动既有文件。

### S3: 自检
- Status: Done —— 三重点各有说明/表/示例；端到端＝调用序列 + Mermaid 时序图；含接入自检清单；信号清单为快照并声明以代码常量为权威；未写死行号（以符号名为准）。
- 修正记录：`Emit` 写入的 `update` 条目 `Source` 恒为 `"bus"`，信号名在 `Message`/`Keywords[0]`——已据此修正 §2.3/§2.4。
