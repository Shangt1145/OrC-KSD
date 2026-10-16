# Requirements Grill Plan — 面向 MCP 的引擎外部 API

> 本轮拷问聚焦：把 OrC 引擎能力（资产管理 / 对局交互 / 调试读取 / 文档读取）暴露为 MCP 可用的外部 API，其**外部可观察行为**如何定义。
> 术语一律沿用 `CONTEXT.md`；新增术语（沙盒模式、调试 API、引擎托管引用表、全局引用表）待定稿后记入 CONTEXT。
> 本轮整理：已把逐轮追加产生的重复段落合并，每个问题只保留最终状态。

## 基线事实（代码已确认，作为拷问对照面）

- 全仓 `mcp|jsonrpc` 零命中；`src/` 无 HTTP/CLI/stdio/宿主基础设施；唯一"引擎→外部"契约是 `src/Orc/Output/IEngineBridge.cs`（纯推送）。
- `EngineHost` 无 `Dispose/Stop`；`TryLoad/Unload/ReloadCardPack` 硬拒 `PackageKind.Plugin`（`EngineHost.cs` L329/L389/L465）⇒ **插件只能冷加载、进程内不可重启**。
- 全仓无对局驱动器/调度器；`Bus.Emit`/`TakeSegments`/`Subscribe`/注册表均为无锁同步语义，单线程假设。
- `Trigger.Hooks` 仅构造期声明且 `internal` ⇒ 外部无法新增订阅者。
- 唯一"外部驱动型交互"＝`ITargeterBridge.BeginTargeting(session)`（同步、不得阻塞）＋ `ITargeterSession.NextAsync()`（`SemaphoreSlim`，唯一天然跨线程往返点）；`BeginTargeting` **不带对局标识**。
- 逐卡注册面 `MatchCardService.RegisterCard`（`MatchCardService.cs:166`）有 `EnsureInProgress()` 门禁且**只追加不覆盖**。
- 沙盒需要的可变面**都已存在且多为 public**（`MatchCardService.RegisterCard`、`EffectRuntime.DynamicEffects.*`、`JudicatorRegistry.Register`、`Trigger.RegisterModing`、`KeywordRegistry.RegisterOrReplace`、`EngineHost.TryLoadCardPack`）⇒ 沙盒＝门控层，非新增机制。
- 调试只读面齐全（审计链/事件流/快照/清单导出），缺口 4 处：`EventSegmentJson` 实现缺失、校验报告无 JSON、`JsonValueWriter` 为 internal、字段级数值差异未暴露。
- 失败模型不统一：多数包生命周期失败为 `IReadOnlyList<string>`；仅 `PackagePrecheckReport`/`CardDistributionValidationReport`/`CardDataLoadResult`/`CardDataSaveResult`/`PrefabLoadReport` 结构化。
- 两处硬编码默认目录需警惕：`EffectTemplateLoader.DefaultDirectory`（`EffectTemplateLoader.cs:25`）、`KeywordPrefabLibrary.DefaultDirectory`（`KeywordPrefabLibrary.cs:19`）。

---

## 一、总体形态（已完成）

### Q1: MCP 服务的进程与部署形态 【Completed】
- 决策（选项 a）：新增 `src/Orc.Mcp`＝**协议无关服务层**（零 MCP SDK 依赖：工具/资源目录 + 执行器 + DTO）；另加 `samples/Orc.Mcp.Stdio`＝薄 stdio 适配 sample。Godot 宿主与 stdio 宿主复用同一服务层。
- 引擎侧允许的改动：① 必要 internal 面放开（`JsonValueWriter`、`ServiceRegistry` 条目名只读枚举）；② 新增单线程对局执行器（命名待定）；③ Q22 决议新增「引擎托管引用表」。

### Q14: 「两个 MCP」的工程形态 【Completed】
- 决策（选项 b）：**一个可执行 + `--mode authoring|player`**，共享服务层；两 mode 各自独立进程 ⇒ 跨进程天然隔离。

### Q13: 「沙盒模式」的落点与粒度 【Completed】
- 决策（选项 a + c1）：门控**只落在服务层**——引擎侧不新增沙盒标志/硬门；非沙盒模式＝工具集内不含调试工具（**不暴露**，非引擎拒绝）。
- 语义改写（已落地）：原表述"引擎应该有一个沙盒模式"改为"**服务层提供沙盒运行模式；引擎侧不新增门控**"。

