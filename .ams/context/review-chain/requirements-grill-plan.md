# Requirements Grill Plan — 审查链（逻辑序列化与关系导出）

> 本轮 grill 聚焦：**"引擎提供的对局内不变触发器"这一锚点前提与代码的出入**（卡级/效果级触发器实为实例级）。
> 调研基线见 [可行性报告.md](./可行性报告.md)；已暴露矛盾 C1–C6 见该报告第三节。

## 未决问题

### Q4: 触发器锚点的范围与标识方案 In Progress
- 前提（用户）：视图/效果序列化的基础是「引擎提供的**对局内不变**的触发器与 hookID」。
- 与代码核对：
  - ✅ hookID 稳定：`Updates.*` 常量（`src/Orc/Core/Updates.cs`）＋ `GameUpdates` 字符串。
  - ✅ **引擎级**触发器对局内稳定、共享：
    - 指挥 / 单位移动 / 单位攻击 / 造成攻击伤害（`src/Orc.Game/Commanding/CommandManager.cs:125-139`）
    - 防御归零检查 / HQ 归零检查（`CommandManager.cs:164-180`）
    - 再触发接收（`src/Orc.Game/Cards/RetriggerSystem.cs:87-92`）
    - 卡牌放置/清理处理器（`src/Orc/Cards/CardLifecycle.cs:223-242`，引擎级懒挂载共享实例）
  - ❌ **卡级**触发器是**每卡实例**构造创建：预打出/打出/部署/加入/单位化（`src/Orc.Game/Cards/UnitCard.cs:41-45`）、指令预报出/打出（`CommandCard.cs:34-35`）、使用反制（`CounterCard.cs:46`）——**不是引擎提供、非对局内唯一**，且 `Trigger.Name` 同名不消歧（所有单位卡的部署触发器都叫「部署触发器」）。
  - ❌ **效果级**触发器也是**每效果实例**构造创建：被动效果生命周期触发器（`src/Orc/Cards/Effect.cs:239`）、主动效果施放触发器（`Effect.cs:320`）。
- 影响：效果 `Inject` 的目标（如 `AttackDamageTrigger` 是引擎级；但也可能注入卡级触发器）如何被稳定引用？
- 待定（选项见本轮提问）：Q4a 锚点范围；Q4b TriggerId 形态。

### Q3: 效果反序列化的用途与时机 Pending（Q2 后重定）
- 已定：**效果自身**是唯一可反序列化单元（视图为单向快照）。
- 待定：效果反序列化用于——存档重建 / 复装到卡 / 卡定义声明 / 跨对局移植？时机＝装载时 / 运行期？

### Q5: 效果序列化对"主动效果"的形态 Pending
- 矛盾 C1：主动效果主触发器不可挂载、无 HookID。
- 待定：主动效果序列化为纯动作型声明（无 hook）？还是排除出本期？

### Q6: "预制体"定义 Pending
- 用户原述：「其他 handler 和触发器的『预制体』的声明」。
- 待定：可复用模板 / 主子触发器声明 / 其它语义。

### Q7: 编排管理器的承载位置 Pending
- 待定：扩展现有 `TriggerRegistry`（`src/Orc.Game/Triggers/TriggerRegistry.cs`）/ 新建独立管理器 / 内核侧实体。

### Q8: 静态图的覆盖范围 Pending（Q1=a 新开）
- 矛盾 C5：Bus 不是效果全集（主动效果不挂总线；词条内嵌效果经独立通道 `DeathrattleKeywordComponent.EmbedEffect`）。
- 待定：静态图覆盖——卡上效果 / 词条内嵌效果 / 引擎内置流程触发器 / 主动效果；起点是否＝`LogicEngine.Cards` + 内置触发器登记 + Bus。

## 已决问题

### Q1: 审查链的"链"语义 Completed
- 决策：**a —— 仅声明期静态图**。运行期实际调用链由 `EventStream`/`EventStreamJson` 承担，不重复建设。

### Q2: 序列化的往返目标与逻辑本体表示深度 Completed
- 决策：**a —— 视图为单向快照（只读导出，不反序列化）**；
  **仅"效果自身"可反序列化**；触发器与 hookID 作为**对局内不变的引擎锚点**（不序列化，靠标识引用）。
- 影响：R1 逻辑本体落为"标识层"；不需要完整双向序列化；handler 本体经 ID → 代码注册表回指。
