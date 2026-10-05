# Requirements — 审查链（逻辑序列化与关系导出）

在任一时刻从引擎静态声明面导出"全域效果/触发器/handler 关系图"（审查链），并支持以**预制体**为单位的逻辑文本化与效果序列化/反序列化/运行时装载。
本文件为需求 grill 定稿产物（**全部条目 Confirmed**）。

## Scope

- Includes：
  - **定义级引用锚点**：HookId / TriggerId / 事件标识；hook 名 ↔ hookID 双向互转；hook 空间开放但首次登记即对局内固化；保留自由字符串语义。
  - **编排管理器**（内核）：集中登记 handler ↔ 触发器 ↔ hook 关系，含反向依赖；可输出以指定触发器为起点的**声明期可达链**。
  - **审查链导出**：覆盖**全域**（引擎内置触发器 + 卡上效果 + 主动效果 + 未装载效果 + 词条内嵌效果 + 动态效果/预制体）；输出 = **JSON 真源 + 派生文本视图**。
  - **三层预制体**：效果预制体 → 引用 → 触发器预制体 + 事件预制体（handler）。
  - **预制体管理器**：显式注册（程序集来源）+ 本地明文目录加载（csx 来源）+ 委托解析 + **moding 支持**。
  - **效果序列化/反序列化**：快照（被动/主动两种形态）+ 从文本重建动态效果 + 运行时装载计划；moding 纳入快照。
  - **csx 执行**：satellite 工程 + `IScriptEvaluator` 接口倒置（内核零依赖）+ SecureCSharpEval。
- Excludes：
  - 运行期实际因果链导出（由 `EventStream` / `EventStreamJson` 承担，不重复建设）。
  - 触发器 / hookID 本体的完整序列化/反序列化（引擎词汇表，仅作引用锚点）。
  - 在线预制体仓库 / 工坊（**仅本地分发**）。
  - 进程级隔离沙箱（后置加性项）。
  - 现有 C# 效果体系的迁移（保留不动；后续新卡走预制体）。

## Constraints

- 遵守既有 P1（禁止无条件数据更新）/ P2（更新不允许异步）。
- 对内核 `Trigger` / `Bus` 的改动须为**加性**（不破坏既有语义与测试）。
- 内核 `Orc` 保持**零包依赖**；Roslyn 仅在 satellite 工程。
- csx 社区内容：SecureCSharpEval **弱沙箱**（同进程、命名空间白/黑名单、关键字过滤、超时）+ **明文储存**；已知非强隔离。
- 遵守 `.ams/context/CONTEXT.md` 与 [review-chain/CONTEXT.md](./CONTEXT.md) 术语。

## Requirement Items

### R1: 单向序列化与引用锚点
- Status: Confirmed
- Scenario/Trigger: 引擎初始化与运行期任一时刻。
- Behavior: 引擎应能**单向**导出挂载面（"什么时候该调用"）与反向依赖（"我该被什么调用"）；逻辑本体以**定义级稳定标识**表示；触发器 / hookID 作为对局不变的引用基础。
- Acceptance:
  - [ ] TriggerId / HookId / 事件标识为定义级、跨对局稳定。
  - [ ] 提供 hook 名 ↔ hookID 双向互转。
  - [ ] 可导出挂载面（hook 列表 / 挂载优先级 / 所有者 / band / priority / 注册序）。
  - [ ] 可导出反向依赖（某 hook 上触发器/handler 的有序快照）。
  - [ ] handler 可声明其可能触发的**下游触发器**（含主动触发器）。
  - [ ] 初始化时收集全部底层触发器的 HookID 并在引擎内维护。
- Terminology: [触发器](../CONTEXT.md)、[Hook](../CONTEXT.md)、[事件](../CONTEXT.md)、[效果](../CONTEXT.md)、[审查链](./CONTEXT.md)

### R2: 编排管理器与审查链
- Status: Confirmed
- Scenario/Trigger: 需要审查 / 诊断时。
- Behavior: 所有 handler 与触发器在**编排管理器**处登记关系；可输出以指定触发器为起点的**声明期可达链**（触发器为节点、handler 为链）；覆盖**全域**；输出 = **JSON 真源 + 派生文本视图**。
- Acceptance:
  - [ ] 集中登记 handler ↔ 触发器 ↔ hook 关系。
  - [ ] 可输出以指定触发器为起点的可达链（静态声明图）。
  - [ ] 覆盖全域：引擎内置触发器 + 卡上效果 + 主动效果 + 未装载效果 + 词条内嵌效果 + 动态效果/预制体。
  - [ ] 导出 JSON 真源（节点-边）并可派生人类可读文本视图。
  - [ ] 链语义 = **声明期**（不涉及运行期实际调用）。
- Terminology: [审查链](./CONTEXT.md)、[编排管理器](./CONTEXT.md)、[触发器](../CONTEXT.md)、[Hook](../CONTEXT.md)

### R3: 预制体与效果序列化
- Status: Confirmed
- Scenario/Trigger: 导出 / 存档 / 加载效果时。
- Behavior: 三层**预制体**（效果 / 触发器 / 事件）；**预制体管理器**（显式注册 + 本地明文目录）；效果快照可**反序列化**并生成运行时装载计划；csx 经 satellite + 接口倒置执行。
- Acceptance:
  - [ ] 三层预制体模型（效果预制体引用触发器预制体与事件预制体）。
  - [ ] 预制体管理器：显式注册（程序集来源）+ 本地明文目录加载（csx 来源）+ 委托解析。
  - [ ] 被动效果快照 = {主触发器, 挂载 HookID, 其它预制体声明, moding 声明}。
  - [ ] 主动效果快照 = 动作型声明（主触发器 Active + 施放事件预制体 + moding，**无挂载 HookID 段**）。
  - [ ] moding 纳入快照并可重建（保留运行期注销/回退语义）。
  - [ ] 从文本重建"携带完整逻辑的动态效果"，并生成运行时装载计划（进既有 `Card.Effects` + 装载链）。
  - [ ] csx 执行经 satellite 工程 + `IScriptEvaluator` 接口倒置，**内核零依赖**。
  - [ ] 集成 SecureCSharpEval（弱沙箱，已知限制登记于 Constraints）。
- Terminology: [预制体](./CONTEXT.md)、[预制体管理器](./CONTEXT.md)、[效果](../CONTEXT.md)、[注入](../CONTEXT.md)、[动态效果](./CONTEXT.md)
