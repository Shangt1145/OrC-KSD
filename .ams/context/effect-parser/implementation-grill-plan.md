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
