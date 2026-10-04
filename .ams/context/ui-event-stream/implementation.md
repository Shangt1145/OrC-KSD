# Implementation — UI 消费桥接（引擎内部信号 → 表现层）

设计已定稿（实现 grill 闭合）。本文档描述"UI 如何监听 Orc 引擎内部信号来更新表现"的桥接设计与实现步骤。**本文档即实现计划**。

> 实现状态：内核（S1–S5、S8）与游戏层动作入口包载（S6）**已实现并通过测试**；对接文档（S7）已产出（`ui-integration-guide.md`）。

## Scope
- Target: `src/Orc`（内核桥接层：动作作用域 / 段聚合 / 段对象 / 段暂存队列与拉取 / 即时更新监听口）+ `src/Orc.Game`（动作入口包载）。
- Excludes（后续任务，本任务不做）: 联网/跨机**统一引用**、Nexum、对局拓扑、快照对齐、表现编排元数据。

## 两条 UI 通道

UI 监听引擎内部信号共两条**独立**通道：

| 通道 | 方向 | 用途 | 形态 |
|---|---|---|---|
| **段通道** | UI **轮询拉取** | 播放完整视觉表现 | 引擎每个动作产出一段 → 入待取队列；UI `TakeSegments()` 轮询取段 |
| **即时更新通道** | 引擎**调用** UI handler | 即时反馈 | UI 注册**非阻塞** handler；引擎在每次 `Emit` 时点调用（监听全部更新） |

## 桥接总览：UI 监听流程

```
UI 初始化阶段                       对局运行期（每次动作）                 UI 表现层（帧循环）
──────────────                      ────────────────────────              ──────────────────
① UI: 注册即时更新 handler（非阻塞）
② （游戏层动作入口内部）engine.BeginAction()
   ── 由 Match/PlayManager 动作方法包载，无需 UI 关心
                                              │
                                    驱动方 await match.EndTurn()
                                              │
                          ┌────────── 动作作用域 Begin ──────────┐
                          │  Emit → update 条目（引擎内部信号）   │
                          │  订阅者执行 → 子流（递归）            │
                          │  异常/报错 → log 条目                │
                          └────────── 动作作用域 End（封段）─────┘
                                              │
                                    段入待取队列（FIFO，取走即移除）
                                              │
                                              ▼
                                    ③ UI 帧循环: TakeSegments() 轮询取段
                                              │
                                    ④ 异步播放表现（引擎不等待）
```

**要点**：
- **UI 在初始化阶段只注册一次**（即时更新 handler）；段通道无需注册，直接轮询。
- 每个对局动作，引擎**自动**产出一段（含完整因果与报错）；UI 在**自己的帧循环**里轮询取段并异步播放，**引擎绝不等待 UI**。
- 详细对接说明见 [`ui-integration-guide.md`](./ui-integration-guide.md)。

## Implementation Steps

### S1: 动作作用域原语
- Status: Done
- Target: `src/Orc/Core/LogicEngine.cs`（新增公开面）＋新类型（形如 `ActionScope`）。
- Approach: `BeginAction()` 返回可释放作用域（`await using` 使用）；引擎以 **`AsyncLocal` 维护作用域栈**（同 `ExecutionFrame` 手法，跨 `await`、支持异步嵌套）；**仅最外层作用域**退出时封段；内层进入不新建段（外层优先、内层合并）。**异常路径**：`finally` 中封段入队，产出"已发生部分"。
- Acceptance:
  - [ ] 嵌套作用域只产出外层一段
  - [ ] 动作抛异常时仍产出"已发生部分"的段
  - [ ] 未处于作用域时（如裸 `Emit`）不产段、不报错
- Rationale: I3=a/x1；引擎无"动作"概念，须由显式作用域补足；异常不丢已发生事实。
- Terminology: [事件流](../../../CONTEXT.md)

### S2: 段聚合（引擎内部信号的收集）
- Status: Done
- Target: `src/Orc/Core/EventStream.cs`（加性内部读面）＋新逻辑（段聚合）。
- Approach: 作用域开始记录 RootStream 的**游标**（本地条目计数、子流计数）；结束取"游标之后的新增本地条目 + 新增子树"。因外部动作调用不在执行帧内，`Emit` 条目直接写 RootStream、订阅者子流直接挂 RootStream，故"本段"＝RootStream 该区间的增量。**段含本段全部条目（不限 kind：update / log / attach / 异常与 validation 留痕）与递归全部子树**。
- Acceptance:
  - [ ] 段内容＝本段新增条目（含报错）＋新增子树；不含历史条目
  - [ ] 段起点为本段第一个条目（R2）
- Rationale: R2/R7；I4=a；用户要求"报错信息也要让 UI 消费"。
- Terminology: —