### Q2 / Q2a: 插件重启与重启时对局处置 【Completed / Not Applicable】
- Q2 决策（选项 c）：**不提供插件热重启**；工具清单坦承该限制；插件变更要求人工重启进程；不提供"重建宿主"式假重启。
- 硬矛盾（保留备查）：`KeywordRegistry.Register` 重名 fail-fast（`KeywordRegistry.cs` L49-68）；仅内建插件有 `_processRegistered` 幂等门（`BuiltInPlugin.cs` L110-121）；`CardPackKeywordLoader` static `KeywordOwner` unload 不注销、跨包重名拒绝（L19-52）；`EngineHost` 无 `Dispose/Stop`。
- Q2a 随 Q2=c 失效。

### Q16: 沙盒局与 targeter 单局路由冲突 【Completed】
- 决策：随 Q14=b（两 mode 独立进程）弱化；同一进程内**仍只允许一个活跃对局**（服务层强制）。

---

## 二、沙盒与隔离（已完成）

### Q15: 沙盒开放的能力档 【Completed】
- 决策（**B 档**）：A 档（资产/定义层：局内动态注册新卡、效果注册/覆盖/卸载、效果资产装载、词条注册/覆盖、卡包生成 + 热加载、资产 dry-run 校验与编译报告）＋ 对局塑造（直接建卡到指定区域、改血量/指挥点、任意事件投递、相位强推/跳过）。
- 决策：**C 档（判定器/触发器 moding、效果资产全局装载）不开放**——源码级操作由专业人员用编码 agent 走代码路径。

### Q15b / Q20 / Q21: 沙盒建局与资产加载 【Completed】
- Q15b（选项）：沙盒局**默认无卡**；卡（多张）/卡组/卡包均由 agent **显式加载**。
- Q20（选项 a）：**逐卡注册进当前局**为唯一加载路径（单卡文件 / 卡牌 id 列表 / 卡包目录经 `CardPackLoader.Load` 展开）→ `MatchCardService.RegisterCard`。不提供"热加载卡包 + 重建局"模式（重建由 `dispose + create` 组合达成）。包级附加语义（id 前缀 `pack/<pkg>/`、`texts.json`、`keywords.json`）由服务层处理。
- Q21（选项 a）：**改卡＝新 id 重新注册**（不新增覆盖面、不动"只追加不覆盖"不变量）；效果修改走 `EffectRuntime.DetachEffectAsync` + 重新挂载。

### Q17: 沙盒进程级副作用的处置 【Completed】
- 决策（选项 a）：**不清理**进程级变更（词条注册面、卡包目录）在沙盒局之间保留；须对 agent **显式可见**（返回标记 + 可查询已发生的进程级变更）。
- 不可回滚的证据：`CardPackKeywordLoader.KeywordOwner` private static 无注销面；`BuiltInPlugin._processRegistered` 一次性门；卡包目录可 `EngineHost.RestoreFrom`（L488）但不完整。

### Q18: 沙盒持久化隔离 【Completed】
- 决策（选项 a）：**服务层写路径白名单**——写类工具只接受沙盒根之下路径（默认 `<base>/sandbox`），越界拒绝；`packs/`、`plugins/` 对写工具只读；**读不隔离**。
- 实现期注意事项（未选 c 的补偿）：禁止走两个硬编码 `DefaultDirectory`；沙盒根与正式资产根的重叠关系至少文档化说明。

### Q19 / Q12: 提升与资产变更时机 【Completed】
- Q19（选项 a）：提供显式提升工具（沙盒产物 → **已存在**的正式卡包目录）；**默认关闭**，需启动参数显式开启。
- Q12 主问（选项 b）：**提升要求无活跃沙盒局**（先结束/销毁）。
- Q12 子问（选项 b）：**允许沙盒局活跃期间热加载卡包**，但返回须明确提示"仅对新建对局生效，当前沙盒局不受影响"。
- 补充：不新增 `LockCardPacks` 强制调用（既有"宿主自觉"语义不变，以提示代替强制）。

---

## 三、对局与交互（已完成）

### Q3: 对局实例归属与驱动模型 【Completed（已按 Q13 需求扩展修订）】
- Q3-主（选项 b，**仅 player 模式**）：宿主预置对局，服务层不建局。编辑者/沙盒模式下**服务层自建沙盒局**。
- Q3-子（选项 a）：**targeter 会话化 pending**——动作工具返回 `{status:"targeting", sessionId, selector, mode, candidates[], min, max}`；agent 用 `target.submit(sessionId, picks[])` / `target.cancel(sessionId)` 应答；沿用三态结果与重试上限 16。

