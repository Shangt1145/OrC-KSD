# Implementation — 审查链（逻辑序列化与关系导出）

实现 grill 已收束，本文件为实现计划（定稿）；按 S1→S11 顺序实施，先内核后 satellite 与游戏层接线。

## Scope

- Target：内核 `Orc`（标识 / 枚举面 / 编排管理器 / 导出 / 预制体模型）+ 新 satellite `Orc.Script`（csx 执行）+ 游戏层注册接线。
- Excludes：在线预制体分发、进程级隔离沙箱、现有 C# 效果体系迁移、运行期因果链导出（既有 `EventStream` 承担）。

## 跨步骤定稿口径

- 标识：稳定键 → FNV-1a 64 位哈希即 ID；注册表双向映射 + 碰撞检测（Q-I1）。
- 触发器稳定键：`Trigger<TView>` 构造新增可选 `stableKey`；未声明回退派生（Name／视图类型名）并标弱身份（Q-I2）。
- 登记：内核自动采样（`Bus.Mount`／`Effect.Inject`）+ 装配方显式登记（未挂载主动触发器／动态／下游边）（Q-I3）。
- 下游边：`Register` 加性重载携带 `downstream`（Q-I4）。
- 快照：嵌套 DTO（三层预制体）+ 显式有序装载计划（Q-I5）。
- csx：两级编译（内置预编译／社区运行期按需 + 哈希缓存；AOT 降级为仅预编译）（Q-I6）。
- 引擎引用：`Context.Engine` 加性暴露（Q-I7）。
- csx 契约：最小白名单引用集（与 SecureCSharpEval 对齐）+ 具名入口 `HandleAsync`（Q-I13）。
- 卡 → 预制体：`CardEffectRegistry.DeclarePrefab(cardId, prefabIds)`（加性）（Q-I9）。
- 节点粒度：种类节点 + `instances[]` 实例清单，单层图（Q-I8）。
- 枚举面：内核加性只读枚举（Q-I11）。
- 序列化范围：仅预制体（动态）效果可序列化；C# 效果仅审查节点（`serializable:false`）（Q-I12）。
- 版本：`schemaVersion` + `{prefabId, version}` 引用；不匹配＝结构化失败（Q-I10）。
- 收集时点：对局装配期收集底层触发器 HookID（Q-I14）。

## Implementation Steps

### S1: 定义级标识层
- Status: Done
- Target: `src/Orc/Core/Identifiers.cs`（新）、`src/Orc/Core/HookRegistry.cs`（新）、`src/Orc/Core/Trigger.cs`（改）
- Approach: 新增 `HookId`/`TriggerId`/`EventKey` 稳定标识（FNV-1a 64）；`HookRegistry` 名字↔ID 双向映射 + 碰撞检测 + 登记序；`Trigger<TView>` 构造新增可选 `stableKey`，暴露 `StableKey`/`TriggerId`/`HasDeclaredStableKey`；`LogicEngine` 暴露 `Hooks`。
- Acceptance:
  - [ ] hook 名 ↔ hookID 双向互转。
  - [ ] 同名恒同 ID；跨进程/跨机器一致。
  - [ ] 未登记的自由 hook 字符串仍可用。
  - [ ] `Trigger` 未声明 stableKey 时可回退派生键并标弱身份。

### S2: 内核加性只读枚举面 + `Context.Engine`
- Status: Done
- Target: `src/Orc/Core/Bus.cs`、`Trigger.cs`、`Context.cs`、`src/Orc/Cards/Effect.cs`
- Approach: `Bus.EnumerateHooks()`（全量 hook + 订阅者）；`Trigger<TView>.EnumerateEvents()`（事件：名/band/priority/seq/downstream）；`Effect.EnumerateInjections()`（目标触发器 + 句柄；随 `_rollbacks` 同步维护一条可读登记）；`Context.Engine`（由执行帧填充）。
- Acceptance:
  - [ ] 可枚举 Bus 全部已订阅 hook 与订阅者标识。
  - [ ] 可枚举触发器全部事件（含运行期注册项）。
  - [ ] 可从效果反查注入项（目标触发器 + 事件标识）。
  - [ ] `Context.Engine` 在执行期可读、执行外为 null。

### S3: 编排管理器
- Status: Done
- Target: `src/Orc/Core/Orchestration.cs`（新）
- Approach: 登记 `触发器（TriggerId/稳定键/实例宿主/装载态）`、`hook → 订阅触发器`、`触发器 → 事件（含 downstream）`、`效果 → 注入项`；反向索引；`QueryChain(TriggerId)` 做 BFS/DFS 可达链；自动采样钩子接 `Bus.Mount`／`Effect.Inject`。
- Acceptance:
  - [ ] 全域登记（引擎内置 + 卡上 + 主动 + 未装载 + 词条内嵌 + 动态）。
  - [ ] 反向依赖查询（某 hook 的订阅者有序快照）。
  - [ ] 可达链输出（声明期）。