### S3: 段对象（EventSegment）
- Status: Done
- Target: `src/Orc/Output/EventSegment.cs`（新）。
- Approach: 自包含段对象：内容（本段新增条目＋子树，持引用）＋元信息（**段号**〔**引擎实例内**单调递增〕、**时间戳**〔UTC〕）。为实现上"不暴露 RootStream 全量读面"（R2），段只引用本段对象。**不重复放置**可由段内条目推导的信息（动作/回合/玩家）。
- Acceptance:
  - [ ] 段可独立枚举本段条目与子树
  - [ ] 段号在引擎实例内单调递增、可排序
- Rationale: Q3=b/Q8=a/s1；单一事实来源；引用安全（LogEntry 不可变）。
- Terminology: —

### S4: 段暂存队列 + 拉取接口
- Status: Done
- Target: `src/Orc/Core/LogicEngine.cs`（新增）。
- Approach: 引擎维护**待取段队列**（FIFO，**无上限**）；UI 经 `TakeSegments()` / `TryTakeSegment(out EventSegment)` **轮询**拉取，取走即移除；过滤由 UI 自持。
- Acceptance:
  - [ ] 拉取即取走；队列 FIFO
  - [ ] UI 可纯轮询消费（不依赖通知）
- Rationale: I8=a/I5=a；用户定为"轮询 / 拉模式"。
- Terminology: —

### S5: 段产出与入队（交付语义）
- Status: Done
- Target: 段产出实现（`LogicEngine` 内）。
- Approach: 动作作用域结束点封段（S2/S3）→ **入待取队列**；段消费由 UI 轮询完成，**不依赖精确通知信号**；引擎不等待 UI、不阻塞。
- Acceptance:
  - [ ] 引擎推进不因 UI 播放而停顿（R4）
  - [ ] 段入队后可由 UI 任意时机拉取
- Rationale: 用户定为"轮询、不依赖精确信号"。
- Terminology: —

### S6: 游戏层动作入口包载
- Status: Done
- Target: `src/Orc.Game/Match/*`、`src/Orc.Game/Managers/PlayManager.cs`、`src/Orc.Game/Commanding/CommandManager.cs`。
- Approach: 在 R5 列出的对局级动作入口（`Match.Initialize`/`EndTurn`/`ShuffleDeckAsync`；`PlayManager` 五入口；`CommandManager.BeginCommandAsync`）方法体包一层动作作用域；嵌套调用（`Initialize` 内 `ShuffleDeckAsync`、`BeginUnitPrePlay` 内 `PlayUnitAsync`、指挥链内移动/攻击）由 S1 的栈语义自动合并。
- Acceptance:
  - [x] 每个公开动作入口恰好产出一段
  - [x] `Initialize` 内子动作不单独成段
- Rationale: R5。
- Terminology: —

### S7: UI 接入说明（对接文档）
- Status: Done
- Target: `ui-integration-guide.md`（已产出草案，随实现同步更新）。
- Approach: 面向 Godot C# 前端开发者，描述：①注册即时更新 handler；②轮询取段；③段对象结构（条目/子树/元信息/报错/引用表示）；④帧循环消费与异步播放；⑤取消订阅；⑥约束（handler 不得阻塞、段无上限）；⑦**信号来源**（业务更新清单＋框架留痕）与**无来源时如何新增信号**。
- Acceptance:
  - [x] 说明覆盖"注册 → 取段 → 异步播放 → 取消"全流程
  - [x] 说明 UI 信号来源与扩展方法（§8/§9）
- Rationale: 用户核心诉求＝"描述 UI 如何监听引擎内部信号来更新"。
- Terminology: —

### S8: 即时更新监听口（非阻塞）
- Status: Done
- Target: `src/Orc/Core/LogicEngine.cs`（新增）。
- Approach: 新增**专用注册口**（运行期多注册、返回 `IDisposable`、注册序）；handler **机制上不可阻塞引擎**（不返回可等待对象 / 引擎不 `await`）；监听**全部 `Emit` 更新**（过滤由 UI 自持）；与段轮询通道、既有 `Subscribe` 并存互不影响；异常隔离沿用现有口径。
- Acceptance:
  - [ ] handler 机制上不可阻塞引擎
  - [ ] 与 `Subscribe`、段轮询通道并存互不影响
- Rationale: I2=b/r1；用户要求"强制不阻塞"。
- Terminology: —

## 通道关系与兼容（I6）

- **段通道**：**新增**，是本任务的核心交付。
- **即时更新通道**：**新增**专用口（S8），非阻塞；既有 `Subscribe` **保留不动**（供需要"可 await / 完整环节"的场景），`IEngineBridge` 亦保留。
- 三者在 `Emit` 时点的关系：既有 `Subscribe`/`Bridge` 沿用现有顺序；新增即时更新口与段产出（作用域结束）互不干扰。
