# Requirements — 面向 MCP 的引擎外部 API

> 把 OrC 引擎的四类能力（资产管理、对局内交互、调试信息读取、引擎文档读取）暴露为 MCP（Model Context Protocol）客户端可直接调用的外部 API，使 AI agent 无需阅读引擎源码即可管控引擎、按文档开发新卡牌。

> 状态：**需求 grill 已完成（21 轮）**，全部决策见 `requirements-grill-plan.md`；仅 Q3-子4（player 模式座位）按范围降级、留待 player 模式交付时对齐。

## Scope

- **Includes（目标能力域）**
  - R0 语义与沙盒：两种 agent 语义（agent 玩家 / 卡牌编辑者）与沙盒运行模式；**先交付卡牌编辑者（沙盒）**。
  - R1 资产管理：插件与卡包的发现、预检、热加载与生命周期查询（**不含插件热重启**）；卡牌数据体 / 效果预制体 / 模板效果 / 效果 DSL / csx 的读取与生成；沙盒持久化隔离与产物提升。
  - R2 对局交互：面向 AI 的对局交互界面——沙盒建局/销毁、输入入口（与引擎入口清单对齐的原子工具）、事件投递、[Targeter](../../CONTEXT.md) 会话化应答、观察面读取。
  - R3 调试读取：审计链、事件流段、快照、信号历史、hook/触发器登记、校验与编译报告的读取（细粒度 + 聚合入口）。
  - R4 文档读取：**新写**面向 agent 的文档集（API 文档 + 引擎原理伪代码/源码），经 `list-docs` / `read-docs` 读取。
  - R5 错误模型、R6 交付形态与验证、R7 契约版本、R8 协议无关性与访问根。
- **Excludes（明确不做）**
  - 不做引擎本体的规则扩展（不新增卡牌规则、不改既有判定器语义）。
  - **不开放内核改写面**（判定器 moding 替换、触发器 moding 注入、效果资产全局装载）——源码级操作由专业人员用编码 agent 走代码路径。
  - 不做插件热重启（含任何"重建宿主"式假重启）。
  - 不做官方卡牌数据导入/导出（单向导入脚本 `scripts/import_kards_cards.py` 保持原状）。
  - 不做 GUI/前端渲染（渲染仍由 `Orc.UI` 投影与真实前端负责）。
  - 不做账号/权限/多租户。
  - 不做写入型诊断（不清空/裁剪引擎事件流）。
  - player 模式（agent 玩家）**本轮不交付**，仅保留已对齐的语义边界。

## Constraints（已由代码基线确认，不可协商的事实）

- **单线程模型**：引擎全域无驱动器/调度器（`MatchRunner|GameLoop|Dispatcher|TaskScheduler` 零命中），`Bus.Emit`/`TakeSegments`/`Subscribe`/注册表均为无锁同步语义，任何外部调用面必须自建"单写者串行化"机制。
- **插件只能冷加载，且进程内不可重启**：`TryLoad/Unload/ReloadCardPack` 硬拒 `PackageKind.Plugin`（`EngineHost.cs` L329/L389/L465）；`EngineHost` 无 `Dispose/Stop`；重建宿主会重跑插件 process 相注册，而 `KeywordRegistry.Register` 对重名 **fail-fast**（`KeywordRegistry.cs` L49-68，仅 `RegisterOrReplace` 幂等），且 `CardPackKeywordLoader` 的 static `KeywordOwner` 不注销（`CardPackKeywordLoader.cs` L13-52）。⇒ "重启"若不做进程级重启，只能是**有条件的假重启**。
- **热加载只对新建对局生效**：已冻结的 `AssetCatalogSnapshot` 不受影响；`CurrentMatchInjection` 是显式受控的第二级注入。
- **事件名开放但不可发现**：`Bus.Emit` 接受任意字符串，无白名单；但 `Trigger.Hooks` 仅构造期声明且 `internal`，外部不能为新事件挂 handler。
- **Targeter 是唯一天然跨线程往返点**：`ITargeterBridge.BeginTargeting`（同步、不得阻塞）＋ `ITargeterSession.NextAsync()`（`SemaphoreSlim`）；未装配桥接则一切需选位的动作以 `BridgeNotAssembled` 失败。
- **沙盒模式是服务层的运行模式**：已定——门控只落在 `Orc.Mcp` 服务层（工具集差异），引擎侧不新增任何沙盒标志或硬门。
- **引擎侧新增「全局引用表」＝通用能力，不是沙盒门控**：不违反上一条。归属已定为**引擎实例级**（挂 `LogicEngine`，随对局创建/销毁），故与"无全局单例"原则保持一致；MANIFEST 中 `LogicEngine` 的既有表述需在实现时同步修订。
- **唯一稳定可外传标识是 `Entity.Id`（Guid，投影用 `N` 格式）**；`CardBase.MatchCardId` 为 internal。
- **失败模型不统一**：多数包生命周期失败为 `IReadOnlyList<string>`；仅 `PackagePrecheckReport`/`CardDistributionValidationReport`/`CardDataLoadResult`/`CardDataSaveResult` 结构化。
- **序列化风格**：全仓仅 `System.Text.Json`，导出器一律手写 `Utf8JsonWriter`；`JsonValueWriter`（`$entity/$ref/$effect/$truncated`）为 `internal`。
- **工程风格**：内核零可选依赖、接口倒置（`IEngineBridge`/`IUiPluginServices`/`IScriptEvaluator`）；README 声明"不含宿主/客户端实现"。
- **许可**：AGPL-3.0-or-later + 单仓库具名附加许可，新增代码默认 AGPL。

