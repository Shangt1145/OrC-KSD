# Implementation — OrC 效果解析器

> 实现 grill 已完成，本文件即**可执行实现计划**。
> 目标：三层流水线 ① 模板效果（专用格式 + 槽位声明）② DSL（官方带参原语 + csx 逃生舱）③ tokenizer + AST；最终产物＝**带 csx 的 `EffectSnapshot` JSON 文本**。
> **转换段（S1–S5）与解析段（S6–S11）均已完成**；扩展 E1（扩表达力）**部分完成**；全量测试 1208 项通过。

## Scope
- **Target**：全部代码落 `src/Orc.Game` 下新命名空间 **`Orc.Game.EffectParsing`**；资产落 `src/Orc.Game/EffectParsing/Templates/`。
- **Excludes**：`src/Orc` 内核改动；通用解释器/运行时装载；卡级字段接线；UI/AI/网络；数值对拍。

## Constraints
- 构建 0 错误 / 0 警告；测试全绿。
- **不触动 `src/Orc` 内核**。
- 生成物**只保证结构合法**（`PrefabJson.Deserialize` 通过），**不保证 csx 可编译/可运行**（把 `Orc.Game` 加入 csx 白名单为后续任务）。
- 句式内建、词表外置；官方原语无法表达者只能走 `csx` 逃生舱。
- 超子集文本 → **显式失败 + 诊断**（原文 span + 原因），不产占位效果。

---

## 转换段（已完成）

### S1: DSL 模型 + 序列化 + 校验 {Done}
- Target: `Orc.Game.EffectParsing.Dsl`
- 交付物：`DslModels.cs`（`DslEffectInstance`/`DslSlotFill`/`DslOp`/`DslSelector`/`DslFilter`）、`DslJson.cs`（读写 + fail-fast）、`DslOpRegistry.cs`（官方原语登记 + 参数校验）。
- Acceptance:
  - [x] DSL 实例可 round-trip 序列化（测试 `Dsl_RoundTrips`）。
  - [x] 非法 DSL（缺 template / 空 op 序列 / 未登记原语 / 缺必填参数）被显式拒绝。

### S2: 模板效果（专用格式）与加载 {Done}
- Target: `Orc.Game.EffectParsing.Templates` + `Templates/deploy_basic.tpl.json`
- 交付物：`EffectTemplate.cs`（同构子集模型 + slots）、`EffectTemplateSlots.cs`（定位解析 `mainTrigger.events[e1]` / `otherTriggers[<id>].events[e2]` + 语义校验）、`EffectTemplateJson.cs`、`EffectTemplateLoader.cs`（目录加载、单文件隔离、重复 id 隔离）。
- **落地修正**：模板触发器采用**触发器级 `stableKey`**（对齐 `Orc.Cards.TriggerPrefab`）——原提案草图里的 `root.stableKey` 在真实模型（`EffectPrefab` 无该字段）中不存在，故按 EffectSnapshot 真实结构对齐。
- Acceptance:
  - [x] `*.tpl.json` 可加载为 `EffectTemplate`。
  - [x] 悬空槽位目标 / 槽位指向已有固定 csx 的事件 / 非槽位事件缺 csx ＝ 加载失败。

### S3: op 语句模板 + 渲染 {Done}
- Target: `Orc.Game.EffectParsing.Compilation.OpTemplateCatalog` + `Templates/ops/*.csx.tpl`
- 交付物：6 个 op 模板（`damage`/`draw`/`buff`/`grant`/`move`/`csx`）；占位符契约：整数（`amount`/`count`/`attack`/`defense`）、C# 字符串字面量（`op`/`keyword`/`zone`）、原样注入（`script`）、紧凑 JSON（`target`/`filter`，供注释/诊断）。
- Acceptance:
  - [x] op → csx 片段渲染正确、占位符全部替换（残留＝抛）。
  - [x] 未知占位符在**加载期**即被隔离。

### S4: 转换器（DSL → 带 csx 的 EffectSnapshot） {Done}
- Target: `Orc.Game.EffectParsing.Compilation.EffectCompiler`
- 交付物：模板查找/槽位覆盖校验 → 各槽位渲染 csx → 包裹成入口变量 `Func<View, Context, CancellationToken, Task> HandleAsync = (view, ctx, ct) => { ... };` → 组装 `EffectSnapshot`（`id`/`version` 由调用方给，`stableKey` 沿用模板）。
- Acceptance:
  - [x] 输出可被 `PrefabJson.Deserialize` 解析（测试 `Compile_Output_RoundTrips_Through_PrefabJson`）。
  - [x] 主触发器来自模板、`events[].csx` 来自 DSL；无 `assemblyKey` 路径。

### S5: MVP 原语与模板落地 + 转换段验收 {Done}
- Target: 1 骨架模板 + 5 op 模板 + 测试 `tests/Orc.Game.Tests/EffectParsingTests.cs`
- Acceptance:
  - [x] MVP 端到端可跑（15 项测试通过）。
  - [x] 全量回归为绿（1194 项测试，0 失败）。
- **已知取舍**：MVP 的 op 语句模板 body 是**占位实现**（`await Task.CompletedTask;` + 参数注释），因本轮明确不做执行、且 csx 只能引用内核程序集；待把 `Orc.Game` 加入 csx 白名单后，只需改 `Templates/ops/*.csx.tpl` 即可落地真实调用。

---

## 解析段（待开工：需先与用户对齐 tokenizer/AST 具体语法）

### S6: 外置词表资产 {Done}
- Target: `Orc.Game.EffectParsing/Lexicon/`
- 交付物：`LexiconSet.cs`（**最长匹配** + **一字面一类别**强制）、`LexiconLoader.cs`（文件名 → 类别映射、单文件隔离）；10 个词表文件（triggers / connectives / pronouns / actions / quantifiers / sides / zones / filters / conditions / numerals）。
- **落地修正**：Action 条目携带的是**动词键**（`damage`/`attack`/`destroy`/`draw`/`gain`/`move`），不是 op 名——因为同一动词（如"获得"）可映射到多个 op（`grant`/`buff`），映射由 S9 依宾语判定。一致性测试相应改为"动词键 ∈ 已知动词集合"。
- Acceptance:
  - [x] 词表可加载（0 失败）。
  - [x] 存在"词表动词键 ↔ 已知动词集合"一致性测试。

