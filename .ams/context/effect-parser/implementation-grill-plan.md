# Implementation Grill Plan — OrC 效果解析器

> 本文件记录**实现层**未决问题（需求层问题见 `requirements-grill-plan.md`）。

## 未决问题

### I1: tokenizer 的 token 类型划分 {Completed}
- 用户确认合并后的 **12 类**划分与"**句式内建 / 词表外置**"落点（见下"调研结论"）。
- 归属步骤：**S7（解析段）**。

### I2: 转换段的起步切分与模板载体 {Completed}
- 模板载体：**甲**——`*.prefab.json` 文件。
- "挖空"表达：**否决"留空 handler"**（因一个效果可有**多个 handler**，如"部署"需注入主动触发部署效果的 handler）。改为**显式定义"模板效果"**：**持有预制体作为模板 ＋ 额外将部分 handler 声明为 DSL 可赋值填写的槽位**。
- 起步：**S1 + S2 一起做**（schema 与模板约定耦合）。

### I3: "模板效果"的槽位定位与填写粒度 {Completed}
- 槽位定位：**乙**——**语义槽位名 + 映射**；DSL 只认槽位名。
- 填写粒度：**iii**——**op 序列 或 一段 csx 皆可**（op 序列优先，csx 逃生舱兜底）。
- **修订（用户中途）**：**不再依赖实际预制体**——改为**为模板效果设计专门的"模板效果预制体"格式**，再声明 handler 槽位。

### I4: 专用"模板效果预制体"格式的具体形态 {Completed}
- 与原生关系：**甲**——**同构子集**（字段名/结构对齐 `EffectSnapshot`：`mainTrigger`/`otherTriggers`/`modings`/`injects`/`events`…；仅 handler 内容改为**槽位引用**，另加 slots 声明表）。
- 身份字段：`stableKey` 由**模板填写**；`version`/实例 `id` 由**转换器生成**。
- 存放：`src/Orc.Game` 下**独立 `Templates/` 目录 + `*.tpl.json`**（与卡数据体解耦）。

### I5: DSL 实例的顶层 schema {Completed}
- 顶层：**甲** map 形式——`{ "template": "<tplId>", "fills": { "<slotName>": { "ops": [ ... ] } } }`。
- 槽位内容：**统一为 op 序列**，其中 `csx` 逃生舱是一种 op（`{ "op": "csx", "script": "..." }`）。
- 元数据：**不进** DSL（作转换器入参/旁路）。
- **原则（用户明确）**：凡**官方原语无法表达者，必须（且只能）用 csx 表达**（不存在其它扩展通道；解析段对超子集文本仍走显式失败）。
- 一个 DSL 实例＝**一个效果**；一卡多效果＝**DSL 数组**。

### I6: 官方 op 的参数模型 {Completed}
- 参数模型：**甲**——沿用 kards-diy 结构（`target` 选择器 `sel`/`side`/`zone`/`filter`/`count`；`filter`；`condition`；数值表达式对象），**只启用子集字段**。
- MVP 启用范围：**不启用 `condition`**；数值**只支持常数**（`{stat}`/`{count}`/"每有…" 后置）；选择器**要**启用 `side` ＋ `count` ＋ 极简 `filter`（兵种/词条）。

### I7: csx handler 的能力边界（调研，已完成） {Completed}
- **入口形态**：csx 入口必须是**顶层具名变量**（不是方法），签名固定
  `Func<TView, Context, CancellationToken, Task> HandleAsync = (view, ctx, ct) => {...};`
  变量名须 == `EventPrefab.entry`（默认 `HandleAsync`），否则 `entry-missing`。
- **可拿到的入参**：仅 `view` / `ctx` / `ct`；引擎经 `ctx.Engine`（唯一通道）。
- **引用白名单**（`CSharpScriptSecurityOptions`）：`System.*` + **`Orc`（内核）**；导入 `Orc.Core`/`Orc.Cards`。**`Orc.Game` 不在白名单**。
- **csx 能做**：读写视图/`ctx` 数据、`ctx.Stop/Interrupt`、`ctx.Engine.Emit`、调内核 `DamageFlow`/`AttackFlow`/`OrderFlow`、改 `HealthData`。
- **csx 做不到（硬缺口）**：**抽牌 / 移动 / 部署**（在 `Orc.Game`，白名单够不到）；无任何 csx 便利 API 层；无真实非空 csx 样例可抄。
- **沙箱**：`File/Assembly/Environment/Process/System.IO` 等做**子串**匹配 → 模板/参数里出现这些子串会被拒。
- **风险点**：`viewTypeName` 与 csx 泛型参数必须严格对应，否则 `signature` 失败。

### I8: csx 可达性 vs MVP op 集 {Completed}
- 决策：**甲**——本轮**只保证结构合法、不保证可运行**；csx 可达性后续通过**把 `Orc.Game` 加入引用白名单**解决（用户明确"后面把 game 加入白名单就行了"）。
- 因此 MVP op 集**不因可达性收缩**。

### I9: op → csx 的渲染形态 {Completed}
- 决策：**甲**——**文本模板 + 占位符**（外置 `Templates/ops/*.csx.tpl`）；**否决乙（代码构造器）**，理由：太复杂、工作量大。
- csx 编译校验：本轮**不要求**（只要求 `EffectSnapshot` 可反序列化）。

### I10: 转换段落地契约 {Completed}
- 用户**全部认可**提案：
  - `*.tpl.json`＝同构子集 + `slots` 声明表；槽位定位用**事件 id 引用**（`mainTrigger.events[e1]`）；`stableKey` 模板填、`version`/实例 `id` 转换器补。
  - DSL＝`{ template, fills: { slot: { ops: [...] } } }`。
  - op 语句模板＝`Templates/ops/<op>.csx.tpl`（**body 片段** + `{{字段}}` 占位符）；转换器拼 body 并包裹成**顶层入口变量** `Func<...> HandleAsync = (view, ctx, ct) => { ... };`。
  - 输出＝`EffectSnapshot` JSON 文本（indented，可落 `<id>.prefab.json`）。
  - 校验：模板侧（schemaVersion/id 唯一/slots 命中/被指事件无既有 csx）；DSL 侧（template 存在/槽位名合法/op 名在注册表/参数合法）。
  - 命名空间 `Orc.Game.EffectParsing`（`.Templates`/`.Dsl`/`.Compilation`/`.Parsing`）；资产 `Templates/*.tpl.json`、`Templates/ops/*.csx.tpl`。

### I11: 解析段 AST 的结构与"AST ↔ DSL"分层 {Completed}
- AST 形态：**甲**——**与 DSL 解耦的中性语法树**（触发短语/条件子句/目标短语/动作短语/数值），语义映射到 DSL 在下游。
- AST 节点**保留原文 span**。
- 词表资产：**分文件**；条目**引用 op 名**，由校验器强制 ∈ 注册表。

### I12: 解析段对外接口与诊断形态 {Completed}
- 接口：**甲**——`Parse(string) → ParseResult { Effects: DslInstance[], Unresolved: {Span,RawText,Reason}[] }`。
- 验收形态：**iii**——测试守门 ＋ 生成 `.md` 覆盖率报告。

## 状态：转换段已落地（S1–S5 完成）

### I13: 句式切分规则（解析段 S8，C 阶段） {Completed}
- P1 预处理：ASCII `.` 仅后随汉字视作句号；`;`/`,`/`:` 归一；**换行归一为空格**（唯一例外：整行仅词条/数值 ⇒ 丢弃该行）；成对引号屏蔽；未闭合引号不切分；本批**不建**错别字归一表（留钩子）。
- P2 效果切分：`。！？` **硬边界**（单元独立）；`；` **软边界**（继承触发/条件）。**D1＝继承"最近的硬边界单元"**。产出 `SegmentedEffect{ Text, Span, HardBoundary }`。
- P3 触发归属：触发短语＝**句首 → 第一个 `：` 或第一个 `，`**；显式具名触发（冒号界定）；监听型触发（句首监听主语且以 `时/后` 结尾）；**D2＝无显式触发时尽量解析为被动触发器（限支持的句式）**；多事件触发**拆分为多个效果**。
- P4 抉择：`抉择：`；选项按 `或者/或` 优先、`；` 兜底；范围＝当前硬边界单元，句号后不受影响。
- P5 代词回指：`其/它/该单位/将其/使其/对该` 指向前一单元目标短语；**D5＝认不准不替换**。
- P6 单元内子句：`，`/`、` 为子句分隔（不产生新效果）；`：` 之后全部属该触发的正文。