## Requirement Items

### R0: 两种 agent 语义与沙盒模式
- Status: Confirmed
- 背景：外部 agent 有两种语义，工具面与门控不同：
  - **agent 玩家**：只拥有与真人玩家对称的交互界面，语义与玩家一致，不得触达普通对局不可用的能力。
  - **卡牌编辑者**：可直接生成**沙盒对局**，在该对局内使用调试 API（动态注册新卡、注册/覆盖效果等）开发引擎端能力。
- 交付顺序：**先做卡牌编辑者**（沙盒模式）；玩家语义后做。
- 待决：调试能力清单（Q15）、持久化隔离机制（Q18）、产物毕业通道（Q19）。

#### R0a: 服务层提供沙盒运行模式，调试工具仅在沙盒模式暴露
- Status: Confirmed
- Scenario/Trigger: 服务层以"卡牌编辑者"语义（沙盒模式）或"agent 玩家"语义（非沙盒模式）运行、需要调用调试类能力时。
- Behavior: **沙盒模式是服务层的运行模式，不是引擎的运行态**——引擎侧不新增任何沙盒标志或硬门，既有 public 可变面行为与改动前完全一致。非沙盒模式下的"禁用"＝该模式的**工具集内不包含**调试工具（不暴露，而非引擎拒绝）。
- Acceptance:
  - [ ] 沙盒模式由服务层启动参数决定，可判定。
  - [ ] 非沙盒模式的工具清单中不存在调试类工具。
  - [ ] 引擎侧源码不因本需求新增沙盒分支（非沙盒路径行为与改动前一致，回归可验）。
- Terminology: [沙盒模式]（待记入 CONTEXT）、[调试 API]（待记入 CONTEXT）。

#### R0b: 「两个 MCP」＝一个可执行的两个 mode
- Status: Confirmed
- Scenario/Trigger: 需要区分"agent 玩家"与"卡牌编辑者"两种外部接口时。
- Behavior: 提供**单个可执行**（共享协议无关服务层），以启动参数 `--mode authoring|player` 选择工具集；两种 mode 各自独立进程运行。同一进程内**只允许一个活跃对局**。
- Acceptance:
  - [ ] 单一可执行可通过启动参数切换两套工具集。
  - [ ] 两 mode 各自独立进程，跨进程不共享引擎实例。
  - [ ] 同一进程内注入/创建第二个活跃对局被拒绝。
- Terminology: [对局](../../CONTEXT.md)。

#### R0c: 沙盒进程级副作用不回滚，且对 agent 可见
- Status: Confirmed
- Scenario/Trigger: 沙盒局结束或重建后，agent 继续使用同一服务进程。
- Behavior: 沙盒内的进程级变更（词条注册面、卡包目录等）**不随沙盒局结束回滚**；该事实须对 agent **显式可见**（工具返回标记 + 可查询当前进程已发生的进程级变更）。
- Acceptance:
  - [ ] 沙盒局结束后，进程级已注册词条/卡包目录保持变更后状态。
  - [ ] 存在可查询"本进程已发生的进程级变更"的读取面。
  - [ ] 相关工具返回中包含"未回滚"的显式标记。
- Terminology: [词条](../../CONTEXT.md)、[卡包](../../CONTEXT.md)、[包管理器](../../CONTEXT.md)。