### S7: tokenizer（12 类） {Done}
- Target: `Orc.Game.EffectParsing.Parsing` —— `TextNormalizer.cs`（归一 + **原文偏移映射**）、`Token.cs`（12 类 + 标点子型 `hard/soft/clause/list/colon/quoteOpen/quoteClose`）、`Tokenizer.cs`。
- Approach：标点先分类；阿拉伯数字成 `Num`；否则**词表最长匹配**；连续未命中字符合成 `Unknown`。
- Acceptance:
  - [x] 12 类 token 可产出；span 指**原文**（切片自洽）。
  - [x] 未命中字符合成 `Unknown`（测试用"点伤害"）。
  - [x] 引号以 `QuoteOpen`/`QuoteClose` 产出（供切分器按深度跳过）。

### S8: AST 构建 + 多效果切分 {Done}
- Target: `Orc.Game.EffectParsing.Parsing` —— `Segmenter.cs`、`Ast.cs`、`AstBuilder.cs`
- 落地修正：`SegmentedEffect.HardBoundary` 语义定为"**本单元是否自成链首**"（false＝承接上一个软边界、需继承），与 kards-diy"分号后仍受前面控制"一致；新增 `PunctKinds.List`（`、`）——触发短语内不断句、正文内断子句。
- Approach（**已定稿**）：
  - **切分**（I13）：预处理（换行归一 + 纯词条行丢弃 + 成对引号屏蔽 + 未闭合引号不切分）→ `。！？` 硬边界 / `；` 软边界（继承**最近的硬边界单元**）→ 产出 `SegmentedEffect{ Text, Span, HardBoundary }`。
  - **AST 节点**（中性语法树；携带词法归类值；节点均带**原文 span**）：
    ```
    EffectAst { TextSpan Span; BoundaryKind Boundary; EffectAst? InheritsFrom; TriggerNode? Trigger; ClauseNode[] Clauses }
    TriggerNode { TriggerSyntaxKind Kind(Named|Listen|Implicit); string RawText; TriggerEventPhrase[] Events; TextSpan Span }
    TriggerEventPhrase { string RawText; TextSpan Span }
    ClauseNode { ConditionPhrase? Condition; TargetPhrase? Target; ActionPhrase[] Actions; TextSpan Span }
    TargetPhrase { string? QuantifierRaw; string? SideRaw; string? ZoneRaw; FilterPhrase[] Filters; bool ExcludeSelf; TextSpan Span }
    FilterPhrase { FilterKind Kind; string RawText; object? Value; TextSpan Span }
    ActionPhrase { string VerbRaw; string? ObjectRaw; PayloadNode? Payload; TextSpan Span }
    PayloadNode { PayloadKind Kind(Integer); int Int; string Raw; TextSpan Span }
    ConditionPhrase { string RawText; TextSpan Span }
    ```
  - 触发短语＝句首 → 第一个 `：` 或第一个 `，`；无显式触发时**尽量解析为被动触发器**；**多事件**在 `TriggerNode.Events` 并列，由 **S9 展开为 N 个效果**。
  - 代词安全阀：认不准不替换。
- Acceptance:
  - [ ] 一卡多效果切成多个 `EffectAst`；软边界单元带 `InheritsFrom`。
  - [ ] 每个 AST 节点带**原文** span（归一到原文偏移映射可用）。
  - [ ] 多事件触发在 AST 内并列，S9 能展开为多效果。

### S9: AST → DSL 语义映射 {Done}
- Target: `Orc.Game.EffectParsing.Parsing.SemanticMapper`
- Approach：动词键 + 宾语 → op（`damage`/`draw`/`grant`/`buff`/`move`）；多事件展开为多效果；其余 → `Unresolved{ Span, RawText, Reason }`。
- Acceptance:
  - [x] 子集内 AST 映射为 `DslEffectInstance`。
  - [x] 超子集（消灭 / 监听型触发 / 未知事件 / 条件子句）产出显式 `Unresolved`，不产占位效果。
  - [x] 多事件尽量拆分（能映射的才产出，其余记未解析）。

### S10: 解析入口 + 验收 {Done}
- Target: `Orc.Game.EffectParsing.Parsing` —— `EffectParser.cs`、`ParseResult.cs`；测试 `tests/Orc.Game.Tests/EffectParserTests.cs`。
- Approach：`Parse(text) → ParseResult{ Effects, Unresolved }`；测试内建 10 条代表语料（覆盖 damage/draw/grant/move/全体/超子集/监听/多事件/词条行/多效果）并生成 `.md` 覆盖率报告。
- Acceptance:
  - [x] 入口返回"效果数组 + 未解析记录"。
  - [x] 存在测试守门 + `.md` 覆盖率报告（当前 **8/10**；2 条为**预期**的超子集显式失败）。
  - [x] 端到端：卡面文本 → DSL → 带 csx 预制体 → `PrefabJson.Deserialize`。

### S11: 术语登记与文档 {Done}
- Target: 根 `CONTEXT.md`
- Acceptance:
  - [x] 登记：`模板效果`、`效果 DSL`、`词法单元（token）`、`词法层（tokenizer）`、`语法树（AST）`、`效果解析器`。

---

## 扩展 E1：扩表达力（用户裁决＝B）

### E1-1: 让 csx 可用游戏层 {Done}
- `src/Orc.Script/CSharpScriptSecurityOptions.cs`：`DefaultAllowedAssemblies` 追加 **`Orc.Game`**。
- **默认导入不变**（不预置 `Orc.Game.*`）：`using` 不存在的命名空间＝编译失败，而脚本宿主未必加载游戏层
  （如纯内核/脚本测试环境，实测会打挂 6 项 `Orc.Script.Tests`）。改用**全限定类型名**，宿主亦可经 `AllowedImports` 自行追加。

### E1-2: 效果运行时门面 `EffectRuntime` {Done}
- `src/Orc.Game/Effects/EffectRuntime.cs`：csx 的**唯一**游戏层受控入口（集中暴露，不给各服务零散加 public 面）；
  `ResolveFor(card)`（卡 → 玩家 → 服务，与既有服务同构）；方法：`KillAsync`（**死亡链**）／`DamageAsync`／`BuffAsync`／`GrantAsync`／`DrawAsync`／`SelectAsync`；
  服务不可达＝降级不抛错（false／空集）。