### Q3-子5: 卡牌开发验证的开局通道 【Completed】
- 决策：由沙盒建局解决（编辑者语义）；player 语义不提供。

### Q3-子2: 挂起未应答的处置 【Completed】
- 决策（选项 a + a）：**超时＝取消**（可配置，默认 10 分钟；超时按 `CancelEvent` 走 `SelectorStatus.Cancelled`，返回显式标记）；**挂起期间只允许 `target.*` 与只读观察工具**，其他输入工具返回"存在未完成的选择请求"。

### Q3-子3: 输入入口清单与粒度 【Completed】
- 决策（选项 a + a）：**原子工具、与 `GameEntryPoints.All` 1:1 对齐**（不发明新入口）；第一版只暴露编辑者验证闭环所需子集（建局/销毁、注册卡、放置卡、`EndTurn`、出牌、指挥、观察）。
- 对局塑造（B 档）用**一组原子工具**（`spawn`/`kill`/`damage`/`set_points`/`emit`）；不用 `mutate(path,value)` 式变更工具。

### Q3-子6: 观察面读取形态 【Completed】
- 决策（选项 a + a 子问）：**多面并存 + 按模式分级**——沙盒暴露全知面（实体快照、事件流段、信号历史、审计链）；player 只暴露受过滤面（投影 + 段）。
- 段语义：沿用"取走即移除"，但服务层保留**最近 N 段缓存**（可配置，默认约 200）；并提供基于 `MatchHistoryService` 的按事件类型检索（非消费）。

### Q3-子4: agent 玩家语义的座位与控制权 【Pending（降级）】
- 待决：单座位 / 全控制 / 宿主声明式控制模式。player 模式暂缓交付，故后置。

---

## 四、事件与引用（已完成）

### Q4: 事件投递的能力边界 【Completed】
- 主问（选项 a）：**任意非空白事件名** + **投递回执**（`delivered`/`subscriberCount`/`segmentProduced`；无订阅者时 `no-subscriber` 警告）。外部**仍无法新增订阅者**。
- 子问（用户方案）：**引擎侧新增全局引用表 + 「引擎托管」接口**——投递事件一律提供"全 GUID 字符串版本"的对象引用；已有 GUID 的对象直接以既有 Id 注册（不发明第二套 id）。

### Q22 / Q22a / Q22b: 全局引用表 【Completed】
- Q22（选项 a）：**引擎实例级**——表挂 `LogicEngine`，随引擎/对局创建与销毁；「引擎托管」接口承担注册语义；注销随实体销毁/对局销毁。
- Q22a（选项 a）：GUID 统一 **`N` 格式**（32 位无连字符），与现有导出（投影 `Uid`、快照 `entityId`）一致。
- Q22b（选项 a）：**无 GUID 对象（如效果）由引擎分配 GUID** 后登记。
- 张力记录：该机制改变"引擎无全局单例"的表述——决议采用**实例级**以保持一致；`LogicEngine.cs:15-16` 的表述需在实现时同步修订。

---

## 五、资产与生成（已完成）

### Q7: 卡牌/效果生成的产物与校验闭环 【Completed】
- 主问（选项 a）：**agent 写原文 JSON**，服务层只做**校验 + 规范化写入**（不提供结构化建卡工具）；并**提供文档阅读 tool 与"阅读已有"效果 JSON 的 tool** 作为参考语料（与 Q6 合并落地）。
- 目录形态（选项 a）：**标准卡包结构**（`manifest.json` + `cards/` + `effects/`，可选 `texts.json`/`keywords.json`），符合 `CardPackLoader` 约定，便于逐卡注册与提升。
- 子问 Q7b（选项 c）：效果生成走 **DSL + `*.prefab.json` + 显式 csx 编辑工具** 三条路径。

### Q23: csx 编辑工具的门控 【Completed（暂定·用户未表态）】
- 暂定（保守）：csx 工具**默认关闭**，需启动参数显式开启（与 `promote` 同构）；沙箱拦截/编译失败/超时原因必须回传给 agent。可随时推翻。
- 风险事实：`CSharpScriptSecurityOptions` 自陈"尽力而为的弱沙箱（同进程、非安全边界）"；`CSharpScriptEvaluator` 亦声明"真正的强隔离须进程级"；超时是逻辑中断，无法真正终止脚本。

