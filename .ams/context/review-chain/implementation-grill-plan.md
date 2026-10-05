# Implementation Grill Plan — 审查链（逻辑序列化与关系导出）

> 当前聚焦：**csx 编译上下文与入口契约**（可用引用集、入口函数约定、与 SecureCSharpEval 命名空间黑白名单的协同）。
> 需求基线见 [requirements.md](./requirements.md)；实现文档见 [implementation.md](./implementation.md)。

## 未决问题

（无未决项；实现 grill 已收束，见 [implementation.md](./implementation.md)。）

## 已决问题

### Q-I1: 标识的生成与分配 Completed
- 决策：**a —— 确定性派生**。触发器与 hook 引入**稳定键（作者声明的字符串）**；ID = 稳定键的 64-bit 稳定哈希（FNV-1a/SHA 截断）；注册表维护 键↔ID 双向映射 + 碰撞检测；跨进程/跨机器一致、不依赖装配顺序。

### Q-I2: 触发器"稳定键"的声明方式 Completed
- 决策：**a —— 构造参数（加性、可选）**。`Trigger<TView>` 新增可选 `stableKey`，身份随实例走；未声明回退派生键并标"弱身份"；框架内置件由框架统一给键。

### Q-I3: 编排管理器的登记入口 Completed
- 决策：**c —— 混合**。内核在 `Bus.Mount` / `Effect.Inject` 自动采样"挂载/注入"面；装配方显式登记"未挂载的主动触发器 / 动态触发器 / 下游声明边"。

### Q-I4: 声明式下游边的表达 Completed
- 决策：**a —— 注册面加参数**。`Trigger.Register` 加性重载携带 `downstream`（下游 TriggerId/键）；预制体实例化把事件预制体的声明翻译成该参数（动态与内置收敛到同一机制）。

### Q-I5: 效果快照 DTO 与装载计划结构 Completed
- 决策：**a —— 嵌套 DTO（镜像三层预制体）+ 显式装载计划**。快照 = 效果预制体{触发器预制体{事件预制体}} + 挂载 HookID + moding；反序列化产出**有序装载计划**（挂主触发器 / 注册事件 / 施加 moding），由装载器执行并落既有 `Card.Effects` + 装载链。

### Q-I6: csx 编译时点、缓存与执行接口 Completed
- 决策：**c —— 两级**。内置/官方构建期预编译；社区动态运行期按需编译 + 源码哈希缓存；AOT 端降级为仅预编译。
- 接口形状：内核定义 `IScriptEvaluator`（零依赖），satellite 实现；以"视图类型 + 源码 + 入口名"为参数、返回擦除形态委托，调用侧按 `TView` 适配。

### Q-I7: csx handler 的引擎引用获取方式 Completed
- 决策：**a —— `Context` 加性暴露引擎**（`Context.Engine`，由 `InvokeAsync`/执行帧填充）。csx 入口从 ctx 取引擎，与 C# handler 路径统一。

### Q-I13: csx 编译上下文契约 Completed
- 决策：**a —— 最小白名单 + 具名入口**。可用引用 = `Orc` / `Orc.Game`（视图类型）/ 基础 BCL（`System`、`Collections.Generic`、`Linq`、`Threading.Tasks`），与 SecureCSharpEval 黑白名单对齐；入口 = 预制体声明的入口名（默认 `HandleAsync`），签名 `Task(TView, Context, CancellationToken)`。

### Q-I9: 卡如何声明"使用效果预制体" Completed
- 决策：**a —— 扩展 `CardEffectRegistry`（加性新面）**。新增 `DeclarePrefab(cardId, prefabIds)`；装载链在代码效果之后、按声明序装载预制体效果（沿用 `CardEffectLoader` 既有顺序与失败口径）。

### Q-I8: 审查链节点粒度 Completed
- 决策：**a —— 种类节点 + 实例清单（单层图）**。图为种类级（TriggerId 节点）；每个种类节点携带 instances[]（宿主/归属/来源/装载态）。

### Q-I11: 枚举面补全 Completed
- 决策：**a —— 内核加性只读枚举面**（`Bus` 全量 hook、`Trigger` 全量事件、`Effect` 注入登记面；编排管理器聚合全域）。反射路径不可行（`_rollbacks` 是闭包，还原不出目标）。

### Q-I12: "序列化"的范围边界 Completed
- 决策（自主定）：**只有预制体（动态）效果可序列化**；既有 C# 效果（`DeathrattleEffect` 等）仅作审查图节点，导出时标 `serializable:false`。

### Q-I10: 快照的版本与引用形态 Completed
- 决策（自主定）：快照/预制体带 `schemaVersion`；内置预制体引用 = `{prefabId, version}`；版本不匹配＝**结构化失败**（不静默）。

### Q-I14: 底层触发器 HookID 的收集时点 Completed
- 决策（自主定）：在**对局装配期**收集（与既有 `TriggerRegistry.Register` 同批）；引擎侧登记面由编排管理器（S3）承载；内核侧提供 HookID 机制（S1）。