- 装配：`Match.Initialize` 创建并注入各玩家（`Player.EffectRuntime` + `ConfigureEffectRuntime`）；协作者延迟读取（指挥管理器创建较晚）。

### E1-3: 无头选靶判定器 {Done}
- `src/Orc.Game/Judicators/EffectTargetResolveJudicator.cs`＋`JudicatorNames.EffectTargetResolve`（`effect.target.resolve`）；
  `Match.Initialize` 内置固定注册；`GameHooks` 追加转发常量并同步 `JudicatorNameList`/`BuiltInJudicatorNames`（15→16 / 13→14）。
- 默认规则：side（相对视角）＋ zone ＋ keyword 过滤 ＋ sel/count；**`unitType` 过滤与 `random` 真随机待补**。

### E1-4: 击杀公开面 {Done}
- `CommandManager.KillUnitAsync`（**internal**）包装 `ProcessDeathAsync`（游戏层死亡链：亡计/词条注销/修饰清理/`card.died`）——
  与总线 `card.destroyed`（内存销毁，可能只是弃牌）**严格区分**。

### E1-5: `destroy` 原语全链 {Done}
- `DslOpRegistry` 加 `destroy`；`SemanticMapper` 的 `destroy` 动词键 → `op destroy`（原为"超子集失败"）；
- `Templates/ops/destroy.csx.tpl` → `EffectRuntime.KillAsync`。

### E1-6: 其余 op 模板改真实调用 {Done（move 除外）}
- `damage`／`draw`／`buff`／`grant` 模板改为经 `EffectRuntime` 真实调用（选择器实参由新占位符 `{{sel}}`/`{{side}}`/`{{filterUnitType}}`/`{{filterKeyword}}` 注入）。
- **`move` 仍为占位**：移动需"外层收尾"（扣行动费/刷新动作态），`EffectRuntime.MoveAsync` 尚未提供。

### E1-7: 施动卡来源机制（D-甲） {Done}
- `EffectTemplate.ActorFrom`（`eventCard`｜`effectHost`）＋ `*.tpl.json` 的 `actorFrom` 字段（缺省 `eventCard`；非法值＝加载失败）；
- 转换器 `WrapHandler` 按声明生成**施动卡变量 `self`**：
  - `eventCard` ⇒ `var self = view.Card as Card;`
  - `effectHost` ⇒ `var self = (view.Effect is Effect hostEffect && hostEffect.IsMounted ? hostEffect.Host : null) as Card;`
- 全部 op 模板**统一改用 `self`**（不再各自从 `view.Card` 取）；`deploy_basic` 标 `actorFrom: "eventCard"`。

### E1-9: 监听型触发（hook 监听） {Done}
- **引擎侧补载荷（①）**：`Orc/Core/Trigger.cs` 在绑定视图前，若触发器所有者是**带宿主的效果实例**，把宿主卡注入
  视图数据（**复制字典后注入**，不污染事件流载荷）；`Orc/Cards/CardLifecycle.cs` 的 `CardEventView` 增可选 `[Read] Host`。
- 模板：`death_basic`（`card.died`）／`position_basic`（`unit.position.changed`）／`drawn_basic`（`card.drawn`），
  均 `actorFrom: "effectHost"`、槽位 `on_event`。
- 映射：`SemanticMapper.ResolveListenTemplate` 按事件关键词（被消灭/阵亡/死亡 → death；移动/前线 → position；抽牌 → drawn）；
  未命中＝显式失败（② 回退）。
- `WrapHandler` 的 `effectHost` 分支改为 `var self = view.Host as Card;`。

### E1-11: 条件子句（A） {Done}
- DSL：`DslOp.Condition`（`DslCondition`：`owner.same`｜`owner.different`｜`raw`）＋ JSON 往返。
- **归属过滤＝真实 csx**：`owner.same` / `owner.different` 渲染为
  `if (self is CardBase actorSelf && view.Card is CardBase actorEvent && actorSelf.Owner is not null && ReferenceEquals(actorSelf.Owner, actorEvent.Owner))`（异主取其反）。
- **其它条件＝占位**：`raw` ⇒ `if (false /* TODO 条件占位：<原文> */)`。
- AST：`TriggerNode` 捕获监听短语内的阵营；**纯条件子句（无动作）**→ 整效果的占位条件（不再计为失败）。
- 映射：监听短语的 friendly/enemy → `owner.same` / `owner.different`（**监听归属过滤落地**）。

### E1-12: `move` 真实化（B） {Done}
- `CommandManager.MoveUnitAsync`（**internal**）：依 zone 取**首个空槽**，走与指挥同一执行链（`DispatchMoveAsync`：复验触发器 → 外层收尾：扣行动费 / 刷新动作态）。
- `EffectRuntime.MoveAsync`；`Templates/ops/move.csx.tpl` 改为真实调用。

### E1-14: 生成 csx 的真编译校验（C） {Done}
- 测试侧引入 `Orc.Script`（`Orc.Game.Tests` 加 `ProjectReference`——**生产侧不依赖**），新增
  `EffectParserTests.Generated_Csx_Compiles`：把 9 条代表语料生成的全部 csx 用 `CSharpScriptEvaluator` **真编译**并断言成功。
- **校验当场抓出 3 个真 bug**（此前生成物根本不可能编译）：
  1. 包装 lambda 缺 `async`（`await` 在非 async lambda 内非法）；
  2. `async` lambda 内 `return Task.CompletedTask;` 非法（异步 lambda 返回 `Task` 时不可 return 值）——移除该行；
  3. 条件守卫里的 `ReferenceEquals` 在脚本顶层不可达 ⇒ 改 `object.ReferenceEquals`；
  4. 条件守卫里的 `CardBase` 未限定（csx 默认导入不含 `Orc.Game.*`）⇒ 改**全限定** `Orc.Game.Cards.CardBase`。

### E1-15: 选靶补全（D） {Done}
- **unitType 过滤**：`EffectTargetResolveJudicator.MatchesUnitType`——选择器给的词表键（如 `infantry`）经 `Enum.TryParse<UnitType>` 后与
  `unit.GetData<UnitStateData>().UnitTypes` 比对（未知兵种词＝不匹配，不静默放行）。