#### R0d: 沙盒持久化隔离（防污染正式资产）
- Status: Confirmed
- Scenario/Trigger: 沙盒模式下 agent 生成/改写卡牌、效果、卡包等产物时。
- Behavior: 服务层以**写路径白名单**实现隔离——写类工具的路径必须位于沙盒根之下，越界拒绝；正式资产根（`packs/`、`plugins/`）对写工具只读；**读不隔离**（agent 仍可读正式卡包与文档）。沙盒根由启动参数指定（默认 `<base>/sandbox`）。
- Acceptance:
  - [ ] 写类工具在路径越出沙盒根时返回明确拒绝，且不落盘。
  - [ ] 沙盒内全部写操作产物均落在沙盒根之下。
  - [ ] 读取正式卡包/文档不受限。
  - [ ] 实现不使用 `EffectTemplateLoader.DefaultDirectory` 与 `KeywordPrefabLibrary.DefaultDirectory` 两个硬编码默认目录。
- Terminology: [卡包](../../CONTEXT.md)、[插件](../../CONTEXT.md)、[卡牌数据体](../../CONTEXT.md)。

#### R0e: 沙盒产物提升通道（默认关闭）
- Status: Confirmed
- Scenario/Trigger: agent 在沙盒内完成并验证了卡牌/效果产物，需要进入正式资产时。
- Behavior: 提供显式提升能力，把沙盒内指定产物写入**已存在**的正式卡包目录；该能力**默认关闭**，需启动参数显式开启。
- Acceptance:
  - [ ] 默认启动状态下不存在可用的提升能力（调用被拒或工具不暴露）。
  - [ ] 显式开启后，提升目标必须为已存在的卡包目录，否则拒绝。
  - [ ] 提升不做隐式覆盖（目标已存在同名文件时的行为须明确）。
- Terminology: [卡包](../../CONTEXT.md)、[卡牌数据体](../../CONTEXT.md)、[词条效果库](../../CONTEXT.md)。

#### R0f: 沙盒开放的能力档＝B 档（不含内核改写面）
- Status: Confirmed
- Scenario/Trigger: 沙盒模式下 agent 需要构造验证场景或改引擎资产时。
- Behavior: 沙盒开放①**资产/定义层**：局内动态注册新卡、效果注册/覆盖/卸载、效果资产装载、词条注册/覆盖、卡包生成 + 热加载、资产 dry-run 校验与编译报告；②**对局塑造**：直接建卡到指定区域、改血量/指挥点、任意事件投递、相位强推/跳过。
  **不开放内核改写面**（判定器 moding 替换、触发器 moding 注入、效果资产全局装载）——源码级操作走编码 agent 的代码路径，不经 MCP。
- Acceptance:
  - [ ] 沙盒工具清单覆盖上列①②两组能力。
  - [ ] 工具清单中不存在判定器/触发器 moding 类工具。
  - [ ] 沙盒模式创建的调试点在非沙盒（player）模式工具集中全部缺席。
- Terminology: [判定器](../../CONTEXT.md)、[词条](../../CONTEXT.md)、[csx 动态效果](../../CONTEXT.md)。

#### R0g: 沙盒局默认无卡，资产由 agent 显式加载
- Status: Confirmed
- Scenario/Trigger: 服务层创建沙盒对局后。
- Behavior: 沙盒局**默认不含任何功能卡**；agent 显式加载**多张卡 / 卡组 / 卡包**后方可验证。加载走**逐卡注册进当前局**这一条路径（`MatchCardService.RegisterCard`）：单卡文件、卡牌 id 列表（卡组）、卡包目录（经 `CardPackLoader.Load` 展开为逐卡）三种来源统一展开为逐卡注册，随后按需放置。**改卡＝以新 id 重新注册**（不覆盖既有 id）。不提供"热加载卡包 + 重建局"的加载模式（重建由 `dispose + create` 组合达成）。
  - **实现期偏离（已交付口径）**：引擎在创建期硬拒**空卡组名单**（`Match.cs:99-107`，既有测试锁定）⇒ 沙盒局以脚手架生成的**占位卡**（`sandbox.placeholder`，agent 可见/可改/可删）作为双方卡组底，并将其设为止手 0 张以保证不进手牌。故"默认无卡"按"不含任何**功能卡**"落地。
