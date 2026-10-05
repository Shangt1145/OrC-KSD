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

### I11: 解析段 AST 的结构与"AST ↔ DSL"分层 {In Progress}
- 待裁决：AST 是否**镜像 DSL**（几乎同构）、还是**与 DSL 解耦的中性语法树**（触发短语/条件子句/目标短语/动作短语/数值 → 再语义映射到 DSL）／极简短语序列。
- 关联：若 AST ≡ DSL，则"AST 作为中间层"名存实亡（用户明确要求引入 AST 作中间层）。

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