- **random 真随机**：经 `MatchRandomService.ResolveFor(card).PickN(candidates, take)` 取样（不可用＝确定性取首个）；
  `Match.Initialize` 注册时注入该解析器。
- `sel == "all"` ⇒ 全部；否则 `take = Count ?? 1`，再按 `sel == "random"` 决定随机/顺取。

### E1-16: 语料对拍（官方卡牌 zh-Hans） {Done}
- 新增 `tests/Orc.Game.Tests/EffectParseCorpusTests.cs`：定位仓库内 `docs/初始设计/kards官方卡牌.json`（缺失＝跳过），
  全量跑解析器并产出 `corpus-coverage.md`（汇总 + 未解析原因 TOP30 + 样例 20 条）。
- **实测结果**：卡面 1553 条 → 产出效果 **336 条（覆盖率 21.6%）**、效果总数 364、未解析记录 1906。
- **未解析原因 TOP**：无法识别动作短语 1055 ／ 监听型触发不在子集 419 ／ 无法判定的获得类 233 ／ 事件不在子集 108 ／ 造成伤害缺数值 35 ／ 未知动作 35。

### E1-18: 语料扩展·第 1 轮 {Done}
- **动作词**：`压制`→`pin`、`抑制`→`silence`；`移动` 别名（`移动至`/`移至`/`移到`）。
- **新 op**：`pin` / `silence`（`EffectRuntime.PinAsync` / `SilenceAsync` → `SuppressRules.ApplyAsync` / `InhibitRules.ApplyAsync`）＋ 各自 csx 模板。
- **监听模板 +2**：`turn_end_basic`（`turn.end`）、`played_basic`（`card.played`）＋ 关键词映射（"回合结束"／"使用指令/打出指令"）；
  并引入 `SupportsOwnerFilter`——`turn.end` 载荷**只有 Player 无事件卡**，故**不做归属过滤**（否则守卫恒假、效果永不触发）。
- **对拍刷新**：覆盖率 **21.6% → 25.8%**（产出效果 336 → **401** / 1553）；效果总数 364 → 447；未解析 1906 → 1811。
  未解析 TOP 变化：动作短语 1055→1020、监听型 419→353、获得类 233→237。

### E1-20: 语料扩展·第 2 轮（引号卡名 / 目标声明 / 新 op） {Done}
- **引号封装的卡牌引用**：词法层把引号整段识别为**一个** `Filter{dimension:"name"}` token（内容原样保留在 `Lexeme`，span 覆盖整段，
  引号内标点不再参与切分）；AST 归类为 `FilterKind.Name`；DSL 新增 `DslOp.Name` 字段。
- **"先声明目标、后接动作"句式**：`ClauseNode.IsTargetDeclaration`（无动作但含已识别 token）；映射层不再把它计为未解析，
  其目标供后续子句的代词回指（`指向 1 个单位，使其移至前线`）。
- **新 op `addToHand` / `shuffleIn`**：`EffectRuntime.AddToHandAsync` / `ShuffleInAsync`——
  按**卡名**在卡池反查定义（`MatchCardService.RegisteredDefinitions`，首个同名）→
  `CreateAndPlaceToHandAsync` / `CreateAndPlaceIntoDeckShuffledAsync`（后者发 `deck.shuffled`）；词表补 `加入手中`/`加入手牌`/`放入手中`/`洗入`。
- **对拍刷新**：覆盖率 **25.8% → 35.0%**（产出效果 401 → **544** / 1553）；效果总数 447 → 628；未解析 1811 → **886**。
  **"无法识别动作短语" 1020 → 80**（目标声明 + 引号卡名 + 新动词的合力）。
  当前 TOP：监听型 353、获得类 229、事件不在子集 108。

### E1-22: `获得`类多义（向后看消歧） {Done}
- **机制**：`获得`（动词键 `gain`）的含义**取决于其后的 token**——映射层按下列次序判定：
  ① 属性名词（`攻击力`/`防御力`）+ 数值 → `buff`；
  ② **两个数值**（`获得 +1+1`）→ `buff(attack, defense)`（AST 增 `ActionPhrase.SecondaryPayload`）；
  ③ 对象名词 `花费` → `costMod`；
  ④ 对象名词 `指挥点槽` → 显式失败（无公开入口）；
  ⑤ 词条名词 → `grant`；⑥ 其余 → 显式失败。
- **AST/词表**：`FilterKind.Object`（新维度）+ `filters.json` 增 `花费`/`行动花费`/`指挥点槽`；`PayloadNode` 支持第二数值。
- **新 op `costMod`**：`EffectRuntime.CostModAsync` → `AddModifier(CardStatFields.OperateCost, delta, this)`；`costMod.csx.tpl`。
- **对拍刷新**：覆盖率 **35.0% → 42.0%**（产出效果 544 → **653** / 1553）；效果 628 → 756；未解析 886 → **742**；
  "无法判定的获得类" **229 → 69**。

### E1-23: "op 无法解析"⇒ 原文保留 + 标记需要 csx 实现 {Done}
- **规则（用户口径）**：**句式层**失败（无动作词、监听型未映射、事件不在子集…）⇒ 仍计未解析；
  **op 层**失败（动作键无对应 op、缺参数、无判定依据…）⇒ 不再计未解析，改为产出 `op: "needsCsx"`，
  把**原句原文**放进 `script`，模板渲染为 `// TODO **需要 csx 实现**：<原文>` + no-op（注释安全化：换行折空格、`*/` 断开）。
- 交付：`DslOpRegistry.NeedsCsxOpName`、`needsCsx.csx.tpl`、`OpTemplateCatalog` 新增注释安全占位符 `{{rawText}}`。

### E1-24: 事件骨架复用（亡计/动员） {Done}
- `EventTemplates` 增 `亡计 → death_basic`（hook `card.died`）、`动员 → position_basic`（hook `unit.position.changed`）；
  修正**槽位名硬编码**——`SlotNameFor(template)`：部署骨架 `on_deploy`，监听/事件骨架 `on_event`。
- **对拍刷新**：覆盖率 **42.0% → 55.0%**（产出效果 653 → **854** / 1553）；效果 756 → 987；未解析 742 → **477**。
  未解析 TOP 收敛为三项：**监听型 353**、无法识别动作短语 81、事件不在子集 43。