- Acceptance:
  - [ ] 新建沙盒局不含任何卡实例与卡组内容。
  - [ ] 支持一次加载多张卡、整组卡组、整包卡包，均展开为逐卡注册。
  - [ ] 加载发生在对局"进行态"之外时返回明确的可判别失败（`RegisterCard` 的 `EnsureInProgress` 门禁）。
  - [ ] 同 id 重复注册返回可判别的失败，不做静默覆盖。
  - [ ] 加载结果返回可判别的成功/失败明细。
- Terminology: [卡包](../../CONTEXT.md)、[卡牌数据体](../../CONTEXT.md)、[对局](../../CONTEXT.md)。

### R1: 资产管理
- Status: Draft（R1a~R1c 已确认）
- 行为待决点：对局期变更窗口（Q12）、错误模型（Q9）。
- 已知可复用后端：`EngineHost`、`PackageDiscovery/Resolver/Precheck`、`CardPackLoader`、`CardDataJson/Loader/Writer`、`PrefabJson/Writer`、`EffectTemplateJson/Loader`、`KeywordPrefabLibrary`、`PackageManifest.Parse/ToJson`。

#### R1b: 资产以原文 JSON 生成，服务层负责校验与规范化写入
- Status: Confirmed
- Scenario/Trigger: agent 需要生成或改写卡牌数据体、效果资产时。
- Behavior: **不提供结构化建卡工具**；agent 写原文 JSON，服务层提供①校验（`CardDataJson.Read`、`CardDistributionValidator`、`PackagePrecheck`、效果编译 DryRun）②规范化写入（`CardDataWriter`/`PrefabWriter`，遵循[读宽写窄](../../CONTEXT.md)）。沙盒产物落位在**标准卡包结构**（`manifest.json` + `cards/` + `effects/`，可选 `texts.json`/`keywords.json`），与 `CardPackLoader` 目录约定一致，便于逐卡注册与提升。同时提供**文档阅读与已有资产阅读**工具作为参考语料（见 R4）。
- Acceptance:
  - [ ] 写入前必跑校验，返回结构化报告（错误/警告分列）。
  - [ ] 沙盒产物目录符合 `CardPackLoader` 约定，可被其直接解析。
  - [ ] 写入文本满足读宽写窄的写出规范（固定 schemaVersion、camelCase、LF、无 BOM）。
  - [ ] 存在可读取既有卡牌/效果 JSON 的工具。
- Terminology: [卡牌数据体](../../CONTEXT.md)、[读宽写窄](../../CONTEXT.md)、[卡包](../../CONTEXT.md)、[词条效果库](../../CONTEXT.md)。

#### R1c: 效果生成三条路径（含显式 csx）
- Status: Confirmed（csx 门控待 Q23）
- Scenario/Trigger: agent 需要为该卡牌编写效果时。
- Behavior: 提供三条效果编写路径：① 效果 DSL（`{template, fills}`，经 `EffectCompilationDriver` 校验与编译）② `*.prefab.json` 快照 ③ **显式 csx 源码编辑**（经既有沙箱文本过滤与超时限制）。超表达力时必须**显式失败**并给出诊断，不得静默降级。
- Acceptance:
  - [ ] 三条路径均有可用的写入与校验入口。
  - [ ] DSL 超表达力时返回明确诊断而非生成占位效果。
  - [ ] csx 路径的沙箱拦截/编译失败/超时原因回传给 agent。
- Terminology: [效果 DSL](../../CONTEXT.md)、[模板效果](../../CONTEXT.md)、[csx 逃生舱](../../CONTEXT.md)、[效果预制体](../../CONTEXT.md)。

#### R1d: 提升与资产变更的时机约束
- Status: Confirmed
- Scenario/Trigger: agent 请求提升沙盒产物，或在沙盒局活跃期间热加载卡包时。
- Behavior: 提升要求**无活跃沙盒局**（须先结束/销毁）。沙盒局活跃期间**允许**热加载卡包，但返回须明确提示"仅对新建对局生效，当前沙盒局不受影响"。不对引擎新增 `LockCardPacks` 强制调用（保持既有"宿主自觉"语义，以提示代替强制）。
- Acceptance:
  - [ ] 存在活跃沙盒局时，提升请求被拒并给出可判别原因。
  - [ ] 沙盒局活跃期间热加载成功后返回带"仅对新建对局生效"的提示，且当前沙盒局状态不受影响。
  - [ ] 未引入对 `LockCardPacks` 的新调用点。
- Terminology: [热加载](../../CONTEXT.md)、[卡包](../../CONTEXT.md)、[对局](../../CONTEXT.md)。