---

## 六、调试与文档（已完成）

### Q5: 调试信息读取面的清单与形态 【Completed】
- 主问（选项 c）：**细粒度工具 + `diag.report` 聚合入口并存**；聚合入口保持引用式（只给摘要与取数入口，不内联大结构）。
- 子问（选项 b）：**只读 + 补齐 4 处缺口**——实现 `EventSegmentJson`、校验报告 JSON 导出、`JsonValueWriter` 公开、暴露字段级数值差异（`StatUpdateDetection`）。自检能力并入 R1 校验工具；**不做写入型诊断**（不碰 `EventStream`"不设封口/裁剪与上限"契约）。

### Q6 / Q6a / Q6b / Q6c: 文档读取面 【Completed】
- 方向（用户修正）：**不复用现有 `docs/*.md`**；**新写面向 agent 的文档集**（API 文档 + 引擎内部原理伪代码/源码）；工具固定 `list-docs` / `read-docs`。
- Q6-主（选项 c）：**混合生成**——事实表由**代码生成**（包装 `GameEntryPointsJson`/`GameHooksJson`/`AuditChainJson`/`KeywordRegistry.Registered`/`CardComponentRegistry.Registered`/`PackageManifest` schema），叙述与伪代码手写；两类条目同一清单。
- Q6a（选项 a）：`list-docs` 返回扁平静单 `{id, title, category, summary, updatedAt}`，条目内含**分节目录**（小节标题 + 起始行）。
- Q6b（选项 b）：`read-docs(id, offset?, limit?)` 按行段读取。
- Q6c（暂定·用户未表态）：**引用路径＋行号＋关键片段摘录**。
- 必须规避的既有问题：`docs/game-flow/09-调试工具.md` 声称 `EventSegmentJson.Serialize` 存在，但实现缺失（文档超前于实现）。

---

## 七、错误模型（已完成）

### Q9: 错误模型与结果形态 【Completed】
- 主问（选项 a）：**统一外壳** `{ ok, code, message, details?, engineResult? }`；`code` 为受控枚举（`not-ready`/`mode-denied`/`path-denied`/`pending-targeting`/`not-supported`/`validation-failed`/`engine-rejected`/`timeout`/`cancelled`/`internal`）；**MCP `isError` 只用于工具自身故障**，业务失败一律 `ok:false`。
- 子问一（选项 a）：**`Cancelled` → `ok:true` + `status:"cancelled"`**（取消是合法结果）；`Failed` → `ok:false` + 映射 `code`，原生 `Status`/`FailureReason` 保留在 `engineResult`。
- 子问二（选项 a）：引擎扁平字符串失败**原样透传 + 附加 `code:"validation-failed"`**（不解析文本、不改引擎）。

---

## 未决问题（收尾）

### Q11: 交付形态与验证方式 【In Progress（本轮主问）】
- 代码事实：解决方案现为 8 库 + 4 测试 + 1 sample；测试为 xUnit，已有 JSON 契约断言范式（`tests/Orc.Tests/AuditChainExportTests.cs` 逐属性断言导出的 JSON）；README 声明"只提供引擎与示例，不含宿主/客户端实现"。
- 待决：新增工程清单（`src/Orc.Mcp` + `samples/Orc.Mcp.Stdio` + `tests/Orc.Mcp.Tests`？）；stdio 适配放 `src/`（正式产品）还是 `samples/`（与 README 声明一致）；测试要求（单测 + JSON 契约断言 + 端到端最小闭环）；文档集首批覆盖范围。

### Q10: 兼容与版本策略 【Pending（本轮子问）】
- 待决：服务层契约的 `schemaVersion` 策略（现状：`OrchestrationManager`=1、`EffectSnapshot`=2、`CardDataJson`=1、`EffectTemplate`=1，另有 4 个导出器完全无版本字段）；是否与引擎既有版本联动；旧客户端兼容策略。

### Q8: 协议无关性、AGPL 与安全边界 【Pending（本轮子问）】
- 待决：正式确认服务层零 MCP SDK 依赖；新增代码许可头；文件访问根白名单（沙盒写根 / 文档读根 / 正式资产读根）。

### Q3-子4: agent 玩家语义的座位与控制权 【Pending（降级，player 模式交付时再定）】