### E1-27: inject 型监听（攻击类）＋ 对局级具名触发器解析面（任务 4，D-甲） {Done}
- **前置（对局级具名触发器解析面）**：新增 `Orc.Game.Cards.MatchNamedTriggers`（名 → 视图类型 ＋ **延迟提供器**）；
  `Match.Initialize` 登记四个流程触发器（`指挥触发器`/`单位移动触发器`/`单位攻击触发器`/`造成攻击伤害触发器`）并注入各玩家；
  `EffectSystem` 的 `injects` 解析在**宿主卡未命中后回退**到对局级表。
- **视图面**：`UnitAttackTriggerView` 增 `[Optional][Read] Host`（与 `CardEventView.Host` 同一手法——内核把宿主注入视图数据，声明即可取）。
- **固定句式优先用模板效果**（用户口径）：新增 `attack_basic.tpl.json`——**active 主触发器**（无 hooks）＋ `injects: [{ target: "单位攻击触发器", event: "e1" }]`，
  `actorFrom: effectHost`；映射 `攻击…` → `attack_basic`（该模板不做归属过滤——视图无 `Card`）。
- **包装层**：视图类型名一律改**全限定**（csx 默认导入不含 `Orc.Game.Commanding`）。
- **对拍刷新**：覆盖率 **55.0% → 57.8%**（产出效果 854 → **898** / 1553）；未解析 477 → **417**；**监听型 353 → 285**。

### E1-28: 各类事件监听模板扩展 {Done}
- 新增 5 个 hook 型监听骨架（均 `actorFrom: effectHost`、槽位 `on_event`）：
  `deploy_listener`（`unit.deployed`）／`join_basic`（`unit.joined`）／`shuffle_basic`（`deck.shuffled`）／
  `turn_start_basic`（`turn.start`）／`destroy_basic`（`card.destroyed`——**引擎销毁**，与死亡链 `card.died` 严格区分）。
- 关键词映射扩展：`回合开始`／`摧毁`／`洗切·洗牌`／`加入`／`部署`。
- **归属过滤改为白名单口径**（`OwnerFilterableTemplates`）：只有**载荷/视图确实带事件卡**的模板才加 owner 守卫，
  避免 `turn.start`/`deck.shuffled`（只有 Player/Deck）等恒假、`attack_basic` 视图无 `Card` 属性而编译不过。
- **对拍刷新**：覆盖率 **57.8% → 62.1%**（产出效果 898 → **965** / 1553）；未解析 417 → **326**；**监听型 285 → 179**。

### E1-29: 嵌套效果解析（任务 2） {Done}
- **判定**：`获得` + 引号内容含触发界定符/句号（`：`/`。`）⇒ 判为**内嵌效果文本**（而非卡名）。
- **递归回调**：`SemanticMapper` 增 `Func<string, ParseResult>` 构造入参，由 `EffectParser` 注入**自身 `Parse`**——
  内嵌文本走**完整管线**（tokenizer → 切分 → AST → 映射）。
- **承载**：DSL 增 `nested` op，`DslOp.Nested` 携带**内部 DSL 效果序列**（JSON **递归**往返）；
  `Templates/ops/nested.csx.tpl` 为占位（内容型词条落地到 `KeywordManager.GrantWithContentAsync(keyword, Effect?)`
  需 **Effect 实例**，csx 造不出 ⇒ 运行时落地另行设计）。
- **注意**：本轮**不改变未解析计数**（此前这类 op 已由 `needsCsx` 兜住），只把承载从"留痕"升级为"结构化内嵌效果"。

### E1-25: 指挥点槽事件改进（用户口径，**已完成**）
- 定义三个信号：**额外获得 n 个指挥点槽**、**失去 n 个指挥点槽**、**指挥点槽改变**；
  前两者**内部**汇聚到「指挥点槽改变」；**回合开始的槽递增直走「指挥点槽改变」**（不走额外/失去）。
- 卡牌效果可走「额外获得/失去」，也可**改写回合开始的递增**（moding 注入面）。
- 落地需：`ResourceManager` 由只读 `MaxPointSlots` 改为**可变槽**+ 受控入口；`Match` 装配注入；`EffectRuntime` 暴露受控方法。
- 可执行计划见文末 **「E1-25 实施计划」**；实现层未决问题见 `implementation-grill-plan.md` 的 I16–I22。

### E1-26: 未完成项 / 下一轮候选
- **嵌套效果解析未实现**（用户已提出）：`获得：“亡计：将 1 张本单位的复制加入手中…”` —— 需
  ① 引号内容含效果特征时判为"内嵌效果文本"（而非卡名）；② 映射层接受**递归回调**（`Func<string, ParseResult>`，由 `EffectParser` 注入自身）；
  ③ DSL 增 `nested` op 携带内部 DSL；④ 模板为占位（内容型词条落到 `KeywordManager.GrantWithContentAsync` 需 Effect 实例，另需设计）。
- `获得`类多义收敛（229 条）：`+N 花费`、`1 个指挥点槽`。
- 监听型（353 条）：攻击类无对位信号（搁置）；可继续扩有信号的（`card.drawn`/`deck.shuffled` 等）。
- 生成 csx 用全限定 `Orc.Game.*`，宿主须加载 `Orc.Game`（对局内成立；纯脚本宿主需自加 `AllowedImports`）。

---

## E1-25 实施计划 — 指挥点槽事件改进（**已完成**）

> 需求口径与定稿设计见 `委托-指挥点槽事件改进.md`（用户称"已对齐定稿"）。
> 实现 grill 交叉核对代码后暴露的缺口/裁决见 `implementation-grill-plan.md` I16–I25。
> **落地结论**：构建 0 错误；`OrcEngine.sln` 全量测试 **1232 项全绿**（基线 1218 ＋ 新增 14 项）；
> 对拍报告"指挥点槽"未解析原因**归零**（覆盖率 62.1% → 62.7%）；点数事件改造见下「E1-25 后续」段。

- Target：全部改动落 `src/Orc.Game`（内核 `src/Orc` 不触动）。
- Excludes：点数（`player.Points`）的信号化；槽上限的对外修改入口；天气/充能；内核改动。