#### R1a: 插件不做热重启
- Status: Confirmed
- Scenario/Trigger: 外部请求对插件做加载/卸载/重载/重启时。
- Behavior: 系统**不提供**插件热重启能力；插件资产只参与冷加载。卡包仍按既有语义热加载（默认仅对新建对局生效）。插件变更后须由人工重启进程。MCP 工具面须显式暴露该限制（不得提供"重建宿主"式假重启）。
- Acceptance:
  - [ ] 工具清单中不存在"插件重启/重建宿主"类工具。
  - [ ] 存在一条可查询的限制说明（插件不可热重启及其原因）。
  - [ ] 请求对插件做重启时返回明确失败/不支持，而非静默重建。
- Terminology: [插件](../../CONTEXT.md)、[卡包](../../CONTEXT.md)、[冷加载](../../CONTEXT.md)、[热加载](../../CONTEXT.md)。

### R2: 对局内交互界面（面向 AI）
- Status: Draft（部分 R2a/R2b 已确认）
- 行为待决点：控制权划分（Q3-子4）、验证开局通道（Q3-子5）、挂起处置（Q3-子2）、输入入口清单与粒度（Q3-子3）、事件投递边界（Q4）。
- 已知可复用后端：`Match` 及各 Manager、`TargeterManager`/`ITargeterBridge`、`Bus.Emit`、`BeginAction/TakeSegments`、`UiProjectionProvider`、`MatchHistoryService`、`GameEntryPoints`。

#### R2a: 对局实例的归属（分模式）
- Status: Confirmed
- Scenario/Trigger: 外部（agent）连接服务层时。
- Behavior:
  - **player 模式**：对局由**宿主**创建并注入服务层，服务层不提供建局工具（该模式暂缓交付）。
  - **沙盒模式（编辑者，先做）**：服务层**自己创建沙盒对局**（沙盒局默认无卡，见 R0g）。
  - 两种模式下服务层在同一时刻**只允许一个活跃对局**（因 targeter 桥接无法区分对局归属）。对局未就绪时，输入类工具返回明确的"未就绪"结果而非异常。
- Acceptance:
  - [ ] 工具清单中不存在建局工具（无 `match.create` 类工具）。
  - [ ] 注入第二个对局被明确拒绝。
  - [ ] 对局未就绪时输入工具返回可判别的未就绪结果。
- Terminology: [对局](../../CONTEXT.md)、[Targeter](../../CONTEXT.md)。

#### R2b: Targeter 往返采用会话化 pending
- Status: Confirmed
- Scenario/Trigger: 某输入入口的内部流程发起目标选择时。
- Behavior: 需要选靶的动作**不阻塞**工具调用，而是返回待决选择请求（含 `sessionId`、选择器名、交互模式、候选、最小/最大数）；agent 经独立的应答工具提交选取或取消。非法提交返回可判别的失败原因并允许重试（上限沿用引擎的 16 次）；候选由引擎给定，外部不得自造。
- Acceptance:
  - [ ] 发起需选靶的动作后，工具立即返回待决选择请求而非等待。
  - [ ] 应答工具接受选取与取消两种结果。
  - [ ] 非法选取返回失败原因且不中断对局，可重试。
  - [ ] 超过重试上限后的行为与引擎三态结果一致。
- Terminology: [Targeter](../../CONTEXT.md)、[选择器](../../CONTEXT.md)。

#### R2c: 待决选择请求的超时与门禁
- Status: Confirmed
- Scenario/Trigger: 已产生待决选择请求，但 agent 长时间未应答或主动放弃；或在挂起期间调用其他输入工具。
- Behavior: 待决会话带**可配置超时（默认 10 分钟）**；超时后按**取消**处理（等价 `CancelEvent`，走 `SelectorStatus.Cancelled`），对局继续，工具返回显式标记"因超时按取消"。agent 可随时主动取消。**挂起期间只接受 `target.*` 与只读观察类工具**，其他输入工具返回可判别的"存在未完成选择请求"。
- Acceptance:
  - [ ] 超时后对局不卡死，且结果与引擎 `Cancelled` 三态一致。
  - [ ] 超时与主动取消在返回中可区分。
  - [ ] 挂起期间非 `target.*`/非只读工具的调用被拒绝且不产生副作用。
- Terminology: [Targeter](../../CONTEXT.md)、[选择器](../../CONTEXT.md)、[对局](../../CONTEXT.md)。