### I14: AST 节点形状（解析段 S8，B 阶段） {Completed}
- 全部裁决通过：B-D1 **携带词法归类值**；B-D2 **AST 保持多事件、S9 展开为 N 个效果**；B-D3 软边界继承用**引用 `InheritsFrom`**；B-D4 span 口径＝**原文偏移**（tokenizer 维护归一映射表）；B-D5 **条件短语入 AST**，S9 判"超子集→显式失败"。
- **修订（用户）**：`ActionPhrase` 的数值槽改为通用的 **`Payload`**，本批**仅允许整数**（`PayloadKind.Integer`，预留扩展）。

### I15: token 字段与分类细节（解析段 S7，A 阶段） {Completed}
- 全部裁决通过：A-D1 具名触发词由词法层产 `Trigger{Named}`、监听型短语由 AST 层组合识别（`时/后` 为 `Trigger{Suffix}`）；A-D2 **一字面一类别**（加载期强制）；A-D3 引号产 `QuoteOpen/QuoteClose`、切分器按深度跳过；A-D4 **不保留**空白/换行 token、tokenizer 内置归一→原文偏移映射；A-D5 `Unknown` **累积成段**。

## 状态：全部设计闭合，S1–S11 已实现并验收

## 调研结论（kards-diy 证据 → token 类型）

**关键事实**：kards-diy **没有 tokenizer**，是"分层正则 + 字符串切分 + 逐行状态机"。词汇散落在约 20 张正则/词表里。真正**集中的权威表**只有 3 张 + 一张触发词表：
- `UNIT_TYPES` 兵种（`primitives.js:39-61`）
- `KW_CN` 词条（`primitives.js:433`）
- `SET_WORD_FILTER` 系列（`primitives.js:444`）
- `TRIGGERS` 触发前缀（`compiler.js:116-263`，约 60 条）——注意触发词**不在** primitives 里。
- 比较词族 `CN_LT/CN_GT/CN_EQ`（`primitives.js:1697-1708`）；条件规则 `COND_RULES`（1710-1876）；动作词 `ACTIONS`（207-431）。

**建议的 token 类型（合并两路结论，12 类）**：

| # | 类别 | 作用 | 代表词/模式 | 证据 |
|---|---|---|---|---|
| 1 | `TRIGGER` 触发词 | 定主触发器 | 部署：/亡计：/动员：/抉择：/抽取：；…时/…后（监听型约 27 项） | `compiler.js:116-263` |
| 2 | `PUNCT` 标点 | 切分/边界 | `。！？`硬、`；`软（继承前句控制）、`，、`子句/归并、`：`触发界定、`“”「」`屏蔽、`\n`零权重 | `compiler.js:625/671/695`；`parser-v2.js:22-31` |
| 3 | `CONNECTIVE` 连接/逻辑 | 组合 | `或者/或`(抉择)、`如果/若/当`(条件引导)、`则`、`否则`、`并/且/然后`、`每有`、`直到` | `compiler.js:1012/1988` |
| 4 | `PRONOUN` 代词/回指 | 目标绑定 | 其/它/该单位/该目标/将其/使其/对该/把它/将该 | `compiler.js:266-275`；`parser-v2.js:110` |
| 5 | `ACTION` 动作词 | 谓语锚点 | 造成/消灭/摧毁/抽/加入手牌/压制/抑制/移动/召唤/洗入/获得/恢复 | `primitives.js:207-431` |
| 6 | `QUANT` 量词/选靶 | 选靶方式 | 一个/N个/所有/全部/随机/至多N ＋ 个张名辆架艘支枚 | `primitives.js:117-126` |
| 7 | `SIDE` 阵营 | 选靶 | 友方/我方/敌方/对方/双方/任意 | `primitives.js:128-137` |
| 8 | `ZONE` 区域/落点 | 选靶/落点 | 前线/支援阵线/战场/卡组/手牌/总部 | `primitives.js:139-142` |
| 9 | `FILTER` 过滤 | 过滤维度 | 兵种/词条/系列/卡名/稀有度；花费/攻击/防御阈值；受伤态 | `primitives.js:144-205` |
| 10 | `COND` 条件词 | 条件 | 比较词（不小于/至少/至多/大于/小于/等于…）；场面/资源/回合/事件态 | `primitives.js:1697-1876` |
| 11 | `NUM` 数值/表达式 | 数值 | 中文数字+阿拉伯、`±`符号、动态取值 `{stat}/{count}`、"每有…" | `primitives.js:17-27` |
| 12 | `UNKNOWN` 未知词 | **显式失败** | 未归类串 → 失败（对齐 `trigUnresolved`），不静默丢弃 | `compiler.js:926/968-974` |

**落点建议**：1/2/3/4 与"词表命中策略"属**句式（内建）**；5–11 的**词条内容**属**词表（外置）**（对齐需求 Q6"句式官方维护 + 词表外置"）。

**已知坑（自 kards-diy）**：
- 中文无空格 ⇒ tokenizer 不是"分词"，而是**词表最长匹配扫描**。
- 同一中文串在不同位置可能是**词表词**也可能是**卡名**（如"步兵"）⇒ UNKNOWN 判定需与卡名词表交互。
- 代词有**两条含义不同**的实现路径（compiler `EVENT_REF` vs parser-v2 代词原语），OrC 需显式择一。
- OCR/错别字归一表在 kards-diy **只有 2 条**（`compiler.js:891`）⇒ OrC 需自建归一前置阶段。
- `每当` 在 kards-diy 中**不存在**（只有"每有/每受到/每消灭"）。

---

# 指挥点槽事件改进（E1-25）实现层未决问题

> 基线：委托定稿 `委托-指挥点槽事件改进.md`；本段为实现 grill 交叉核对代码后暴露的缺口/矛盾。

### I16: `ResourceManager` 如何取得判定器（构造可达性 + 装配时序倒置） {Completed}
- **矛盾事实**：`JudicatorRegistry` 为游戏层对象，`LogicEngine` 上**不可达**；且 `Match.Initialize` 中
  `_resourceManager = new ResourceManager(...)`（`Match.cs:426`）**早于** `_judicators = new JudicatorRegistry()`（`Match.cs:502`）。
  ⇒ 委托 §3.3 给的构造签名**不足以**实现 §3.2。
- **裁决（用户：a）**：构造改 `ResourceManager(LogicEngine engine, Func<JudicatorRegistry?> judicators, int maxPointSlots = DefaultMaxPointSlots)`；
  `Match.cs:426` 传 `() => _judicators`（延迟读取，与 `EffectRuntime` lambda 注入同构；**不动**既有创建顺序）。

### I17: `Settle` 由同步 `void` 改异步 {Completed}
- **矛盾事实**：`Settle` 现为同步 `void`（`ResourceManager.cs:34`），发 `slot.changed` 需 `Task`；委托未申报签名变更。
- **裁决（用户：a）**：改名 `SettleAsync`（受控变更申报）；唯一调用点 `TurnManager.cs:107` 改 `await`。

### I18: `GameHooksJson` 硬编码清单未纳入委托 {Completed}
- **缺口事实**：`Output/GameHooksJson.cs:100-156` 的 `SignalMetadata` 是**硬编码 18 条**表；
  测试断言 `signals`=18 且每条 `payloadKeys`/`emitters` 非空；`helpers(13)+turn(5)==Signals.Count`。
- **裁决（用户：同意）**：3 条新信号补 `SignalMetadata` 条目 ＋ 受控更新 `GameHooksTests`（signals 18→21、helpers 13→16）。