### S1: 三条信号常量 + 载荷键 + 转发面 {Done}
- Target：`GameUpdates.cs`、`GameHooks.cs`。
- Approach：`SlotGained` / `SlotLost` / `SlotChanged` ＋ 3 个静态 Emit 助手；载荷键 `PayloadAmount` / `PayloadOldSlots` / `PayloadNewSlots`；
  `Signals` 18→21、`PayloadKeys` 10→13。
- Acceptance:
  - [x] `GameHooks.Signals`（21）/`PayloadKeys`（13）与 `GameUpdates` 常量反射集一致。
  - [x] 每条新信号有静态 Emit 入口（`Every_Signal_Has_A_Static_Emit_Callpoint` 绿）。

### S2: GameHooksJson 清单表补齐（I18） {Done}
- Target：`Output/GameHooksJson.cs`。
- Approach：`SignalMetadata` 补 3 条 `SignalEntry`；`JudicatorMetadata` 补 `effect.target.resolve` ＋ 3 条资源判定器（15→19）。
- Acceptance:
  - [x] `Exporter_Emits_Parseable_Json_...` 通过（signals 21 / judicators 19）。

### S3: `ResourceManager` 受控面 {Done}
- Target：`Managers/ResourceManager.cs`。
- Approach：构造＝`ResourceManager(LogicEngine, Func<JudicatorRegistry?>, int = DefaultMaxPointSlots)`；
  `MaxPointSlots` → `{ get; private set; }`；`Settle` → `SettleAsync`（`TurnManager.cs` 调用点改 `await`）；
  新增 `GainSlotsAsync` / `LoseSlotsAsync` / 私有 `SetSlotsAsync`（`SettleAsync` 走同一收敛点）。
- Acceptance:
  - [x] `GainSlotsAsync`：未到上限＝`slot.gained`→`slot.changed`（Amount＝Δ）；在上限＝零信号。
  - [x] `LoseSlotsAsync`：槽 >0＝`slot.lost`→`slot.changed`（Amount＝Δ）；为 0＝零信号。
  - [x] `SettleAsync`：只发 `slot.changed`（不发 gained/lost）；数值/点数结果与现状一致。

### S4: 判定器（递增 + 加/减数字包裹） {Done}
- Target：`Judicators/PointSlotIncrementJudicator.cs`、`PointSlotGainAmountJudicator.cs`、`PointSlotLoseAmountJudicator.cs`
  ＋ `JudicatorNames` ＋ `GameHooks` ＋ `Match.Initialize` 内置固定注册段。
- Approach：`resource.slot.increment`（`(Player)→int`，默认 1）、`resource.slot.gain` / `resource.slot.lose`
  （`(Player,int)→int`，默认恒等）；`JudicatorNameList` 16→19、`BuiltInJudicatorNames` 14→17。
- Acceptance:
  - [x] `RegisterModing` 递增为 2 后 `SettleAsync` 递增为 2；注销回退 1。
  - [x] `RegisterModing` 改写 `resource.slot.gain` 后 `GainSlotsAsync` 按改写值生效；注销回退原值。

### S5: 效果侧接入 {Done}
- Target：`Effects/EffectRuntime.cs` ＋ `Match.Initialize` 注入。
- Approach：增 `Func<ResourceManager?>` 延迟读取器；新增 `GainPointSlotsAsync` / `LosePointSlotsAsync`（不可达＝false）。
- Acceptance:
  - [x] csx 经 `EffectRuntime` 加/减槽可达并生效。

### S6: 词表 + DSL op + 模板 {Done}
- Target：`Lexicon/actions.json`、`Dsl/DslOpRegistry.cs`、`Parsing/SemanticMapper.cs`、`Templates/ops/`。
- Approach：动作词 `失去 → lose`；`gain` 分支 `pointSlot` → `gainSlot`（原为显式失败）；新增 `lose` → `loseSlot`；
  登记 `gainSlot`/`loseSlot`（`Required:["amount"]`）；新增两个 csx 模板。
- Acceptance:
  - [x] `获得 N 个指挥点槽` → `gainSlot`、`失去 N 个指挥点槽` → `loseSlot`；csx 真编译校验通过；对拍原因归零。

### S7: 验收与受控变更申报 {Done}
- Target：`tests/Orc.Game.Tests/`。
- Acceptance:
  - [x] 构建 0 错误；`OrcEngine.sln` 全量测试全绿（1225 项）。
  - [x] 既有期望值受控变更逐条申报（见下）。

### 受控变更清单（对既有断言的改动，逐条申报）
1. `GameHooksTests`：`GameUpdates` 常量计数 18→21、载荷键 10→13；`JudicatorNames` 16→19、内置 14→17；
   `helpers` 13→16（＋3 新信号）；`GameHooksJson` 导出计数 signals 18→21、judicators 15→19（原 15 表本就缺 `effect.target.resolve`，一并补齐）。
2. `GameEntryPoints`（生产侧清单）：入口 `ResourceManager.Settle` → `SettleAsync`（签名/结果形态标注同步）。
3. `TurnCycleTests`：回合开始序列插入 `slot.changed`（`turn.start` 之后、`card.drawn` 之前）。
4. `UnitTurnsInPlayTests.Advance_Is_Silent_No_Extra_Updates`：既有 7 条 → 8 条（插入 `slot.changed`）。
5. `MatchInitializationTests`（2 处）：初始化更新总数 45→46；回合开始尾部序列插入 `slot.changed`。
6. `ScenarioTests`：初始化 45→46、回合 2/3 序列插入 `slot.changed`（7 条 → 8 条）。
7. `EffectParserTests`：`Action_Lexicon_Keys_Are_Known_Verbs` 已知动词集加 `lose`；`Generated_Csx_Compiles` 语料 +2（槽加/减）。
8. `EffectParsingTests`：`Op_Templates_Are_Shipped_And_Loaded` 断言集加 `gainSlot`/`loseSlot`。
- **新增测试**：`tests/Orc.Game.Tests/PointSlotEventTests.cs`（7 项——三信号语义/钳制/mod/EffectRuntime 可达）。
- **既有警告**：`MatchOpeningConfigTests.cs(142/143/150)` 三条 `xUnit2013` 为**基线既有**（该文件未改动），非本次引入。