#### R2d: 工具粒度＝与引擎入口清单对齐的原子工具
- Status: Confirmed
- Scenario/Trigger: 服务层设计工具清单时。
- Behavior: 一个引擎公开入口对应一个工具，**不发明新入口命名**（权威来源＝`GameEntryPoints.All` 与 `GameEntryPointsJson`）。第一版只暴露编辑者验证闭环所需子集（建局/销毁、注册卡、放置卡、`EndTurn`、出牌、指挥、观察）；沙盒塑造类（Q15 B 档）以一组原子工具暴露，不使用统一的路径式变更工具。
- Acceptance:
  - [ ] 每个工具可追溯到引擎入口或已确认的需求项（无自造入口）。
  - [ ] 第一版工具集合可独立跑完"注册卡 → 放置 → 构造场景 → 观察"闭环。
  - [ ] 沙盒塑造不依赖"路径字符串"解析引擎内部结构。
- Terminology: [接入点](../../CONTEXT.md)、[门户](../../CONTEXT.md)。

#### R2e: 观察面按模式分级，段消费带缓存
- Status: Confirmed
- Scenario/Trigger: agent 读取对局状态与推进过程时。
- Behavior: **沙盒模式**暴露全知观察面（实体快照含组件、事件流段、信号历史、审计链）；**player 模式**只暴露受隐藏信息约束的观察面（投影 + 段）。事件流段沿用引擎"取走即移除"语义，但服务层**保留最近 N 段缓存**（N 可配置）供重复读取，并提供基于信号历史的按事件类型检索（非消费）。
- Acceptance:
  - [ ] 沙盒模式可读到组件级字段与因果条目；player 模式读不到对手隐藏信息。
  - [ ] 段被拉取后仍可从服务层缓存重复读取。
  - [ ] 可按事件类型检索历史条目且不消费段队列。
  - [ ] 缓存上限可配置，超出后按明确策略淘汰（不无界增长）。
- Terminology: [观察面](../../CONTEXT.md)、[事件流段](../../CONTEXT.md)、[投影](../../CONTEXT.md)。

#### R2f: 事件投递与引擎托管引用表
- Status: Confirmed
- Scenario/Trigger: agent 需向对局内总线投递事件；或需在对局调用中以对象引用作为载荷/参数。
- Behavior: 投递接受**任意非空白事件名**，并返回投递回执（是否送达、订阅者数量、是否产生事件流段；无订阅者时给出明确警告）。对象引用统一以**全 GUID 字符串**表达：引擎侧新增**全局引用表**（`dict<string, object>`）与**「引擎托管」接口**（提供注册语义）；已有 GUID 的对象（`Entity.Id`）直接以既有 Id 注册。外部**仍无法新增订阅者**——投递只能命中已存在的 hook。
- Acceptance:
  - [ ] 投递返回可判别的回执（含订阅者数量；无订阅者时为警告而非静默成功）。
  - [ ] 载荷中的对象引用可用全 GUID 字符串表达并被正确解析。
  - [ ] 引用表随其归属作用域正确清理（待 Q22 定），无对象泄漏。
  - [ ] 不存在"通过投递新增订阅者"的能力（该路径被明确拒绝或不存在）。
- Terminology: [引擎托管引用表]（待记入 CONTEXT）、[全局引用表]（待记入 CONTEXT）、[接入点](../../CONTEXT.md)。

#### R2g: 引擎托管引用表（引擎实例级）
- Status: Confirmed
- Scenario/Trigger: 对象的引用需要跨边界（MCP 调用参数、事件载荷）表达与解析时。
- Behavior: 引擎在**实例级**（`LogicEngine`）维护引用表（key＝GUID 字符串，value＝对象引用），并提供**「引擎托管」接口**承担注册语义：已有 GUID 的对象直接以 `Entity.Id` 作为 key 额外登记；无 GUID 的对象由引擎**分配 GUID**后登记；注销随实体销毁/对局销毁。GUID 统一使用 **`N` 格式（32 位无连字符）**，与现有导出（投影 `Uid`、快照 `entityId`）一致。不发明第二套 id。
- Acceptance:
  - [ ] 表随引擎/对局销毁而清空，不留跨对局残留对象。
  - [ ] 以 `N` 格式 GUID 可解析出对应对象；不存在的 GUID 返回可判别失败。
  - [ ] 无 GUID 对象（如效果）注册后同样可被 GUID 引用。
  - [ ] 同一 key 重复注册有明确行为（拒绝或幂等），不静默覆盖。