### I19: 判定器 JSON 表既有不自洽（15 vs 16） {Completed}
- **事实**：`GameHooksJson.JudicatorMetadata` 仅 15 条（**缺** `effect.target.resolve`），`JudicatorNameList` 已 16 条。
- **裁决（用户：同意）**：补新判定器 **并顺带补** `effect.target.resolve`（修既有缺口）。

### I20: `slot.gained` / `slot.lost` 的 `Amount` 语义 {Completed}
- **裁决（用户：同意）**：`Amount` ＝**实际 Δ**（`min(effective, Max-current)` / `min(effective, current)`）；信号只报事实。

### I21: 加/减槽是否连带改点数（`player.Points`） {Completed}
- **裁决（用户：同意）**：**不改** `Points`（`Settle` 的 `Points=PointSlots` 保持唯一重设点）。

### I22: `失去` 的动词键命名 {Completed}
- **裁决（用户：同意）**：动作词 `失去 → key "lose"`（与 `gain` 对称；映射层再判定 `pointSlot`）。
  `Action_Lexicon_Keys_Are_Known_Verbs` 的 known 集加 `lose`。

### I23: `获得 N 个指挥点槽` 无数值时的处置 {Completed}
- **裁决（用户：同意）**：**显式失败**（`gainSlot` 登记 `Required:["amount"]`；不静默补 1）。

### I24: 「额外获得 / 失去」的数字各自经"默认返回原值"的判定器包裹（用户追加） {Completed}
- 用户口径原文：`指挥点槽的额外获得和失去的具体数字可以分别用一个默认返回原值的判定器包裹`。
- **裁决（用户：A / 甲 / I）**：
  - **A 施加位置**＝`ResourceManager.GainSlotsAsync`/`LoseSlotsAsync` **内部**包裹（任何调用方都经过、单一真源，与 `SettleAsync` 经 increment 判定器对称）。
  - **甲 命名**＝`resource.slot.gain` / `resource.slot.lose`（与递增器 `resource.slot.increment` 同族）。
  - **I 钳制与负值**＝`effective` 仍经 `min(·, Max-current)` / `min(·, current)` 钳制；`effective ≤ 0` ⇒ Δ=0 ⇒ **零信号、不抛错**。
  - **注册面**＝三条新判定器均进 `Match.Initialize` **内置固定注册段**（默认名恒可解析）。
- 与 `Settle` 的关系：递增判定器 `resource.slot.increment` 与这两条**互不影响**（回合开始递增不走 gained/lost 的包裹器）。

### I25: 判定器清单计数影响（由 I19/I24 汇总） {Completed}
- `JudicatorNames` 常量：16 → **19**；`GameHooks.JudicatorNameList`：16 → **19**；`BuiltInJudicatorNames`：14 → **17**；`External`：2（不变）。
- `GameHooksJson.JudicatorMetadata`：15 → **19**（＋1 修 `effect.target.resolve`、＋3 新条目）——已落地。
- `GameHooksTests` 受控更新：16→19、14→17、JSON judicators 15→19——已落地。
- **交付结论**：构建 0 错误；全量测试 **1225 项全绿**（基线 1218 ＋ 新增 7 项）；对拍报告"指挥点槽"未解析原因**归零**（覆盖率 62.1% → 62.7%）。

---

# inject 型监听缺口（E1-36 收尾）实现层问题

> 基线：交接文档 §4 的"唯一未闭环项"。本轮已用**决定性探针**取证，根因确诊（见下 I26）。

### I26: `attack_basic` inject 处理器"像没被调用"的根因 {Completed（取证）}
- **决定性证据**（把已 Skip 用例临时启用 + dump `RootStream.Entries`）：
  ```
  [Error] 单位攻击触发器/e1 | Exception has been thrown by the target of an invocation. | kw=exception:TargetInvocationException
  ```
  即 **处理器确实被调用**，并在调用内抛异常；`Trigger<TView>.RunEventsAsync` 对"事件内业务异常"是**隔离记录并继续**
  ⇒ 攻击命令仍 `Success`、外部表现**恰好等同于"没被调用"**（交接文档 §4.3 的 (a) 支**得到确诊**，(b) 支可排除）。
- **异常来源**（代码取证，`TargetInvocationException` 只能来自反射 `Invoke`）：
  `Effect.InjectByFramework` 的宿主包装 `WrapWithHostCore<TView>` → `HostProperty<TView>.Set(view, host)`
  → `PropertyInfo.SetValue(view, host)`。
1. 而 `view` 的实际运行时类型是 **`ViewProxyFactory` 生成的代理**（`ContextViewBinder.Create<TView>` 的产物）；
2. 代理把每个访问器烘焙成**权限闸门**：`EmitSetterBody` 对 `Access != Mutate` 一律 `throw PermissionDeniedException("视图属性 'UnitAttackTriggerView.Host' 未声明 [Mutate]，禁止写入。")`；
3. 而 `Host` 声明是 **`[Optional][Read]`**（`CardEventView.Host` 与 `UnitAttackTriggerView.Host` 同此）
   ⇒ **setter 必抛** → 经 `PropertyInfo.SetValue` 包装为 `TargetInvocationException`。
- **对照（为何 hook 型能跑）**：hook 型监听（如 `join_basic`）的宿主是**经数据面**注入的——
  `Trigger<TView>.InvokeAsync` 在 `new Context(effectiveData)` **之前**把宿主写进**数据字典**（`enriched["Host"] = hostCard`），
  视图 getter 从 `__orc_data["Host"]` 取值，**从不走 setter** ⇒ 不受闸门约束。
- **结论**：`InjectByFramework` 的宿主注入走了**属性写入**这条被闸门禁止的通道；应改为**数据面注入**（与内核同手法）。

### I27: 修法选择（宿主注入通道） {Completed（甲）}
- 候选：
  - **甲（数据面注入，推荐）**：`WrapWithHostCore` 改为 `ctx.Data["Host"] = effect.HostOrNull;` 后再调 `inner`。
    - 依据：`Context.Data` 是 `internal`，`Effect.cs` 在 `Orc` 程序集内**可直接访问**；
      `Context` 构造时**已拷贝**调用方字典（`Data = new Dictionary<string,object?>(data)`），
      `ContextViewBinder.Create<TView>` 把**同一引用**交给代理（`Bind(ctx.Data, ctx)`）
      ⇒ 写 `ctx.Data` 与视图 getter 读取**同一载体**，语义与内核 hook 路径**完全一致**，且**不污染调用方载荷**。
    - 代价：`HostProperty<TView>` 反射缓存可整个删除（去掉一处反射与一次 `TargetInvocationException` 风险面）。
  - **乙（模板侧取施动卡）**：`ActorFrom` 支持原始表达式（如 `view.Attacker as Card`），inject 型模板不再依赖 `Host`。
    - 代价：`AttackDamageTriggerView` 等未来视图未必自带施动卡字段；各模板各自写取法，重复且易错。
  - **丙（内核加宿主注入面）**：让 `Trigger<TView>` 的**注册项**携带宿主、由 `InvokeAsync` 统一注入数据面。
    - 代价：改动内核注册模型（`TriggerRegistration`/`EventEntry` 加宿主），风险与收益不匹配（甲已达成同效）。
  - **丁（放开闸门）**：把 inject 目标视图的 `Host` 由 `[Read]` 改 `[Mutate]`。
    - 代价：把宿主写入权暴露给 csx 脚本，扩大脚本能力面（闸门本就是为约束脚本而设）——**不推荐**。
- **推荐**：**甲**。理由：与内核既有 hook 路径同手法（"数据面注入"）、零新 API、零内核模型改动、可删掉反射包装。