---

## E1-25 后续 实施计划 — 指挥点事件改造（**已完成**）

> 需求变更来源与裁决见 `requirements-grill-plan.md` 的「追加需求：指挥点事件改造」段（R1–R6 全 Completed）。
> **落地结论**：构建 **0 错误**（3 条基线既有警告）；`OrcEngine.sln` 全量测试 **1232 项全绿**（上轮 1225 ＋ 新增 7 项）。

### P1: 三条点数信号 + 载荷键 + 转发面 + 清单表 {Done}
- `GameUpdates`：`point.gained`/`point.lost`/`point.changed` ＋ `PayloadOldPoints`/`PayloadNewPoints` ＋ 3 个静态 Emit；
  信号 21→**24**、载荷键 13→**15**；`GameHooks` 转发与清单同步；`GameHooksJson` 清单表 +3（signals 21→24）。
- Acceptance: [x] 反射集一致；[x] 每条新信号有静态 Emit 入口。

### P2: 点数受控面（两路 + 共用入口） {Done}
- `ResourceManager`：`PointChangeKind{Set,Add}`；`ChangePointsAsync`（**唯一**通用落点，只发 `point.changed`）；
  `GainPointsAsync`/`LosePointsAsync`（卡效果语义路：各发 `point.gained`/`point.lost` 后汇聚）；
  `AddPointsAsync`（＝`AddPoints` 改名的薄包装，原校验/数值语义保留）；`ResolveFor(Card)` 静态解析面。
- Acceptance: [x] 值未变化＝零信号；[x] 失去下限 0；[x] 数字包裹改写为非正＝零信号、不抛错。

### P3: 两条点数判定器 {Done}
- `resource.point.gain` / `resource.point.lose`（`(Player,int)→int`，默认恒等）；判定器 19→**21**、内置 17→**19**；
  `GameHooksJson` JudicatorMetadata 19→21。
- Acceptance: [x] moding 改写生效、注销回退。

### P4: 6 处游戏内通用来源归口 {Done}
- `SettleAsync`（设为）／`OperateCosts.DeductAsync`（行动费，原 `Deduct` 改异步）／`CommandManager.FinalizeMove|FinalizeAttack` 改异步／
  `CommandCard.HandlePlayFinalizeAsync`／`UnitCard.HandlePlayChainAsync`／`CounterCard.HandleUseCounterAsync`（扣点＋退点）。
- 兜底：资源管理器不可达（脱局）＝保持既有直写语义。
- Acceptance: [x] 打牌/指挥/反制皆发 `point.changed`；[x] 无旁路直写残留（除脱局兜底）。

### P5: 效果侧接入 {Done}
- `EffectRuntime`：`GainPointsAsync` / `LosePointsAsync`（卡 → 玩家 → 资源管理器；不可达＝false）。

### P6: 解析侧 `gainPoint` / `losePoint` {Done}
- `filters.json` 增 `指挥点`→object/point（并从 `conditions.json` 迁出——解「一字面一类别」冲突）；
  `SemanticMapper` 的 `gain`/`lose` 分支增 `point` 处理；`DslOpRegistry` 登记两 op；新增两个 csx 模板。
- Acceptance: [x] `获得 N 个指挥点`→`gainPoint`、`失去 N 个指挥点`→`losePoint`；[x] csx 真编译校验通过。

### P7: 受控变更申报 {Done}
1. `GameHooksTests`：信号 21→24、载荷键 13→15、判定器 19→21、内置 17→19、`helpers` 16→**19**（＋3 点数信号）、
   JSON 导出 signals 21→24 / judicators 19→21。
2. `GameEntryPoints`：入口 `AddPoints`→`AddPointsAsync`；新增 `ChangePointsAsync` 条目。
3. **`AddPoints` → `AddPointsAsync`**（签名变更；无生产调用方）：测试侧 25 处调用点改 `await`；
   `TurnPhaseEffectTests` 两处订阅 lambda 改 `async`、三处 `Assert.Throws` 改 `ThrowsAsync`。
4. 回合开始序列再插 `point.changed`：`TurnCycleTests` / `UnitTurnsInPlayTests`(×2) / `MatchInitializationTests`(×2，46→**47**) /
   `ScenarioTests`（46→**47**、回合 2/3 8→9 条）。
5. 打牌/指挥/反制链新增 `point.changed`：`PlayChainCommandTests`(×2) / `PlayChainUpdateTests`(×2) / `PlayChainDeploymentTests` /
   `PlayChainCounterTests` / `EntryPointBeginTests` / `CommandUpdateTests`(×3) / `CommandSystemTests` / `CommandCombatTests`(×2) /
   `StatPortalTests`(×2) / `HqEntityTests` / `MatchOutcomeTests`。
6. `EffectParserTests`：`Op_Templates_Are_Shipped_And_Loaded` 断言集加 `gainPoint`/`losePoint`；编译语料 +2（点加/减）。
- **新增测试**：`tests/Orc.Game.Tests/PointEventTests.cs`（7 项——三信号语义/两路/共用入口/mod/EffectRuntime 可达）。

---

## E1-30：指挥点槽落地核对 ＋ 已有信号监听补全（Done）

- **核对**：用户侧指挥点槽改造**与本设计口径一致且全绿**（`ResourceManager` 三受控面、`slot.changed` 唯一"槽真变了"信号、
  额外/失去各发前置信号、回合递增不发前置；5 判定器；`EffectRuntime` 增减面；4 op ＋ 4 模板 ＋ 词表）。全量 **1232 项测试通过**。
- **本轮补**（纯解析层、零游戏层改动）：4 个**已有信号**的监听骨架——
  `stat_basic`（`card.stat.changed`）／`discard_basic`（`card.discarded`）／`burn_basic`（`card.burned`）／
  `hand_add_basic`（`card.hand.add`）；关键词映射加 `防御力/攻击力`、`弃`、`爆牌`、`获得卡牌/加入手牌`；归属过滤白名单同步补 3 条。
- **对拍刷新**：覆盖率 **62.1% → 63.1%**（产出效果 965 → **980** / 1553）；未解析 326 → **314**；**监听型 179 → 165**。