- Terminology: [引擎托管引用表]（待记入 CONTEXT）、[全局引用表]（待记入 CONTEXT）、[安全引用](../../CONTEXT.md)。

### R3: 调试信息读取
- Status: Confirmed
- Behavior: 提供**细粒度调试读取工具**（审计链、事件流树、段、快照、信号历史、hook 词表与碰撞、总线订阅读面、触发器登记、预制体登记、校验/编译报告）＋ **`diag.report` 聚合入口**（引用式：只给摘要与各面取数入口，不内联大结构）。补齐 4 处缺口：实现 `EventSegmentJson`、校验报告 JSON 导出、`JsonValueWriter` 公开、暴露字段级数值差异。
  - **交付状态（实现期修订）**：前 3 处已交付；**第 4 处（字段级数值差异）未交付**——`StatUpdateDetection` 的比较结果仅在数值管线内部产生，对外暴露需改动管线写入路径（热点路径、触及收敛不变量），收益可由 `observe.snapshot`（组件级字段）部分覆盖。详见 `implementation.md`「实现期发现与偏离」。
- Acceptance:
  - [ ] 细粒度工具可独立取用各调试面，payload 可控。
  - [ ] `diag.report` 返回各面摘要与取数入口，不内联大结构。
  - [ ] 4 处缺口补齐后可经工具读到（段可独立序列化、报告可 JSON 化、数值差异可查）。
  - [ ] 调试面全部只读，不提供清空/裁剪引擎事件流的写入型诊断。
- Terminology: [观察面](../../CONTEXT.md)、[事件流段](../../CONTEXT.md)、[审计链](../../CONTEXT.md)、[动作作用域](../../CONTEXT.md)。

### R4: 面向 agent 的引擎文档读取
- Status: Confirmed（源码呈现方式待 Q6c）
- 方向（已定）：**不复用现有 `docs/*.md`**（那是给人的设计/调研文档）；**新写**面向 agent 的文档集，内容＝① 各类 **API 文档** ② **引擎内部原理的伪代码/源码**；读取工具固定为 **`list-docs`** 与 **`read-docs`**。
- Behavior:
  - 文档集**混合生成**：事实表由代码生成（包装既有导出器：入口清单、信号/判定器/触发器分层清单、审计链、词条注册面、组件注册面、包清单 schema），叙述与伪代码手写；两类条目在同一清单呈现。
  - `list-docs` 返回扁平静单：`{id, title, category, summary, updatedAt}`，并在条目内给出**分节目录**（小节标题 + 起始行）。
  - `read-docs(id, offset?, limit?)` 按行段读取。
- Acceptance:
  - [ ] `list-docs` 覆盖全部文档条目（含代码生成的事实表条目）并带摘要与分节目录。
  - [ ] `read-docs` 支持按 `id` + 行段读取；越界/不存在 id 返回可判别失败。
  - [ ] 代码生成部分随代码变动自动更新（无手工同步步骤）。
  - [ ] 手写部分不承诺与代码自动同步，但不得与生成部分冲突（同一事实只有一个来源）。
- Terminology: [投影](../../CONTEXT.md)、[接入点](../../CONTEXT.md)。
- 必须规避的既有问题：`docs/game-flow/09-调试工具.md` 声称 `Orc.Output.EventSegmentJson.Serialize` 存在，但实现缺失（文档超前于实现）。

### R5: 统一错误模型与结果外壳
- Status: Confirmed
- Scenario/Trigger: 任何工具调用返回时。
- Behavior: 所有工具返回统一外壳 `{ ok, code, message, details?, engineResult? }`；`code` 取自受控枚举（`not-ready`/`mode-denied`/`path-denied`/`pending-targeting`/`not-supported`/`validation-failed`/`engine-rejected`/`timeout`/`cancelled`/`internal`）。MCP 协议级 `isError` **只用于工具自身故障**；业务失败一律 `ok:false`。引擎**取消**（`Cancelled`）视为**成功结果**（`ok:true` + `status:"cancelled"`）；引擎**失败**映射为 `ok:false` + `code`，并把原生 `Status`/`FailureReason` 保留在 `engineResult`。引擎的扁平字符串失败**原样透传**并附加 `code:"validation-failed"`。
- Acceptance:
  - [ ] 所有工具的成功/失败返回结构一致（统一外壳）。
  - [ ] 取消与失败在返回中可区分，且取消不被当作错误。
  - [ ] 引擎原生结果（含三态与结构化报告）在返回中不丢信息。
  - [ ] 不以文本解析方式解释引擎的扁平字符串失败。