### I28: 「补信号」的三个候选里**两个没有发射点**（用户口径 vs 代码事实） {Completed}
- 用户口径（本轮）：`①收口 ＋ ②补信号`（交接文档 §8-2 的 `unit.combat.survived` / `unit.impact` / `card.converted`）。
- **代码事实（逐条核对）**：
  | 候选信号 | 机制是否存在 | 结论 |
  |---|---|---|
  | `unit.combat.survived` | ✅ 交战存在（`CommandManager.HandleDefaultAttackDamageAsync` 有互伤结算与死亡判定） | **可补**（发射点＝结算收尾，对存活参与者发射） |
  | `unit.impact`（冲击） | ❌ **不存在**——`KeywordRegistry` 注释明写「『守护』『冲击』属标注全集但**不在本批实现范围**」；全仓仅 `GameHooks.KardsImpactUsed`（`GameHookPendingStatus.NotPlanned`）一处留痕 | **不可补**（无发射点；要补需先实现「冲击」词条机制＝另一个批次） |
  | `card.converted`（转换） | ❌ **不存在**——只有底层「非死亡离场」原语 `CommandManager.LeaveBattlefieldAsync`（无生产调用方，仅测试调用）；**无转换组合、无转换入口** | **不可补**（同上） |
- **推论**：交接文档 §8-2 的"三个信号"里只有 1 个今天能落地；`unit.impact`/`card.converted` 的实际前置是"先实现词条/机制"，属**新批次**而非"补信号"。
- 待裁决：②的**实际范围**（见 I30）。

### I29: 「监听型 142」的真实构成与交接文档结论相矛盾 {Completed（据此重定范围）}
- 交接文档 §5 结论：「再往上走**主要靠补游戏层信号**，不是补词表」。**该结论与实测不符。**
- 实测（临时探针：把语料对拍里 `监听型` 未解析按**原文**分组；跑完已回退）——142 条的构成大致为：
  | 类别 | 规模 | 性质 |
  |---|---|---|
  | **信号已存在、只是句式/词表没覆盖** | ≈ 30＋ | `友方使用情报牌时`／`敌方指令使用时`／`友方使用英国指令时`（`card.played` 已有，缺「使用…牌」模式＋过滤）；`友方抽 1 张牌时`（`card.drawn` 已有，**`抽1张牌` 中间夹数字导致 `抽牌` 关键词失配**）；`友方额外获得 1 个指挥点槽时`／`友方失去 1 个指挥点槽时`（`slot.gained`/`slot.lost` 已有）；`敌方空军进入战场时`（`unit.deployed`＋兵种过滤） | **映射缺口，不是信号缺口** |
  | **机制存在、需新信号** | ≈ 20 | `交战并存活后`（→ `unit.combat.survived`）、`造成伤害时`（需"伤害来源"侧信号）、`消灭 N 个单位时`（需"击杀"信号） |
  | **机制不存在** | ≈ 60＋ | `升为老兵时`（老兵）、`冲击后`/`具有冲击时`（冲击）、`转换卡牌时`（转换）、`被完全修复时`（修复）、`反制触发时`（反制事件）、`隐蔽单位被揭示时`、`陆军撤退时`、`收缴`、`预报`、`预言类`（`两回合后`/`第二次后`） |
  | **条件态（非事件）** | ≈ 15 | `在支援阵线时`／`本单位在前线时`／`对抗坦克时`／`没有手牌时`／`具有冲击时` |
- 另一处**真实缺陷**：`抽 1 张牌` 这类"动作词＋数字＋单位"的短语，`Tokenizer` 归一后是 `抽1张牌`，
  而 `ResolveListenTemplate` 用 `Contains("抽牌")` 判据 ⇒ **恒不命中**。同类风险面还有 `使用指令`（`指令使用时` 不命中）等。
- 待裁决：②是否改为"**先补映射缺口（信号已存在）**＋`unit.combat.survived`"，把"机制不存在"的一律留白（维持显式失败）。

### I30: **R8 违规：295 张卡被静默丢弃**（无效果、无诊断） {Completed}
- **实测**：语料 1553 条里 `Effects.Count == 0 && Unresolved.Count == 0` 的卡面 = **295 条**。
- **定性**：直接违反需求 **R8**（"超子集文本产生显式未解析记录，**不静默丢弃**、不产占位效果"）。
- **成因（代码定位）**：`SemanticMapper.TryBuildOps` 末尾 `if (ops.Count == 0) { ok = false; }`——
  但 `failures` 里**没有对应记录**（每个纯目标声明 / 纯条件子句在 `clause.Actions.Count == 0` 分支里 `continue`，不计失败），
  于是 `MapEffect` 的 `unresolved.AddRange(failures)` **加了 0 条** ⇒ 整卡**既无效果、也无诊断**。
- **触发面**：`IsTargetDeclaration`（E1-20 为"先声明目标、后接动作"引入）在**整条效果都是目标声明**时漏进静默通道。
  样例：`相邻陆军具有 +2 攻击力。`／`每有 1 个相邻单位，具有 +2 攻击力。`／`在前线时，具有 +1 攻击力。`／
  `使 1 个友方战斗机具有 +1 攻击力和奋战，直到回合结束。`
- **影响**：语料覆盖率数字**失真**——"产出 0 效果 559 条"里有 295 条**并非"无效果文本"**，而是解析器把它们**吃掉了**；
  同时"未解析记录 297"这个"缺口可审计"的承诺对这 295 条**不成立**。
- 待裁决：是否本轮一并修（修法＝`ops.Count == 0 && failures.Count == 0` 时补一条诊断记录；语义见 I31）。

### I31: `ops.Count == 0` 的诊断口径 {Completed（乙）}
- 候选：
  - **甲**：只要 `ops.Count == 0`，就补一条 `Fail(ast.Span, "未产出任何可执行效果（整条均为目标声明/条件子句）。")`——
    与既有"无法识别动作短语"并列，覆盖率数字随之**下降**（295 从"0 效果"变"未解析"）。
  - **乙**：只对"确有已识别 token 但无动作"的整效果补诊断（即现 `IsTargetDeclaration` 情形），
    对"整条是纯条件"另给一条原因（如"纯条件效果暂不在子集内"）。
  - **丙**：本轮不修，仅登记（继续失真）。
- **推荐**：**乙**（诊断更准确，且保留 E1-20 的"目标声明不计失败"意图——只是要求"整效果颗粒度上不能一个 op 都没有还不出声"）。
- **连带影响**：语料对拍数字会变（`产出 ≥1 效果` 与 `未解析记录` 同时变化），**需在受控变更清单里申报**。
- **落地**：已按**乙**实施（`SemanticMapper.TryBuildOps` 整效果粒度补诊断）；
  对拍报告新增「既无效果、也无诊断（R8 违规面）」列：**295 → 0**；覆盖率 64.0% → **64.8%**。

### I32: `使用X牌`／兵种过滤族**不是纯词表工作**（需"事件卡属性过滤"新能力） {Completed（裁决：本轮不做 → 见 I35）}
- **实测**（I29 的原文分组）：`监听型 142` 里最大的一族是
  `友方使用情报牌时`／`使用海军牌时`／`友方使用协力牌时`／`友方使用英国指令时`／`友方使用中立牌时`／
  `敌方使用明牌时`／`敌方使用花费不小于 4 的指令时`／`敌方空军进入战场时`／`敌方单位升为老兵时`（老兵除外）等，
  **信号（`card.played` / `unit.deployed`）都已存在**，缺的是**对"事件卡属性"的过滤**（卡类型/词条/子类别 tag/阵营/花费/兵种）。
- **矛盾点**：这些**不能**简单地映射到 `played_basic` 了事——`played_basic` 不做事件卡过滤时，
  `友方使用情报牌时` 会变成"**友方打出任意牌都触发**"，属**语义错误**（比"显式失败"更糟）。
  （既有 `使用指令`→`played_basic` 映射**已有同类宽松问题**，本轮只是不再扩大它。）
- **要正做的成本**：`DslCondition` 增一类"事件卡匹配"kind（承载既有 `DslFilter`）＋ `EffectCompiler` 渲染真实 csx 守卫
  （读 `view.Card` → `Orc.Game.Cards.CardBase.Keywords.Has(...)`／`TagData` 子类别／`Definition` 类型·阵营·花费）
  ＋ `filters.json` 词表扩充 ＋ 语料与 csx 真编译校验。
