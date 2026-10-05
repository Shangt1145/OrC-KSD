# Requirements — 审查链（逻辑序列化与关系导出）

一条"审查链"：在某个时刻，从逻辑引擎的总线出发，生成所有效果的文本视图；并为此建立逻辑的序列化/反序列化支持、编排管理器与触发器链、效果序列化。本文件为需求对齐产物，**当前为初稿（Draft）**，随需求 grill 逐轮沉淀。

## Scope

- Includes：
  - 从引擎侧导出"所有效果"的**单向文本/结构化快照**（审查链；静态声明期关系图）。
  - 逻辑的序列化支持（编排层 / 自由逻辑层 / 逻辑本体＝标识层）。
  - 编排管理器：集中登记 handler↔触发器↔hook 关系，可输出"特定触发器形成的触发器链"。
  - 效果的序列化 + **效果自身的反序列化**（唯一双向单元）。
  - 触发器与 hookID 作为**对局内不变的引擎锚点**（不序列化，靠标识引用）。
- Excludes：
  - 运行期实际调用链（已由 `EventStream`/`EventStreamJson` 承担，不重复建设）。
  - 触发器/hook 的序列化与反序列化（它们是锚点）。
  - 视图的反序列化（视图仅单向导出）。
  - 逻辑本体的文本化（不引入 DSL/Lua 解释层）。

## Constraints

- 遵守既有 P1（禁止无条件数据更新）/ P2（更新不允许异步）。
- 对内核 `Trigger`/`Bus` 的改动须为**加性**（不破坏既有语义与测试）。
- 遵守 `.ams/context/CONTEXT.md` 既有术语（触发器 / Hook / 事件 / 效果 / 注入 / 总线）。

## Requirement Items

### R1: 逻辑序列化（三块）
- Status: Draft
- Scenario/Trigger: 引擎初始化与运行期任一时刻。
- Behavior: 引擎应能序列化——编排层（"什么时候该调用"）、自由逻辑层（"我该被什么调用" + "我的逻辑是什么"）。
- Acceptance:
  - [ ] 编排层（hook / 挂载优先级 / 所有者 / band / priority / 注册序）可导出。
  - [ ] 反向依赖（某 hook 上挂了哪些触发器/handler）可导出。
  - [ ] 逻辑本体以稳定标识表示，且**可反查**。（表示深度待 Q2）
  - [ ] 初始化时收集全部底层触发器的 HookID 并在引擎内维护。
  - [ ] 效果订阅包裹 handler ID 并记录于相关字典。
  - [ ] 允许 handler 声明其可能触发的触发器。
- Terminology: [触发器](../CONTEXT.md)、[Hook](../CONTEXT.md)、[事件](../CONTEXT.md)、[效果](../CONTEXT.md)

### R2: 编排管理器与触发器链
- Status: Draft
- Scenario/Trigger: 需要审查/诊断时。
- Behavior: 所有 handler 与触发器在某一管理器处登记关系；该管理器可输出"特定触发器形成的触发器链"（触发器为节点，handler 为链）。
- Acceptance:
  - [ ] 存在集中登记 handler↔触发器↔hook 关系的管理器。
  - [ ] 可输出以指定触发器为起点的链。
  - [x] 链语义 = **声明期静态可达图**（Q1 已决：运行期实际调用链由 EventStream 承担，不重复建设）。
- Terminology: [触发器](../CONTEXT.md)、[Hook](../CONTEXT.md)、[事件](../CONTEXT.md)

### R3: 效果序列化
- Status: Draft
- Scenario/Trigger: 导出/存档效果时。
- Behavior: 效果序列化为——一个主触发器 + 其需挂载触发器的 HookID + 其它 handler 与触发器的"预制体"声明。
- Acceptance:
  - [ ] 被动效果可序列化为 {主触发器, 挂载 HookID, 注入声明}。
  - [ ] 主动效果有明确形态（其主触发器不可挂载——见矛盾 C1）。
  - [ ] "预制体"有定义并被序列化承载。（待 Q6）
- Terminology: [效果](../CONTEXT.md)、[触发器](../CONTEXT.md)、[注入](../CONTEXT.md)