### E1-31：下一步（监听型剩余主体＝**需游戏层补信号**）
- 剩余监听型多为**无对位信号**：`受到伤害时`（伤害结算处）、`行动后/攻击后/移动后`（`unit.acted`）、`冲击后`（impact）、
  `交战并存活后`、`转换卡牌时`、`没有手牌时`（条件）。
- 建议**照你刚做的槽信号模式**补：新增信号 ＋ 在对应结算点发射 ＋ `GameUpdates`/`GameHooks`/`GameHooksJson` 同步（含一致性测试），
  然后我方加模板与关键词映射。
- 其余未解析（`无法识别动作短语` 106 / `事件不在子集` 43）多为**卡池内容层**（天气/老兵/充能/隐蔽）与专属机制，非通用机制，建议不做。

## E1-32：监听类补全（第二轮）＋ 缺口定性（Done）

- **本轮加 5 个"已有信号"监听骨架**：`types_basic`（`unit.types.changed`）／`load_basic`（`card.load`）／
  `placed_basic`（`card.placed`）／`turn_end_before_basic`（`turn.end.before`）／`turn_start_before_basic`（`turn.start.before`）＋ 关键词映射。
- **实测结论（重要）**：**对拍数字未变**（仍 63.1% / 监听型 165）——说明这 5 个模板在语料里**没有对应短语**（无增益、亦无害）。
- **缺口定性（据语料样例）**：监听型 165 的构成**全部是需要"新信号"的事件**——
  `受到伤害时`／`行动后`／`冲击后`／`交战并存活后`／`转换卡牌时`／`升为老兵时`／`使用海军牌时`（played＋过滤）／
  `具有冲击时`＋`没有手牌时`（**条件态**，非事件）。
- **结论**：监听类要再上台阶，必须走**游戏层补信号**（照 E1-25 槽信号的成功模式）：
  `card.damaged`（伤害结算处）／`unit.acted`（攻击/移动收尾后）／`unit.impact`（冲击）／`unit.combat.survived`（交战存活）／`card.converted`（转换）／`unit.veteran`（老兵）；
  每个信号都需同步 `GameUpdates` 常量与发射助手、`GameHooks` 常量与 `Signals` 列表、`GameHooksJson` 条目、
  以及 `GameHooksTests` 的 3 处计数断言（24→N）与发射点映射表。
- `无法识别动作短语` 106 与 `事件不在子集` 43 确认为**卡池内容层**（天气/老兵/隐蔽/重复此效果）与专属事件，非通用机制。

## E1-33：监听类补信号（`card.damaged` + `unit.acted`）（Done）

- **新信号（照 E1-25 槽信号模式，四处同步）**：
  - `card.damaged`（载荷 `{Card, Amount}`）：发射点＝`UnitCard.ApplyDefenseDamageAsync` 与 `Hq.ApplyDamageAsync`（**先落定、后发射**）；
  - `unit.acted`（载荷 `{Unit}`）：发射点＝`CommandManager.DispatchAttackAsync` / `DispatchMoveAsync`（外层收尾成功后）。
  - 同步：`GameUpdates`（常量＋`EmitCardDamaged`/`EmitUnitActed`）→ `GameHooks`（常量＋`Signals` **24 → 26**）→ `GameHooksJson`（`SignalMetadata` 条目）→ `GameHooksTests`（`Signals.Count` 24→26、JSON `signals` 24→26、`helpers.Count` 19→21、发射点映射＋2）。
- **落地时抓出并修正 2 个语义问题**：
  1. **改变才传播**——初版按 `amount > 0` 发射，会把「已被打到 0 后再受伤（零变化）」误报；改为**防御/血量实际变化**才发（与既有 `card.stat.changed` 口径一致）；
  2. **不为亡者发射**——`unit.acted` 对**已阵亡**攻击者不发射（`IsDestroyed` 守卫）。
- **模板与映射**：`damaged_basic`（`card.damaged`）／`acted_basic`（`unit.acted`）＋ 关键词 `受到伤害/受伤`、`行动`；归属过滤白名单补 `damaged_basic`（载荷含 Card）。
- **受控变更（7 处既有"精确信号流"断言）**：`CommandUpdateTests`×2、`CommandCombatTests`×1、`CommandSystemTests`×1、`StatPortalTests`×3（含 `Assert.Single(recorder.Updates)` → 按 `card.stat.changed` 过滤）。
- **对拍刷新**：覆盖率 **63.1% → 64.0%**（产出效果 980 → **994** / 1553）；未解析 314 → **297**；**监听型 165 → 142**。

## E1-34：DSL 可运行实现（进行中）

> 用户指示：**暂停解析扩展，专攻 DSL 的可运行实现**。

- **已修（本轮）**：**`engine.ScriptEvaluator` 在全仓库从未被装配**——`PrefabManager.ResolveHandler` 对 csx 一律返回
  `evaluator-missing`，即"生成的 csx 只要能编译也永远跑不起来"。现于 `Match.Initialize` 装配默认求值器
  `Engine.ScriptEvaluator ??= new Orc.Script.CSharpScriptEvaluator();`（宿主已装配＝不覆盖；内核仍零依赖——接口倒置口径不变）；
  为此 `Orc.Game` 增 `Orc.Script` 工程引用。全量 **1232 项测试通过**。
- **可运行路径**（据代码取证）：`Engine.Prefabs.RegisterPrefab(snapshot)`（注册生成的 `EffectSnapshot`）→
  按卡 `DeclarePrefab(cardId, [prefabId])`（`EffectSystem`；`CommandTestKit.CreateCommandMatch(effectRegistry: …)` 是测试侧装配源）
  → 卡加载时挂载 → 触发 hook/注入 → **csx 经求值器执行** → `EffectRuntime` 调游戏层。
- **待办**：端到端可运行测试（`EffectRuntimeEndToEndTests`）。**已知坑**：
  ① `PrepareOnSupportAsync` 走的是**加入**路径（发 `unit.joined`，**不发** `unit.deployed`）⇒ e2e 需用 `join_basic` 系模板或走真实打出；
  ② `Match.EffectSystem` 未见公开访问面——需经 `effectRegistry` 参数路线或补受控访问器；
  ③ csx 使用**全限定** `Orc.Game.*`（默认导入不含游戏层命名空间）⇒ 运行宿主必须加载 `Orc.Game`（本批已保证）。