- **可用的既有素材（已核对）**：`CardBase.Keywords.Has(string)`（词条读口）、`KeywordIds.Intelligence = "情报"`、
  `TagData`（**开放 tag 集合**，明注"海军/T-34/谢尔曼"）；阵营/花费在 `FactionCostData`。
- 待裁决：见 I34。

### I33: `slot.gained`／`slot.lost` 监听**缺归属守卫的取值面** {Completed}
- 语料：`友方额外获得 1 个指挥点槽时`（3）／`友方失去 1 个指挥点槽时`（≈6）／`友方第一次额外获得指挥点槽时`（1）——**信号已存在**。
- **问题**：`slot.*` 载荷是 `{ Player, Amount }`、**没有事件卡**，故既有归属守卫（读 `view.Card ?? view.Unit`）**不适用**
  ⇒ 不写守卫则"友方额外获得…时"会在**敌方**获得时也触发（敌我通吃，语义偏）。
- **可行修法**：归属守卫增加一个**载荷 `Player` 分支**——`self.Owner` 与 `view.Player` 同一性比较；
  hook 型监听里 `self`（宿主卡）**是可得的**（内核按"所有者是带宿主的效果"注入 `Host`），故该守卫可写、可编译。
- 待裁决：见 I34。

### I34: 本轮**过滤守卫能力**的边界（主问题） {Completed（甲）}
- **甲（推荐）**：**部分引入**——归属守卫增加"载荷 `Player` 分支"（供 `slot.gained`/`slot.lost` 等无事件卡信号使用，成本极小）；
  **不**引入"事件卡属性过滤"⇒ `使用X牌`／兵种过滤族（≈22 条）本轮**不做**、登记下一项。
- **乙**：**全引入**——再加 `DslCondition` 的"事件卡匹配"kind ＋ 编译器守卫 ＋ 词表，`使用X牌` 族一并吃下。
- **丙**：**都不引入**——`slot` 监听也走"无过滤"（敌我通吃），最省但语义宽松，且扩大既有宽松口径。
- 已锁定（不受本裁决影响）：①inject 收口（I27 甲）、I30/I31（静默丢弃补诊断）、`unit.combat.survived` 全链、
  `抽 N 张牌` 句式失配修复（`Contains("抽牌")` → 容忍数字间隔）。
- **落地**：按**甲**实施完毕（见 `implementation.md` **E1-39**）。

---

## 本轮收尾时**新发现**的未决问题（不属上表，登记待裁决）

### I35: 「本单位…时」监听**不校验事件主体是否为宿主卡**（自指缺失） {Open}
- **事实**：`SemanticMapper` 的监听分支只做**归属过滤**（friendly/enemy → owner 守卫），
  **没有**"事件主体 == 宿主卡"的校验 ⇒ 词面为 `本单位X时` 与 `友方单位X时` **走同一条路**。
- **后果**：`本单位交战并存活后`（载荷 `{Unit}`）会在**任意单位**交战时触发（不只是宿主自己）；
  `本单位受到伤害时`（载荷 `{Card}`）同理（任意卡受伤即触发）。
  - **例外/自愈**：`death_basic`（`card.died`）因死亡链第 ⑦ 步"效果卸载"**先于**第 ⑨ 步 `card.died` 发射 ⇒ 宿主已卸载、不会误触发。
- **修法候选**：
  - **甲**：`DslCondition` 增"自指"kind（`self.same`），渲染为 `object.ReferenceEquals(self, (view.Unit ?? view.Card))`；
    `SemanticMapper` 识别监听短语里的"本单位/该单位"⇒ 加该守卫。
  - **乙**：不动（接受宽松；与本轮已交付的 `combat_survived_basic` 一致）。
- **影响面**：既有全部含"本单位…"的监听映射（`damaged_basic`/`combat_survived_basic`/`stat_basic`…）；本轮**未做**。
- **注**：本轮端到端用例刻意改用 **`友方单位交战并存活后`**（归属守卫可精确定义断言），避免把该缺口伪装成"已正确"。

### I36: `使用X牌`／兵种过滤族 —— 需"事件卡属性过滤"新能力（承接 I32） {Open}
- 方案已备（I32）但**用户裁决本轮不做**。做之前需一并裁决：过滤维度词表的**归属**（复用 `filters.json` vs 新建）、
  `TagData` 开放子类别（"海军"）与 `KeywordIds`（"情报"）与 `FactionCostData`（阵营/花费）三类读口的**统一条件形状**。

### I37: 机制缺失的监听族（**无发射点，不建议补信号**） {Open（建议维持显式失败）}
- 老兵（`BecomesVeteran:`/`VeteranOf:` 仅在属性映射的"未实现"清单里）／冲击（`KeywordRegistry` 明注未实现）／
  转换（只有底层 `LeaveBattlefieldAsync`，无生产调用方）／完全修复（`RepairDefenseAsync` 无信号）／
  反制触发／隐蔽揭示／撤退／收缴／预报／回合计数类（`两回合后`/`第二次后`）。
- 结论：这些**不是"补信号"能解决的**，前置是**实现对应机制**——属新批次。

---

# 第三轮：「提高官方卡可解析百分比」的现状体检（E1-40 前置）

> 用户口径：**继续完善解析器，提高官方卡可解析的百分比**。按 grill 规则先做**量化体检**（临时探针，跑完已回退），再裁决口径与主攻方向。

### 体检数据（当前基线：覆盖率 64.8%＝1006/1553；**547 张卡产出 0 效果**）

按"卡面"分层（探针启发式分类，**有重叠、仅供排序**）：

| 桶 | 规模 | 内容 | 可达性 |
|---|---|---|---|
| **A** 属性/状态类（`具有`/`获得` ＋ **可表达目标**） | **23** | `所有友方步兵具有 +1 攻击力。`／`其他友方坦克具有 -1 行动花费。`／`使 1 个友方步兵具有 +2 攻击力，直到回合结束。` | ✅ 机制已有（`buff`/`costMod`/`grant`）——**缺动作键 `具有` 与"无动词数值"路径** |
| **B** 属性/状态类（自指／复杂目标） | **15** | `相邻陆军具有 +2 攻击力。`（相邻）／`友方"轻步兵"具有山地。`（未实现词条）／`敌方指令具有 +2 花费。` | ⚠️ 目标选择器维度不足（`相邻`/位置/阵营/构筑） |
| **C** 机制缺失 | **178** | 老兵／冲击／转换／修复／撤退／收缴／预报／开发／隐蔽／流亡／协力／门禁（`无法…`） | ❌ 前置＝实现机制 |
| **D** 条件态／持续态前缀 | **230** | `对抗坦克时，具有 +2 攻击力。`／`敌方回合中，具有 +3 攻击力。`／`每有 1 个相邻单位，具有 +2 攻击力。`／`本回合，所有友方空军具有 +1 攻击力和闪击。`／`若友方先手，具有 +1 花费。` ／（并存若干**监听型映射缺口**：`敌方指令使用时`/`友方反制触发时`/`本单位对敌方总部造成伤害时`） | ⚠️ 拆两半：**"条件"＝需真实求值面**；**"具有 +N"＝可沿用 A 的动作路径** |
| **E** 其它 | **101** | `抉择：…`（modal）／专有句式 | ❌／⚠️ |

**另外**：未解析记录 TOP 里 `478`＝"未产出任何可执行效果"（整条无动作词）已由 E1-38 可见化——**它就是 A+B+D 的毛面**。

### I38: **覆盖率口径**——"结构可解析"是否计入？（主问题） {Completed（甲）}
- **背景（既有先例）**：`DslCondition.RawKind` 已把"其它条件"渲染为 `if (false /* TODO */)`（E1-11 裁决），
  且 `Parse_Maps_Condition_Clause_As_Placeholder` 断言这种效果**算"已解析"**（如`部署：如果手牌不少于2张，抽一张牌。`）。
- **矛盾点**：若沿用该口径，则 `对抗坦克时，具有 +2 攻击力。` 也会变成"**结构可解析、运行时永不触发**"（`if (false)`），
  覆盖率可大涨，但数字里混入"死效果"。