- Terminology: [判定器](../../CONTEXT.md)、[Targeter](../../CONTEXT.md)。

### R6: 交付形态与验证方式
- Status: Confirmed
- Scenario/Trigger: 本需求的工程落地与验收。
- Behavior: 新增三件物：`src/Orc.Mcp`（协议无关服务层）、`samples/Orc.Mcp.Stdio`（可执行 sample，`--mode authoring|player`）、`tests/Orc.Mcp.Tests`。验证分三层：服务层单测、**JSON 契约断言**（仿 `tests/Orc.Tests/AuditChainExportTests.cs`）、**端到端最小闭环**（建沙盒局 → 注册卡 → 放置 → 构造场景 → 观察 → 销毁）。文档集**首批**覆盖编辑者闭环所需主题，内核原理类为第二批。
- Acceptance:
  - [ ] 三个新增物存在于解决方案并可构建。
  - [ ] sample 可经启动参数切换两套工具集，且可直接运行（无需外部宿主）。
  - [ ] 存在覆盖统一返回外壳与关键导出的 JSON 契约断言测试。
  - [ ] 存在端到端最小闭环测试（沙盒建局→注册卡→放置→观察→销毁）。
  - [ ] 文档集首批覆盖：卡牌数据体 schema、6 个内置组件字段语义、效果 DSL 与模板、词条与词条效果库、卡包清单与目录约定、包生命周期、对局状态与观察面、代码生成的事实清单。
- Terminology: [卡包](../../CONTEXT.md)、[卡牌数据体](../../CONTEXT.md)。

### R7: 契约版本策略
- Status: Confirmed
- Scenario/Trigger: 服务层对外契约演进时。
- Behavior: 服务层契约使用**独立** `schemaVersion`，与引擎内部格式版本（`OrchestrationManager`=1 / `EffectSnapshot`=2 / `CardDataJson`=1 / `EffectTemplate`=1）**解耦**，并在工具清单中暴露。引擎内部格式升版不自动改变该版本号。
- Acceptance:
  - [ ] 存在服务层独立契约版本号，且可经工具清单查询。
  - [ ] 引擎内部格式版本变化不影响该版本号。
- Terminology: [读宽写窄](../../CONTEXT.md)。

### R8: 协议无关性、许可与访问根
- Status: Confirmed
- Scenario/Trigger: 服务层的依赖、代码许可与文件访问边界。
- Behavior: `src/Orc.Mcp` **零 MCP SDK 依赖**（协议适配只在 sample 层）；新增源文件带 AGPL 许可头；文件访问白名单＝**沙盒写根（唯一可写）** + **新文档集读根** + **正式资产读根（只读）**。
- Acceptance:
  - [ ] `src/Orc.Mcp` 不引用任何 MCP SDK 包。
  - [ ] 新增源文件带 AGPL 许可头。
  - [ ] 除沙盒写根外无其它写入路径；越界写被拒绝且不落盘。
  - [ ] 读取不超出文档集读根与正式资产读根。
- Terminology: [插件](../../CONTEXT.md)、[卡包](../../CONTEXT.md)。

## Terminology

沿用 [`CONTEXT.md`](../../CONTEXT.md)：引擎内核、游戏层、插件、卡包、包管理器、资产名、冷加载/热加载、判定器、资产服务、观察面、接入点、扩展点、事件流段、动作作用域、Targeter、投影、卡牌数据体、词条效果库、csx 动态效果、读宽写窄、事件卡属性过滤。

本需求新增术语（待记入 `CONTEXT.md`，命名以实现 grill 定稿为准）：
- **沙盒模式**：服务层的运行模式（非引擎运行态）；该模式下暴露调试类工具，调试能力不触达非沙盒模式。
- **调试 API**：沙盒模式开放的工具集合（B 档：资产/定义层 + 对局塑造）。
- **对局执行器**（暂名）：服务层内的单写者串行化组件，承载活跃对局、请求队列与 targeter 待决会话状态机。
- **引擎托管引用表 / 全局引用表**：引擎实例级 `dict<string, object>`，以 `N` 格式 GUID 为 key，支撑对象引用的跨边界表达与解析。
- **MCP 工具 / MCP 资源**：对外暴露的可调用操作与可读数据。