### S4: 审查链导出（JSON 真源 + 文本视图）
- Status: Done
- Target: `src/Orc/Output/AuditChainJson.cs`、`AuditChainText.cs`（新）
- Approach: 由编排管理器导出种类节点 + instances[] 的 JSON（复用 `JsonValueWriter` 降级口径）；再派生人类可读文本；C# 效果标 `serializable:false`。
- Acceptance:
  - [ ] JSON 真源（节点＝触发器种类，边＝事件 + downstream，节点含 instances[]）。
  - [ ] 派生文本视图。
  - [ ] 空图/未登记项降级不抛。

### S5: 预制体模型与效果快照 DTO
- Status: Done
- Target: `src/Orc/Cards/Prefabs.cs`（新）
- Approach: 三层预制体（效果/触发器/事件）+ 效果快照 DTO（被动/主动）+ `schemaVersion` + 引用 `{prefabId, version}` + moding 声明位。
- Acceptance:
  - [ ] 三层可互相引用。
  - [ ] 被动/主动快照结构可表达（主动无 hook 段）。
  - [ ] 版本字段与引用形态就位。

### S6: `IScriptEvaluator` + `Orc.Script` satellite
- Status: Done
- 备注（与 Q7 字面的偏离）：satellite **引用了 Orc**（构造擦除形态委托需 `Context`/视图类型）；内核仍**零包依赖**（依赖方向 `Orc.Script → Orc`，内核不反向依赖）。
- 备注（安全）：按 Q10 决策自实现等价限制（引用白名单 + 导入白名单 + 关键字过滤 + 超时），未引入 SecureCSharpEval 第三方源。
- Target: `src/Orc/Scripting/IScriptEvaluator.cs`（新）+ 新工程 `src/Orc.Script/`
- Approach: 内核定义零依赖接口；satellite 引 Roslyn + SecureCSharpEval，实现"视图类型 + 源码 + 入口名 → 擦除形态委托"，含哈希缓存与结构化失败；AOT 降级为仅预编译。
- Acceptance:
  - [ ] 内核不引 Roslyn（工程产物检查为证）。
  - [ ] satellite 可编译 csx → 委托（含失败结构化）。
  - [ ] 缓存命中不重复编译。

### S7: 预制体管理器
- Status: Done
- Target: `src/Orc/Cards/PrefabManager.cs`（新）
- Approach: 显式注册 API（程序集来源、fail-fast、重复拒绝）+ 本地明文目录扫描加载（csx）+ 委托解析 + moding 注册/注销面。
- Acceptance:
  - [ ] 显式注册与重复拒绝。
  - [ ] 本地目录加载与解析。
  - [ ] moding 面可用。

### S8: 动态效果与装载计划执行
- Status: Done（`Orc` 侧 + `Orc.Game` 侧 `CardEffectRegistry.DeclarePrefab` / `CardEffectLoader` 接线均已实现并端到端验证：`tests/Orc.Game.Tests/PrefabEffectLoadingTests.cs`）
- Target: `src/Orc/Cards/DynamicEffect.cs`（新）+ `EffectSystem` 接线
- Approach: 效果预制体实例化 → csx 重建 handler → `Effect` 包装 → 生成有序装载计划（挂主触发器 / 注册事件 / 施加 moding）→ 落既有 `Card.Effects` + 装载链；新增 `CardEffectRegistry.DeclarePrefab`。
- Acceptance:
  - [ ] 从快照重建等价动态效果。
  - [ ] 装载计划经既有装载链生效。
  - [ ] 与现有 C# 效果并存不冲突。

### S9: moding 序列化与重建
- Status: Done
- Target: `Prefabs.cs` / `DynamicEffect.cs`
- Approach: 快照声明 moding 项（目标注册项 + 替换逻辑预制体）；实例化按序重建 moding 栈。
- Acceptance:
  - [ ] moding 往返后行为等价（含注销回退）。

### S10: 主动效果动作型快照
- Status: Done
- Target: `src/Orc/Cards/Effect.cs`（`ActiveEffect`）
- Approach: 动作型声明（主触发器 Active + 施放事件预制体 + moding，无 hook 段）；施放沿用 `CastAsync`；允许其它被动触发器调用（下游边指向主动 TriggerId）。
- Acceptance:
  - [ ] 主动快照无"挂载 HookID"段。
  - [ ] 重建后可经 `CastAsync` 施放。

### S11: 测试与 CONTEXT 同步
- Status: Done（`Orc.Tests` 290 通过、`Orc.Script.Tests` 9 通过、`Orc.Lua.Tests` 157 通过；术语已登记 review-chain/CONTEXT.md）
- Target: `tests/Orc.Tests/`
- Approach: 标识互转、枚举面、反向依赖、可达链、快照往返、动态效果装载、moding 往返各覆盖用例；术语同步 CONTEXT。
- Acceptance:
  - [ ] 既有测试不回归。
  - [ ] 新能力逐条验收。