- 候选：
  - **甲（推荐）**：**计入**（沿用 E1-11 先例），但**对拍报告分层披露**——新增
    「产出效果（总）」「其中**语义完整**（无占位条件、无 `needsCsx`）」「含占位条件」「含 `needsCsx`」四列，
    使百分比**可审计、不注水**。
  - **乙**：**不计入**——条件必须先有真实求值面；本轮先做"条件求值"能力，再谈覆盖率（工程量大，短期百分比不动）。
  - **丙**：**分轨指标**——报告只给两轨：`结构覆盖率`（甲口径）与 `语义覆盖率`（乙口径），不做单一数字。

### I39: 主攻方向与排序（子问题） {Completed（1 → 3 → 2，暂不做 4）}
- 候选杠杆（按"性价比"排序建议）：
  1. **属性/状态类动作路径**（A ＋ D 里的 `具有 +N` 部分）：加动作键 `具有`／无动词数值描述 → `buff`/`costMod`/`grant`；
     目标不可表达 ⇒ `needsCsx`。**纯解析层、零游戏层改动**。
  2. **`aura` op（持续态的正确机制）**：对接既有 `AuraRegistry`/`AuraDeclaration`（host ＋ field ＋ delta ＋ source ＋ **predicate**），
     `EffectRuntime` 出门面 ⇒ 让"具有 +N"真正生效（且能表达 `相邻`/阵营等谓词）。**中等工程量**。
  3. **监听型映射缺口**（含 `使用X牌` 的**事件卡属性过滤**能力，I32/I36）：≈22＋条。
  4. **`抉择`（modal）**：≈43 记录，需 modal 机制（选择一次，两条分支）。
  5. **机制补齐**（C 桶 178）：老兵/修复/冲击/转换/撤退/收缴/开发/隐蔽…——**新批次**。
- 推荐排序：**1 → 3 → 2 → 4**（先把"能正确表达"的吃掉，再落地持续态机制，再谈 modal）。

---

## 第三轮实现 grill（用户裁决：口径＝甲；先做 **1 → 3 → 2**，暂不做 4）

> 口径假设：**甲**（"结构可解析"计入覆盖率，但报告分层披露"语义完整/含占位条件/含 needsCsx"）。

### I40: **既有缺陷**——自指（无目标）效果的选靶**打错人**（`sel` 无 `self`） {Completed（甲）}
- **代码事实**：`SemanticMapper.BuildSelector` 在"无目标短语/目标短语无限定词（本单位、其…）"时返回 **null**；
  `OpTemplateCatalog` 把 null 选择器的 `{{sel}}` 渲染为 **`"one"`**；
  而 `EffectTargetResolveJudicator.DefaultRule` **没有 `self` 分支**，且 `side == null` 时
  `MatchesSide` 恒真、`SelectLines` 顺序＝**A 支援线 → B 支援线 → 前线**。
  ⇒ `部署：获得闪击。` / `使本单位获得 +1+1。` 实际作用于"**A 方支援线第一个单位**"，**不是宿主自己**。
- **影响**：这是一条**已在覆盖率里的既有语义错误**（不是"未解析"），凡无目标的自指 op 都踩：
  `获得闪击`／`获得 +1+1`／`获得 N 个指挥点槽`?（资源类不经选靶，不受影响）…
- **修法候选**：
  - **甲**：`EffectSelector`/判定器增 `sel == "self"` ⇒ 直接返回 `viewer`（单点、零歧义）；
    并把"无选择器"的 `{{sel}}` 缺省从 `"one"` 改为 **`"self"`**（无目标＝作用于自身）。
  - **乙**：op 模板侧特判（`sel` 为空时直接把 target 置为 `self`，绕过 SelectAsync）。
- **必须修**（否则杠杆 1 的自指 `具有 +N` 会继承同一错误）。

### I41: 杠杆 1「属性/状态类动作路径」的**语义完整性范围**（主问题） {Completed（甲：三件全做）}
- 前置事实（已核对）：
  - `具有` **不在** `actions.json`（故整条无动作词 ⇒ 0 op）；
  - **期限已有机制**：`Modifier` 构造接受 `ModifierExpiry`（相位 ＋ 过滤器，挂载即自订阅、到期自注销），
    但 `EffectRuntime.BuffAsync/CostModAsync` **未暴露期限参数**（需加参数/重载 ＋ op 字段 ＋ 模板占位符）；
  - **单子句多属性**（`具有 +1 攻击力和奋战`）需把宾语列表展开为 2 个 op（`buff` ＋ `grant`）；
  - 词条名（`闪击`/`奋战`/`伏击`…）已在 `filters.json`，`grant` 可直接用。
- 候选：
  - **甲（推荐）**：**三件全做**——① `sel: "self"`（I40）② 期限 `until`（`直到回合结束`/`直到下个友方回合开始`）③ 单子句多属性展开。
    语义完整，可正确吃下 A 桶（23）与 D 桶里的"具有 +N"。
  - **乙**：只做 ①＋无期限的 `具有 +N`（单属性）；带 `直到…`／多属性／难表达目标 ⇒ `needsCsx`。
  - **丙**：只做 ①（必修）＋ 词表加 `具有`，其余一律 `needsCsx`（最小改动）。
- 预期（粗略）：丙 ≈ +8~12 张（+0.5~0.8pp）／乙 ≈ +15~20（+1.0~1.3pp）／甲 ≈ +25~40（+1.6~2.6pp）。
- **推荐：甲**。理由：期限机制**已存在**（只差门面暴露），不做则 `使 1 个友方步兵具有 +2 攻击力，直到回合结束。`
  只能落 `needsCsx`，而这类恰是 A 桶常见形态；且"永久 buff"是**语义错误**，不能靠忽略期限硬上。

### I42: 杠杆 3「事件卡属性过滤」的**谓词形状**（承接 I32/I36） {Completed（乙：先只做词条维度，下一轮执行）}
- 三类读口（已核对）：①`CardBase.Keywords.Has(标识)`（词条，如 `情报`）；②`TagData` **开放子类别 tag**（如 `海军`）；
  ③`FactionCostData`（阵营/花费）；卡类型（指令/单位/反制）在 `CardDefinition`。
- 候选：
  - **甲**：**统一为 `DslFilter` 扩展**（新增 `CardType`/`Tag`/`Faction`/`CostAtMost` 等维度），条件 kind 承载 `DslFilter`，
    编译器渲染为真 csx 守卫（读 `view.Card`）；报告正常计数。
  - **乙**：本轮只支持**词条**（`Keywords.Has`）一种维度（覆盖 `使用情报牌时` 等），其余留白。
- 推荐：先 **乙** 起步（维度收敛、可验证），甲作为后续扩展点。

---

## 第四轮：杠杆 1 收尾（E1-41）与**新一代**缺口

### I43: 杠杆 1 途中抓出的 3 个既有词法/语法缺陷 {Completed（均已修）}
1. **符号丢失**：`-1` → `Unknown("-")` ＋ `Num(1)` ⇒ 负值被读成正值（`-1 行动花费` 静默变 `+1`）；
2. **数量短语被当载荷**：`使 1 个友方步兵具有 +2 攻击力` ⇒ 载荷取到 `1`（阿拉伯数字成 `Num`、中文数字成 `Quant`）；
3. **`amount` 非负**：`DslOpRegistry` 对所有 op 拒绝负 `amount` ⇒ `costMod -1` 无法表达。
- 修法与影响见 `implementation.md` **E1-41 §4**。

### I44: 「语义完整」口径下 `其他/其它`（排除自身）与计数/对抗条件 {Open（已用 needsCsx 守卫兜住）}
- 现状：`其他`/`其它`（排除视角卡）与 `每有`/`对抗`/`回合中` 等**计数/对抗/相位**语式
  在 `state` 路径一律落 `needsCsx`（避免产出"多包含宿主自己"或"单次无条件"的**错误效果**）。
- 回收路径：① 选择器增"排除视角卡"维度（`EffectSelector`/判定器各一处）；② 计数/对抗条件需**真实条件求值面**。

### I45: 下一轮＝杠杆 3（事件卡属性过滤，I42＝乙） {Completed}
- 目标：`友方使用情报牌时`／`敌方指令使用时` 等（信号 `card.played` 已有）——只支持**词条**维度
  （`CardBase.Keywords.Has(标识)`），条件 kind 承载词条标识、编译器渲染真 csx 守卫。
- 同时可顺带修 `ResolveListenTemplate` 的 `使用…牌`／`指令使用时` 句式覆盖。
- **落地**：见 `implementation.md` **E1-42**；新增条件 kind `eventCard.keyword` 与合取 `all`；
  覆盖率 72.4% → **72.7%**（＋4 张）。

### I46: 杠杆 3 的**收益上限**实测（下一步主攻方向的依据） {Completed（据此改攻归属类信号）}
- **实测**：`监听型 125` 条按原文分组后，**"事件卡属性过滤"只占 ≈11 条**（且只有 `情报` 落在词条维度）；
  真正的大头是"**新信号 / 机制缺失 / 条件态**"：
  | 族 | 规模 | 缺口性质 |
  |---|---|---|
  | 老兵（晋升） | ≈17 | **机制缺失**（`BecomesVeteran:` 未实现） |
  | **伤害归属**（`本单位造成伤害时`／`本单位对敌方总部造成伤害后/时`／`友方单位造成 1 点对战伤害时`／`目标单位造成伤害时`） | ≈11 | **需新信号**（"伤害来源"侧；现有 `card.damaged` 只有受方） |
  | **消灭归属**（`本单位消灭 N 个单位时/后`／`友方单位消灭…`） | ≈5 | **需新信号**（"击杀者"） |
  | **指向**（`被敌方指令指向时`／`敌方指令指向友方…`） | ≈4 | **需新信号** |
  | **反制触发**（`友方反制触发时`／`触发敌方反制时`） | ≈3 | **需新信号** |
  | 条件态（`在支援阵线时`／`对抗X时`／`没有手牌时`／`具有冲击时`） | ≈12 | 需**条件求值面** |
  | 冲击／转换／修复／隐蔽揭示／撤退／钳击加成／预报／失去烟幕／压制移除 | ≈15 | **机制缺失** |
  | `使用X牌` 的**其余维度**（tag／卡类型／阵营／卡名／花费阈值） | ≈11 | I42 **甲** |
  | 计时类（`两回合后`／`第二次后`／`三回合后`） | ≈3 | 需计数/计时机制 |
- **结论**：再往上走的主攻是 **① 补归属类信号（伤害来源/击杀/指向/反制，≈23 条）**、
  **② I42 甲的其余维度（≈11 条）**、**③ 机制补齐（≈32 条）**、**④ 条件求值面（≈12 条）**。
  其中 **①** 与既有信号模式完全同构（`GameUpdates`＋`GameHooks`＋`GameHooksJson`＋`GameHooksTests` 四处同步），性价比最高。

---

## 第五轮：归属类信号（E1-47）

### I47: 死亡归属的**实现路径**——"防御归零统一死亡衔接"抢占问题 {Completed（已解）}
- **矛盾事实（实测）**：战斗路径里 `ProcessDeathAsync(target, killer: attacker)` 那句"结算路径内防护兜底"**通常不执行**——
  `ApplyDefenseDamageAsync` 内的跑链 → `card.stat.changed` → 总线 → `HandleDefenseDepletionAsync` → `ProcessDeathAsync(**无归属**)`
  **先行**完成死亡 ⇒ 实测 `card.died` 的 `Killer` 为 **null**。
- **裁决（本轮实施）**：**伤害来源游标**——伤害门户与死亡衔接**同一同步调用链**，故以**结算窗口内**的临时游标
  （`CommandManager._damageSourceCursor` ＋ `WithDamageSourceAsync`，try/finally **恢复式**）承载施动方；
  死亡衔接与战斗路径两处都据此归因；窗口外＝null（修饰到期等非归属驱动死亡）。
- **备选（评估后未采用）**：把来源经 `ApplyDefenseDamageAsync` → 跑链 → `card.stat.changed` 载荷**逐层透传**——
  链路深（修饰容器的集中触发点在下游）、且会把"伤害概念"塞进 `card.stat.changed` 载荷。

### I48: 载荷字段守卫的**主体口径**（自指 vs 归属） {Completed（E1-50 已补齐归属面）}
- **本轮**：`payload.self`（`ReferenceEquals(view.X, self)`）与 `payload.hq`（`view.X is Hq`）——
  覆盖「本单位造成伤害时／本单位消灭…时／…对敌方总部…」。
- **留白**：`友方单位造成 1 点对战伤害时`／`友方单位消灭 1 个敌方单位时`／`目标单位造成伤害时` 需
  「施动方/击杀者的**归属面**」＝`Killer.Owner`／`Unit.Owner` 与 `self.Owner` 比较（新守卫形状）——**未做**，
  且**已用 `NeedsPayloadSubject` 保证"无本单位主体 ⇒ 不映射"**（不泛触发）。

### I49: `unit.targeted`／`counter.triggered`／效应侧归属 {Completed（效应侧归属已做；前两项待做）}
- **`counter.triggered`**：发射点明确（`CounterCard.HandleUseCounterAsync`）⇒ 下一轮可直接做（模板＋映射＋四处同步）。
- **`unit.targeted`**：**无干净发射点**——"指向"分散在打牌/指令/效果选靶多个入口，**尚无统一受控面**；
  需先定"指向"的机制承载（是否＝无头选靶 `EffectRuntime.SelectAsync` ＋ UI 选靶桥的并集？）。
- **效应侧归属**：`EffectRuntime.DamageAsync`／`KillAsync` 未带来源 ⇒ 效果伤害/消灭不产生归属；
  需给门面加 `source` 形参并复用同一游标（`damage.csx.tpl`／`destroy.csx.tpl` 传 `self`）。→ **已完成（E1-50）**。

---

## 第六轮：归属补全（E1-50）

### I50: 主体归属面的**判定顺序**陷阱 {Completed（已解）}
- **矛盾事实**：`本单位对敌方总部造成伤害时` 里"**敌方**"是**宾语**（受方）——
  若按"短语中任意位置出现 `敌方` ⇒ 施动方异主"判归属，会生成"`payload.self` ∧ `payload.owner.different`"的
  **矛盾守卫（恒假）**，效果永不触发（属"语法通过、运行死"的隐性错）。
- **裁决（已实施）**：主体判定**有序**——先 `本单位`／`该单位`（自指），再 `友方`，再 `敌方`；
  且"主体不可辨识 ⇒ **不映射**"（`NeedsPayloadSubject` 的判据改为 `PayloadGuardOf(...) is null`）。

### I51: 下一轮候选（按性价比） {Completed（本轮做 1、2；3 顺延，见 I52–I55）}
1. **`counter.triggered`**（≈3 条）：发射点明确（`CounterCard.HandleUseCounterAsync`），照 E1-47 四处同步 ＋ 模板 ＋ 映射。
2. **事件卡过滤其余维度**（≈11 条，I42 甲）：复用 `eventCard.keyword` 形状扩到 tag／卡类型／阵营／卡名／花费阈值。
3. **`aura` op**（持续态落地；让 `具有 +N` 可表达 `相邻/阵营` 谓词）。
4. **条件求值面**（`raw` 占位转真实）⇒ 回收 `对抗…时`／计数态。
5. **`unit.targeted`**：需先定"指向"的机制承载。
6. **机制缺失族**（老兵/冲击/转换/修复…）：属机制批次。

---

## 第七轮：`counter.triggered` ＋ 事件卡过滤扩维 ＋ `state` 正确性修正（E1-52/53/54/55）

### I52: **`state` 路径"静默丢弃限定词"是正确性缺陷**（不是覆盖率问题） {Completed}
- **取证**：把 `state` 族逐条打出来后，发现 8 类卡面产出**错误效果**（详见 `implementation.md` E1-52 §1）——
  如 `相邻陆军具有 +2 攻击力` 被加成到**宿主自己**、`本单位左侧所有单位具有 -1 行动花费` 作用**全场**。
- **根因**：`AstBuilder` 的未知词**只在动词之后**才记录（`objectParts` 仅在 `actions.Count > 0` 时收集）⇒
  **动词之前的限定词被整段丢弃**，语义层"看不见"。
- **裁决（已实施）**：`TargetPhrase` 增 `HasUnrecognizedQualifier`（动词前未知词、排除纯句法虚词与通用名词）；
  宾语区未知词、两数值＋属性名词的歧义 一并拒绝 ⇒ 落 `needsCsx`。**语义完整覆盖率 50.7% → 48.6%（主动下修＝真实水位）**。

### I53: `counter.triggered` 的**发射分支** {Completed}
- 只在 `HandleUseCounterAsync` 的**激活分支**发（取消分支是退点撤销，不属"触发"）——已落地并加声明。

### I54: 事件卡过滤扩维的**取值安全** {Completed}
- 维度取值（卡类型/阵营）**经枚举白名单校验**（`Enum.TryParse`＋`IsDefined`），**从不把原文拼进 csx**；
  多维用**互不相同的模式变量名** + **扁平合取**（否则 `all` 嵌套且模式变量重名会编译失败）。
- **卡名维度必须用原文切片**：`TriggerNode.RawText` 已剥引号 ⇒ 由 `RawOf(original, listen.Span)` 取回带引号原文。

### I55: **`aura` op**（用户：先做 3、再补 2）→ **已完成（E1-56）** {Completed}
- **顺延理由（实测）**："aura 族"多数卡面带**不可表达的目标限定**（`相邻`／`本单位左侧`／`防御力为 1 的`／`受伤`／
  `手牌中的`／`谢尔曼`）⇒ 光有 aura 机制也映射不了；能被 aura 表达的（可辨识谓词）**本来就用 `buff` 近似**。
  真正的缺口是**谓词表达能力**（相邻/位置/阈值/受伤态），不是"缺少 aura op"。
- **设计已备**（`implementation.md` E1-55）：`EffectRuntime.DeclareAuraAsync` ＋ `AuraDeclaration`（source＝门面 ⇒ 卸载即撤）
  ＋ `RerunAllCardsAsync` 衔接 ＋ `EffectAuraFilter`（side/faction/unitType/keyword/excludeSelf/**adjacent**，读取面已在 `GameEnvironment`）；
  映射判据＝**静态 `X 具有 +N` ⇒ aura**、**触发体内 `使…具有…` ⇒ buff**（修正现"动/静混用"）。
- **另需**：`DslSelector.Zone` 从未传给 `SelectAsync`（模板占位符缺失）⇒ 凡 zone 限定（`支援阵线所有单位`／`手牌中的`）
  现在都是**丢 zone 的近似**——应在 aura（或补 `{{selZone}}`）时一并收口。

---

## 第八轮：`aura`（E1-56）＋ 事件卡四维运行期端到端

### I56: **动/静机制错配**——静态持续态曾用一次性修饰器近似 {Completed}
- **事实**：E1-41 把"无触发的 `X 具有 ±N`"也映射成 `buff`/`costMod`（**登记时一次性快照**）——
  而 KARDS 静态文本的语义是"**在场期间持续**"（进出/位置变化须**实时重算**）。
- **裁决（已实施）**：**静态（无触发且无期限）⇒ `aura`**（`AuraDeclaration` ＋ `RerunAllCardsAsync` 衔接，
  来源＝门面 ⇒ 卸载整组撤销；宿主离场由既有「在场」门禁兜住）；**触发体内/带期限 ⇒ 修饰器**（一次性、可带期限）。
- **谓词不进 csx**：csx 只给 `EffectAuraFilter(Side, Faction, UnitType, Keyword, ExcludeSelf, Zone)`，
  谓词本体由游戏层构造（`Environment.AreAdjacent` 等读取面已在，**相邻尚未接**——因 `相邻X` 的 X 多为**组词**）。
- **取舍记录**：为不留死代码，**未加 `Adjacent` 字段**；`zone` 字段保留（静态 zone 限定文本理论上必要）。

### I57: `selZone` 收口（E1-41 起的**静默丢 zone**） {Completed}
- `OpTemplateCatalog` 增 `{{selZone}}`；**8 个走 `SelectAsync` 的 op 模板**由 `null` 改为 `{{selZone}}` ⇒
  zone 限定首次真正传入选靶。**遗留**：`SelectLines` 只认 frontline/support ⇒ `hand`/`deck` 类会退化为空选靶
  （应显式失败而非静默无操作——已登记为下一步）。

### I58: 补 2 = 事件卡过滤**四维运行期端到端**（E1-54 收尾） {Completed}
- 见 `implementation.md` E1-56 §6。**踩坑**：监听宿主须真实上场（支援位 0 被测试基座占用 ⇒ 用 1、2）；
  被使用卡只需实例（`InstantiateLoadedAsync(toHand: false)`，否则干扰手牌基线）；
  反例探针必须选"四维全不命中"的卡（否则反例本身命中别的维度——本轮首版即栽在此）。

---

## 第九轮：语义完整口径主攻（E1-57）

### I59: 语义完整口径的**结构化体检** {Completed}
- 探针：非语义完整的卡 = **含占位条件 78 张（仅 6 种，全是数值比较算子）** ＋ **含 needsCsx 318 张（274 种，长尾）**。
- **裁决**：先做**单点最大的数值条件**（78 张 ⇒ 回收 46 张），needsCsx 长尾属**机制实现**批次（非解析改动）。

### I60: 比较短语的结构化取面（易错点记录） {Completed}
1. **算子在 AST 里只留了原文**（Cond.RawText='不大于'），左度量在 TargetPhrase.Filters、右操作数
   被误当动作的"第二数值" ⇒ 新增 ComparisonPhrase 一次性取全三者（**修正**：算子后的数值不再进动作载荷）。
2. **规范串里不能再用 :**：度量本身含 =/;，若用 count:s=… 则 Split(':') 会把度量劈成两段（v1 曾栽在此）。
   现用 count=s=…／points=s=…／stat=f=…;s=…;z=hq（**度量内不含 :**）。
3. **计数标记的边界**：…数 判计数需要"**多字**未知段"——友方单位数（run=单位数）是计数，
   而 剩余指挥点数 的 数 被过滤器切成**单字**（run=数）**不是**计数（否则会误判成 count）。
4. **HQ 属性取面**：总部**没有** defense 修饰字段（GetEffectiveValue 会抛）——总部防御力＝Hq.Health。

### I61: 下一轮候选（语义完整口径续攻） {Open（**暂停**：用户口径＝先完善游戏层表达力再回解析侧，见 implementation.md E1-58）}
1. **对手对象取面**（若友方总部防御力大于敌方总部，raw 中 ≈8）：求值层需"对手 Player"（现只能经实际单位反查，
   或给 GameEnvironment 加只读玩家访问面）。
2. **下限/上限钳制**（花费至少为 1）：非条件——需 op 级"钳制"语义。
3. **状态/历史条件**（若有友方动员单位／若上回合没有被攻击）：需状态与历史查询面。
4. **needsCsx 长尾 318 张**：双倍伤害/洗入卡组/复制/门禁(无法攻击或移动)/开发/山地/老兵 —— **机制实现**批次。
---

## 阶段决议：**解析侧暂停**（E1-58）

- **用户口径**：解析侧**先做到这里**；**后续先完善游戏层表达力（机制／信号／选靶面），再回来拓展解析**。
- **依据**：结构可解析 74.6%／语义完整 52.2%；未解析 587 条中 **375 条是"未识别动作词"——多为机制描述**
  （无法…／升为老兵／反制其部署／与其战斗）⇒ **继续堆词表推不动语义完整**。
- **归因表／能力边界／恢复条件**：交接文档.md **§6**（能力边界与暂停点）、implementation.md **E1-58**。
- **I26–I61 全部结案**（I61 的候选转为"游戏层完成后的回收清单"）；此前所有裁决均已成文并可追溯。