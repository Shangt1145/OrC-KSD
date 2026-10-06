# Implementation — OrC 效果解析器

> 实现 grill 已完成，本文件即**可执行实现计划 + 逐条实现记录**。
> ⚠️ **接手请先读 `交接文档.md`**（当前状态、交付物、剩余问题、硬约束、下一步探针都汇总在那里）。
> 目标：三层流水线 ① 模板效果（专用格式 + 槽位声明）② DSL（官方带参原语 + csx 逃生舱）③ tokenizer + AST；最终产物＝**带 csx 的 `EffectSnapshot` 文本**，并**真编译 + 真运行**。
> 当前：转换段（S1–S5）✅、解析段（S6–S11）✅、扩展 E1（扩表达力/可运行）**大部分完成**；全量 **1234 项（1233 通过 + 1 显式 Skip）、0 失败**。
> 下方 E1-xx 段落按时间追加，**每一步都记录了根因与修法**（含若干内核潜伏缺陷）。

## Scope
- **Target**：解析/转换代码落 `src/Orc.Game/EffectParsing/`；**可运行所需的运行期与内核面**已在交付中一并修改（见交接文档 §2.2）。
- **Excludes**：通用解释器；卡级字段接线；UI/AI/网络；数值对拍。

## Constraints（**已随任务演进修订**）
- 构建 0 错误 / 0 警告；测试全绿。
- ~~不触动 `src/Orc` 内核~~ → **已按需最小加性修改**（视图可选面、宿主注入、Unregister 修正、纯注入型挂载），逐条见交接文档 §2.2/§6。
- ~~生成物只保证结构合法~~ → **已升级为「可反序列化 + csx 真编译 + hook 型真运行」**（inject 型差最后一格，见交接文档 §4）。
- 句式内建、词表外置；官方原语无法表达者只能走 `csx` 逃生舱；**op 层无法解析 ⇒ 原文保留 + 标记 `needsCsx`**（用户口径）。
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
- **端到端可运行测试已打通**（`tests/Orc.Game.Tests/EffectRuntimeEndToEndTests.cs`）：
  卡面文本 → 解析 → DSL → 生成带 csx 预制体 → `Engine.Prefabs.RegisterPrefab` + `CardEffectRegistry.DeclarePrefab`（专用卡 id）
  → 真实对局上场 → **csx 真执行** → **断言世界状态**（敌方轰炸机防御 **2 → 1**）。
- **打通途中又抓出一个真 bug**：归属过滤守卫原先读 `view.Card`，但 **`unit.*` 系信号的载荷键是 `Unit`**（`unit.joined`/`unit.deployed`/`unit.position.changed`）
  ⇒ 守卫恒假、带归属过滤的监听**永不触发**。修法：`CardEventView` 增 `[Optional][Read] Unit`（与 `Host` 同手法），守卫改读 `(view.Card ?? view.Unit)`。
- 全量 **1233 项测试通过**。

## E1-35：端到端验证续（多模板类别） {进行中}

| 模板类别 | 端到端结果 | 断言 |
|---|---|---|
| `join_basic` + `damage`（hook 型监听） | ✅ 通过 | 敌方防御 2 → 1 |
| `join_basic` + `addToHand`（**卡名 → 牌库 → 手牌**） | ✅ 通过 | 手牌数 +1 |
| `attack_basic`（**inject 型**监听 + `draw`） | ⏸ **未通过（已 Skip 保留用例）** | 攻击成功，但效果未落地（手牌数未变） |

### E1-36：inject 型监听修复途中挖出的三层问题（已修 3、余 1）

1. ✅ **求值器从未装配**（E1-34 已修）。
2. ✅ **`unit.*` 载荷的归属守卫恒假**：守卫读 `view.Card`，而 `unit.joined`/`unit.deployed`/`unit.position.changed` 的载荷键是 `Unit`
   ⇒ `CardEventView` 增 `[Optional][Read] Unit`，守卫改读 `(view.Card ?? view.Unit)`。
3. ✅ **inject 型主触发器误设为 `active`**：`EffectSystem.ApplyFrameworkInjects` 要求效果是 **`DynamicPassiveEffect`**，
   否则按"非动态被动形态"**直接隔离跳过注入**（`active` ⇒ 注入永远不生效）。`attack_basic` 已改 `passive`（**passive 允许无 hooks**——只有 `active + hooks` 被拒）。
4. ✅ **inject 处理器拿不到宿主**（任务 4 的 (b)）：注入进的是**共享**触发器（所有者＝CommandManager），
   内核"所有者是效果实例才注入 Host"的逻辑对它不生效 ⇒ `Effect.InjectByFramework` 改为**按视图类型包一层**：
   调用前把本效果的宿主写进视图的 `Host` 面（仅在视图声明了可写 `Host` 时包装；反射泛型适配 + 缓存）。
5. ✅ **视图类型解析失败**（用日志定位到的**真正根因**）：内核 `DynamicEffectBuilder.ResolveViewType` 用 `Type.GetType(名)`，
   对**非程序集限定名**只搜索"调用程序集 + 核心库"⇒ 跨程序集的 `Orc.Game.Commanding.UnitAttackTriggerView` 解析不了，
   报 `[view-type] 主触发器视图类型 '…' 无法解析`，**整个效果被隔离**。
   已修：模板改**程序集限定名**（`, Orc.Game`）＋ `EffectCompiler.WrapHandler` 剥掉程序集部分再写 csx（`Split(',')[0]`）。
6. ✅ **内核独立 bug 已修**：`TriggerReflection.Unregister` 的 `UnregisterMethod` 取自**开放式泛型** `typeof(Trigger<>)`
   ⇒ `Invoke` 必抛「Late bound operations cannot be performed … ContainsGenericParameters is true」（且即便封闭也属
   `Trigger<object>`、无法作用于 `Trigger<X>` 实例）。改为**按实例运行时类型**反射（闭型 + 按类型缓存）。
   凡"已注册注入 + 挂载失败 ⇒ 回滚"的组合都会踩到——本路径首次触发，属潜伏 bug。
7. ⏸ **剩余（已用探针缩到"处理器未被调用"）**：
   - **决定性探针**（csx 逃生舱 op 内写 `ctx.Engine.RootStream.WriteLog` 留痕）结论：**处理器根本没被调用**
     （不是"被调用但 `self` 为 null"）。
   - **已排除"区段不匹配"假设**：C# 侧 `Inject<TView>` 用 4 参 `target.Register(name, handler, band, priority)`（`name`＝处理器名、`band`＝区段），
     而 `Effect.InjectByFramework` → `TriggerReflection.RegisterDefaultBand` → 3 参 `Register(name, handler, priority)` ⇒ **注册就在默认区段**；
     `UnitAttackTrigger.Register("攻击执行", …)` 里的 `"攻击执行"` 只是**处理器名**，不是区段。故区段不是原因。
   - **装载期无任何错误**（且没有 `效果预制体装载` 失败行——按前次观察，失败会记该行）⇒ 挂载与注入登记**均成功**。
   - **8. ✅ 纯注入型效果挂不上总线（本轮定位并修）**：用"数注入项"探针实测
     `形态=DynamicPassiveEffect 挂载=False 注入数=0`（同时 `TryGetEventHandler('e1')=True`）⇒ 效果**从未挂载**；
     再捞日志得 `{效果名}/OnMount | Exception has been thrown by the target of an invocation.`（反射 `Invoke` 内层异常）
     ⇒ `DynamicPassiveEffect.MountMainTrigger` 对"主触发器**无 hooks**"仍调 `TriggerReflection.Mount` → 抛 → 装载失败
     → `RollbackMount()` **连注入一起撤销**。已修：无 hooks 时视为"注入载体"**不挂总线**（注入由装载链另行施加）。
   - **9. ✅ 修正 8 已验证有效**（探针复测）：`挂载=False 注入数=0` → **`挂载=True 注入数=1`**，且装载期无 OnMount 错误。
     即 **inject 型效果现在真的能挂载 + 登记注入**（此前 100% 不可能）。
   - **10. ✅ 宿主改为"调用时解析"**：`Effect.InjectByFramework` 的宿主包装不再取装载期快照，而是传本效果、在**调用时**读
     `HostOrNull`（消除"注入早于宿主绑定"的时机依赖）。
   - **11. ⏸ 剩余单点（范围已收窄到"实例寻址"）**：注入**已登记**（`注入数=1`、无隔离日志），但攻击链**未调用**。
     本轮已用实验逐项排除：
     - **宿主包装非元凶**（停用后 TIE 照旧）；
     - **区段**：`Inject<TView>` 用 4 参 `Register(名, handler, 区段, 优先级)`，`InjectByFramework` 用 3 参 ⇒ **同在默认区段**；
     - **优先级**：把注入优先级提到 `-100`（提前于默认链）仍不被调用；
     - **目标解析**：`注入数=1` 且无隔离日志 ⇒ 目标解析并注册成功；
     - **视图类型约束**（对照实验反证）：故意用 `CardEventView` 去注入卡自带 `加入触发器` 会立刻
       `注入失败（隔离）：Exception has been thrown by the target of an invocation`（= `RegisterBand` 里的强制转换失败）
       ⇒ 注入的处理器的视图类型**必须**与目标触发器一致；`attack_basic` 两者同型，故注册成功。
     - **12. ✅ 实例假设被推翻**（实例对照实验）：`CommandManager.DispatchAttackAsync` 留痕 `UnitAttackTrigger` 实例标识
       vs `attacker.Effects[0].Injections[0].Target` 实例标识 —— **完全相同**（`Trigger\`1#41153686`）。
     - **13. ⚠️ 两条旧结论被作废（实验设计缺陷）**：
       - "优先级无效"——把注入优先级设为 `-100` 会让**注入直接失败**（复测 `注入目标=无注入`）⇒ 那轮实验其实**根本没注入**，结论无效；已回退优先级为 0（注入恢复）。
       - "包装非元凶"——首次对照是在**卡自带触发器探针**上做的（那里是**视图类型不匹配**导致的 TIE），与攻击路径无关；
         后在**攻击路径**上重做对照（停用包装）：**仍不被调用** ⇒ 本轮才真正排除包装。
     - **结论更新**：实例/区段/优先级/解析/包装 全部排除 ⇒ 只剩
       **(a) 处理器被调用但内部抛异常**（`Trigger.InvokeAsync` 对"事件内业务异常"是**隔离记录并继续**——正好表现为"像没被调用"）；
       或 **(b) 默认区段注册项在实际执行时被过滤**。
     **下一步探针（一句话可执行）**：查 `RootStream` 中 **`keywords` 含 `exception:`** 的记录与 Error 级条目——
     若出现即 (a) 确诊，并直接给出异常类型与 message。
   ——用例以 `Skip` 保留并写明，**不掩盖缺口**。

- **`attack_basic` 失败的根因（高度可能，待最后一步取证）**：inject 型处理器的**宿主不在视图里**——
  处理器被注册进 `CommandManager` 的**共享**触发器（`单位攻击触发器`），该触发器的所有者是 CommandManager、**不是效果实例**，
  故内核那段"所有者是带宿主的效果时把宿主注入视图数据"的逻辑**不生效** ⇒ `view.Host` 为 null ⇒ `var self = view.Host as Card;` 为 null ⇒ op 全部 no-op。
- **修法（二选一，待定）**：
  - **(a) 模板侧**：`ActorFrom` 支持**原始表达式**（如 `"actorExpression": "view.Attacker as Card"`），inject 型模板改用视图自带字段取施动卡；
  - **(b) 框架侧**：inject 注册时把"所属效果"带给共享触发器（使其也能注入 `Host`）——改动更深，但让所有 inject 型模板统一可用。
- 待办：取证（确认 `view.Host` 为 null）→ 择一实施 → 复验。

## E1-37：inject 型监听缺口**根因确诊并修复**（E1-36 收尾，**Done**）

> 取证方式：把已 `Skip` 的 `Generated_Attack_Listener_Runs_After_Attack` 临时启用 + dump `RootStream.Entries`，跑完即回退该测试。

- **决定性证据**：
  ```
  [Error] 单位攻击触发器/e1 | Exception has been thrown by the target of an invocation. | kw=exception:TargetInvocationException
  ```
  ⇒ **处理器确实被调用了**，且在**调用内抛异常**；`Trigger<TView>.RunEventsAsync` 对"事件内业务异常"**隔离记录并继续**
  ⇒ 攻击命令仍 `Success`、外部表现**恰好是"像没被调用"**。
  交接文档 §4.3 的 **(a) 支确诊**；**(b) 支（区段被过滤）可排除**。此前"处理器未被调用"的探针结论**被推翻**（该探针本身有缺陷）。
- **异常来源（代码取证）**：`Effect.InjectByFramework` 的宿主包装 `WrapWithHostCore<TView>`
  → `HostProperty<TView>.Set(view, host)` → `PropertyInfo.SetValue(view, host)`；
  而 `view` 的实际运行时类型是 `ViewProxyFactory` 生成的**代理**，代理把访问器烘焙成**权限闸门**
  （`EmitSetterBody`：`Access != Mutate` 一律 `throw PermissionDeniedException`），
  而 `Host` 声明为 **`[Optional][Read]`** ⇒ **setter 必抛** ⇒ 经反射包成 `TargetInvocationException`。
- **为何 hook 型不受影响**：hook 型监听的宿主是**数据面注入**——`Trigger<TView>.InvokeAsync` 在 `new Context(...)` **之前**
  把宿主写进**数据字典**（`enriched["Host"] = hostCard`），视图 getter 从 `__orc_data["Host"]` 读，**从不走 setter**。
- **修法（已实施＝数据面注入，I27 甲）**：`WrapWithHostCore` 改为 `ctx.Data["Host"] = effect.HostOrNull;` 再调 `inner`，
  并**删掉** `HostProperty<TView>` 反射缓存；`DeclaresHostProperty` 的判据由 `CanWrite` 改为 `CanRead`
  （数据面注入只需"视图声明了可读 `Host` 面"）。
  - `Context.Data` 为 `Orc` 内 `internal`，`Effect.cs` 同程序集可直接访问；`Context` 构造**已拷贝**调用方字典、
    并把**同一引用**交给代理 ⇒ 与内核 hook 路径同手法、不污染调用方载荷。
- **验收**：`EffectRuntimeEndToEndMoreTests.Generated_Attack_Listener_Runs_After_Attack` **取消 `Skip` 后通过**
  （攻击链上挂的 csx 真执行 ⇒ 手牌 +1）；**全量 1243 项全绿、0 跳过**（此前 1233 通过 + 1 显式 Skip）。

## E1-38：R8 静默丢弃修复 ＋「监听型」构成重估（**Done**）

> 用户口径：`①收口 ＋ ②补信号`；grill 后裁决＝**丙**（①收口 ＋ 按可落地性重定 ② ＋ 一并修 R8 违规）＋**甲**（过滤守卫能力"部分引入"）。

### 落地前的事实核对（结果与交接文档 §5/§8 的结论**冲突**）

1. **②的三个候选信号里只有 1 个能落地**（见 grill plan **I28**）：
   - `unit.combat.survived`：**可补**（交战结算 `HandleDefaultAttackDamageAsync` 已存在）；
   - `unit.impact`：**不可补**——「冲击」词条**未实现**（`KeywordRegistry` 注释列为"不在本批实现范围"，
     全仓仅 `GameHooks.KardsImpactUsed`（`NotPlanned`）留痕）⇒ **没有发射点**；
   - `card.converted`：**不可补**——「转换」机制**未实现**（只有底层离场原语 `LeaveBattlefieldAsync`，无生产调用方）。
2. **「监听型 142」的真实构成**（临时探针按原文分组，见 grill plan **I29**）：**多数是"信号已存在、句式/词表没覆盖"**
   （`使用…牌`类 → `card.played`；`抽 N 张牌`类 → `card.drawn`；`额外获得/失去 N 个指挥点槽` → `slot.gained`/`slot.lost`），
   而非交接文档 §5 结论所称的"主要靠补游戏层信号"；
   其中还暴露一条**句式层缺陷**：`抽 1 张牌` 归一后为 `抽1张牌`，用 `Contains("抽牌")` 判据**恒不命中**。
3. **新发现真实缺陷（R8 违规）**：语料 1553 条中 **295 条**卡面 `Effects.Count == 0 && Unresolved.Count == 0`
   —— **既无效果、也无诊断＝静默丢弃**（见 grill plan **I30/I31**）。
   成因：`SemanticMapper.TryBuildOps` 的 `ops.Count == 0 ⇒ ok = false` 没有配套的失败记录
   （纯目标声明/纯条件子句在 `clause.Actions.Count == 0` 分支里 `continue`、不计失败）⇒ 调用方 `AddRange` 加了 0 条。
   影响：语料"产出 0 效果 559 条"里有 295 条**并非无效果文本**，覆盖率与"缺口可审计"承诺均**失真**。

### 实施（I31 乙：诊断口径按"整效果"粒度补，不改"子句颗粒度不计失败"的既定口径）

- `SemanticMapper.TryBuildOps`：`ops.Count == 0 && failures.Count == 0` 时**补一条诊断**——
  整条均为纯目标声明 ⇒ "未产出任何可执行效果（整条均为目标声明/回指，无动作短语）。"；
  其余 ⇒ "未产出任何可执行效果（整条均为条件/无动作短语）。"（定位用 `FindTargetDeclaration`）。
- 对拍报告新增一列 **「既无效果、也无诊断（R8 违规面）」**：**295 → 0**（该项即为本修复的守门观测面；
  不设硬门槛，因"整条仅词条行"的卡面合法地为空）。

### 对拍刷新

| 指标 | 修前 | 修后 |
|---|---|---|
| 产出 ≥1 效果 | 994 | **1006** |
| 覆盖率 | 64.0% | **64.8%** |
| 效果总数 | 1149 | 1161 |
| 未解析记录 | 297 | 760（**口径纠正**：295 条静默丢弃转为可见诊断） |
| 既无效果也无诊断 | 295 | **0** |
| 监听型触发不在子集 | 142 | **125** |

## E1-39：`unit.combat.survived` 新信号 ＋ 过滤守卫玩家面 ＋ 句式修正（**Done**）

### 1. 新信号 `unit.combat.survived`（照 E1-25 槽信号的四处同步模式）

- **语义**（用户裁决＝甲）：一次单位对战结算结束后，**参战且未阵亡**的单位**各发射一次**；载荷 `{ Unit }`；
  观察序＝**被攻击者在前、攻击者在后**（与同归于尽的 `card.died` 观察序一致）；**反击豁免只影响"是否互伤"、不影响"是否参战"**
  ⇒ 攻击者**恒发射**（存活时），不因 `counterAttacks=false` 而缺席。
- **伏击改写路径**（用户裁决＝"发"）：攻击者死亡、被攻击者不受伤 ⇒ 对**被攻击者**发射。
- **发射点**：`CommandManager.HandleDefaultAttackDamageAsync`——**死亡判定之后**（已阵亡者不发射，不误导"幸存"）。
- **四处同步**：`GameUpdates`（常量 ＋ `EmitUnitCombatSurvived`）→ `GameHooks`（转发常量 ＋ `Signals` **26 → 27**）→
  `GameHooksJson`（`SignalMetadata` 条目）→ `GameHooksTests`（`signals` 26→27、JSON `signals` 26→27、`helpers` 21→22、发射点映射 +1）。
- **模板与映射**：`combat_survived_basic.tpl.json`（hook `unit.combat.survived`、`actorFrom: effectHost`、槽位 `on_event`）；
  `ResolveListenTemplate` 增 `存活` 分支（置于"攻击/行动"**之前**——更具体者优先），并加入卡面归属过滤白名单。

### 2. 过滤守卫的**玩家面**（用户裁决＝甲：部分引入）

- **问题**：`slot.gained`/`slot.lost` 载荷为 `{ Player, Amount }`、**没有事件卡**，既有守卫（读 `view.Card ?? view.Unit`）不适用。
- **交付**：
  - `CardEventView` 增 `[Optional][Read] object? Player`（与 `Unit`/`Host` 同手法）；
  - `DslCondition` 增两类：`owner.same.player` / `owner.different.player`；
  - `EffectCompiler` 渲染玩家面守卫：`view.Player is Orc.Game.Players.Player actorPlayer && self is CardBase actorSelf && …`；
  - `SemanticMapper`：`OwnerFilterableTemplates`（卡面）＋ 新增 `PlayerFilterableTemplates`（玩家面），
    由 `OwnerGuardKind(template, side)` 二选一（未列模板/无阵营词＝不加守卫）；
  - 新模板 `slot_gained_basic.tpl.json` / `slot_lost_basic.tpl.json`（hook `slot.gained` / `slot.lost`）。

### 3. 句式修正（I29 暴露）

- `ResolveListenTemplate` 的 `Contains("抽牌")` → `Contains("抽") && Contains("牌")`
  （归一后 `抽 1 张牌` 为 `抽1张牌`，数字夹在中间 ⇒ 原判据**恒不命中**，语料 3 条 `额外抽 1 张牌时` 因此落空）。
- `指挥点槽` 监听分支：`失去` ⇒ `slot_lost_basic`，否则 `slot_gained_basic`。

### 4. 新增测试

- `EffectParserTests`：`Parse_Maps_Combat_Survived_And_Slot_Listeners_With_Owner_Guard`、
  `Parse_Maps_Drawn_Listener_With_Number_Between`、`Parse_Reports_Unresolved_For_Target_Declaration_Only_Effect`、
  `Parse_Listener_Player_Face_Condition_Renders_Player_Guard_In_Csx`；`Generated_Csx_Compiles` 语料 **+6**。
- `CommandCombatTests`：`Combat_Survived_Emits_For_Living_Participants_Target_First`（双方存活 ＝ 2 条、序）／
  `Combat_Survived_Skips_Dead_Participant`（同归于尽 ＝ 0 条）／
  `Combat_Survived_Fires_For_Unharmed_Target_On_Ambush_Rewrite`（伏击 ＝ 1 条、被攻击者）。
- `EffectRuntimeEndToEndMoreTests`：`Generated_Combat_Survived_Listener_Runs_After_Combat`
  （卡面"友方单位交战并存活后，抽一张牌。" → 新信号 → 归属守卫放行 → csx 真执行 ⇒ 手牌 +1）。
- `EffectParseCorpusTests`：报告新增「既无效果、也无诊断」列与样例段。

### 5. 受控变更清单（对既有测试期望值的改动，逐条申报）

| 文件 | 变更 | 理由 |
|---|---|---|
| `EffectParsingTests` | 模板数 23 → **26** | 新增 3 个监听骨架 |
| `GameHooksTests` | `signals` 26→27、JSON `signals` 26→27、`helpers` 21→22、映射表 +1 | 新信号四处同步 |
| `CommandCombatTests` ×2 | 精确信号流纳入 `unit.combat.survived`（存活 1 条 / 伏击 1 条） | 新信号 |
| `CommandUpdateTests` ×2 | 同上（无死亡 2 条 / 被攻击者死亡 1 条） | 新信号 |
| `StatPortalTests` ×1 | 零伤害链用例纳入 2 条 `unit.combat.survived`（参战即发、与伤害无关） | 新信号 |
| `Orc/Cards/DynamicEffect.cs` | `Unregister` 反射缓存加 `!`（消除 CS8603 警告） | 0 警告口径 |

### 6. 本轮**未做**（明确登记，避免误以为已覆盖）

1. **`使用X牌`／兵种过滤族（≈22 条）**：需要"**事件卡属性过滤**"新能力
   （`DslCondition` 新 kind ＋ 编译器真 csx 守卫 ＋ `filters.json` 词表复用）——见 grill plan **I32/I34**（用户裁决＝甲：本轮不做）。
2. **「本单位…时」的自指不过滤**（本轮新发现，见 grill plan **I35**）：监听模板对 `本单位X时` 与 `友方单位X时` 一视同仁，
   均只做**归属**过滤、**不校验"事件主体是不是宿主卡本身"** ⇒ `本单位交战并存活后` 会在**任意**单位交战时触发。
   既有 `death_basic`/`damaged_basic` 等同族同病（死亡链因"效果先卸载"而自愈，伤害类不会）。
3. **机制不存在的监听**（老兵／冲击／转换／修复／反制触发／隐蔽揭示／撤退／收缴／预报／回合计数类）：无发射点，维持显式失败。

## E1-41：杠杆 1——「属性/状态类动作路径」（`具有` ＋ 自指选靶修复 ＋ 期限 ＋ 多宾语）（**Done**）

> 用户裁决：口径＝**甲**（结构可解析计入 ＋ 报告分层披露）；I40/I41＝**甲**；I42（事件卡过滤维度）＝**乙**；
> 执行顺序 **1 → 3 → 2**（本轮完成 **1**）。

### 1. I40 **既有缺陷修复**——自指（无目标）效果**选中了别人**

- **缺陷（代码取证）**：`BuildSelector` 对"无目标/无限定词"返回 null → `OpTemplateCatalog` 把 null 选择器的
  `{{sel}}` 渲染为 `"one"` → `EffectTargetResolveJudicator` **无 `self` 分支**、且 `side == null` 时 `MatchesSide` 恒真、
  `SelectLines` 顺序＝A 支援线→B 支援线→前线 ⇒ `部署：获得闪击。` 实际作用于"**A 方支援线第一个单位**"。
- **修法**：① 判定器增 `sel == "self"` ⇒ 直接返回视角卡；② `{{sel}}` 缺省由 `"one"` 改 **`"self"`**；
  ③ `BuildSelector` 增"**动词之前的过滤短语才是目标限定词**"（`QualifierFilters`/`ObjectFilters`）——
  否则 `获得闪击` 会被当作"选一个**带闪击**的单位"（同一族的第二个错误）。

### 2. `具有`（新动作键 `state`）——属性/状态描述 → `buff`/`costMod`/`grant`

- `actions.json` 增 `"具有": {"key":"state"}`；`TryMapStateAction` 以**动词之后的过滤短语**为宾语：
  属性（`+N 攻击力/防御力`）→ `buff`；对象（`N 行动花费/花费`）→ `costMod`；词条 → `grant`；
  **单子句多宾语**（`+1 攻击力和闪击`）⇒ 展开为**多个 op**；`+1+1` 两数形态 → `buff(attack, defense)`。
- **保守守卫**（`HasUnmappableStateModifier`）：整效果文本含**计数/对抗/相位/排除自身**语式
  （`每有`/`每拥有`/`每回合`/`对抗`/`回合中`/`交战`/`其他`/`其它`）时**不做映射**，
  改留痕 `needsCsx`——映射成"单次无条件"即语义错误（比"未解析"更糟）。

### 3. 期限（`until`）——持续态**不再退化为永久增益**

- DSL 增 `DslOp.Until`（`Untils.TurnEnd`／`Untils.NextOwnerTurnStart`）＋ JSON 往返 ＋ 注册表校验
  （**只有 `buff`/`costMod` 可带期限**；`grant` 等无期限面若带期限 ⇒ 该子句改 `needsCsx`）。
- `EffectRuntime`：`BuffAsync`/`CostModAsync` 增 `EffectDuration` 形参；`ExpiryFor` 把期限映射为既有
  `ModifierExpiry`（`TurnEnd` ⇒ `turn.end` 相位；`NextOwnerTurnStart` ⇒ `turn.start` 相位 ＋ **载荷玩家＝目标归属玩家**过滤）。
- 期限语式识别：`直到回合结束`／`直到本回合结束`／**`本回合`**（连接词新增，前缀不再成为孤立子句）⇒ `turnEnd`；
  `直到下个友方回合开始`／`直到你的下个回合开始` ⇒ `nextOwnerTurnStart`。
- `buff.csx.tpl`／`costMod.csx.tpl` 传 `{{until}}`（渲染为 `Orc.Game.Effects.EffectDuration.*`）。

### 4. 途中抓出的 3 个**既有词法/语法缺陷**（均已修）

| 缺陷 | 事实 | 影响 |
|---|---|---|
| **符号丢失** | `-1` → `Unknown("-")` ＋ `Num(1)` ⇒ 负值被读成**正值** | `具有 -1 行动花费` 会变成 `+1`（静默反向） |
| **数量短语被当载荷** | `使 1 个友方步兵具有 +2 攻击力` ⇒ 载荷取到 `1`（阿拉伯数字成 `Num`，而中文数字 `一个` 成 `Quant`） | `+2` 被读成 `+1` |
| **`amount` 不能为负** | `DslOpRegistry` 对所有 op 一律拒绝负 `amount` | `costMod -1` 无法表达（`-1 行动花费` 属常态） |

修法：`Tokenizer` 把 `+`/`-`/`＋`/`－`/`−` 并入紧随的阿拉伯数字（**一个** Num、值带符号）；
`AstBuilder` 增 `[数字][量词(measure)]` **且本子句尚未出现动作词** ⇒ 判为**数量短语**（不是载荷），
例外＝紧跟**引号卡名**（`将 3 张“X”加入手中` 的 `3` 仍是数量载荷）；
`DslOpRegistry` 的"amount 非负"只对**数值语义原语**生效（`costMod` 是修饰增量，允许负）。

### 5. 对拍刷新（本轮起点 → 终点）

| 指标 | 起点 | 终点 |
|---|---|---|
| **覆盖率（结构口径＝甲）** | 64.8% | **72.4%**（+7.6pp / +119 张） |
| 覆盖率（语义完整口径＝乙） | —（本轮首次测量） | **49.6%**（771 张） |
| 效果总数 | 1161 | 1298 |
| 未解析记录 | 760 | 623 |
| 既无效果也无诊断（R8） | 0 | 0 |
| 分层：语义完整 / 含占位条件 / 含 needsCsx | — | **771 / 77 / 283** |

> **关于"语义完整 793 → 771"**：中途快照曾为 793，最终 771。差异来自**期限不可承载的 `grant`** 与**守卫命中**的用例
> 由"**错误但计入**"（如 `具有闪击，直到回合结束` 曾渲染为**永久**闪击）改为 `needsCsx` ⇒
> 数字下降是**诚实度上升**（那些卡原本就是"部分效果静默丢失"的状态）。

### 6. 新增测试

- `EffectParserTests`：`Parse_Maps_State_Verb_To_Buff_Grant_And_CostMod`／
  `Parse_State_Verb_Applies_Duration_Only_To_Capable_Ops`／`Parse_Reports_NeedsCsx_For_Unmappable_State`／
  `Parse_Keeps_Signed_Value_And_Count_Phrase`；`Generated_Csx_Compiles` 语料 **+9**（含期限/自指/多宾语/负值）。
- `EffectDurationTests`（新）：`TurnEnd` 到期自注销／无期限跨回合存活／`NextOwnerTurnStart` 按**归属玩家**到期。
- `EffectParseCorpusTests`：报告增**双口径覆盖率** ＋ **四层披露**（语义完整／占位条件／needsCsx／未解析）。

### 7. 本轮**未做**（按裁决顺序，下一步＝杠杆 3）

1. **杠杆 3**：`使用X牌` 等**事件卡属性过滤**（用户裁决＝乙：先只支持**词条**维度 `Keywords.Has`）——**下一轮**。
2. **杠杆 2**：`aura` op（对接 `AuraRegistry`，让持续态真正落地并可表达 `相邻/阵营` 谓词）。
3. **`其他/其它`（排除自身）选择器维度**：本轮以 `needsCsx` 守卫代替（避免多包含宿主）；补全可回收该族。
4. 机制缺失族（老兵/冲击/转换/修复/…）与条件求值面（`raw` 占位）：仍为显式失败／占位。

## E1-42：杠杆 3——事件卡属性过滤（**词条维度**，用户裁决＝乙）（**Done**）

### 1. 交付

- **DSL**：`DslCondition` 增两类——
  - `eventCard.keyword`（**事件卡具有某词条**；`Raw` 承载词条**标识**，如 `情报`）；
  - `all`（**合取**：`All` 承载子条件，渲染为 `(a && b)`）——用于"归属过滤 ＋ 事件卡过滤"并存。
  JSON 往返同步（`ConditionDto.All` 递归）。
- **编译器**：真实 csx 守卫
  ```
  (view.Card as Orc.Game.Cards.CardBase) is { } actorEventCard && actorEventCard.Keywords.Has("情报")
  ```
  合取渲染为 `(…)` 括起的 `&&` 链（新增 `CsStringLiteral` 转义）。
- **映射**：`ResolveListenTemplate` 增 `out string? eventCardKeyword`；`FindEventCardKeyword` 识别
  `使用/打出 …<词条>牌`（词表 `EventCardKeywordLexicon`，当前＝`情报 → KeywordIds.Intelligence`）；
  `CombineGuards` 把归属守卫与事件卡守卫合成合取。
  模板复用既有 `played_basic`（`card.played`，载荷 `{Card, Player}` ⇒ `view.Card`）。

### 2. **乙口径的边界（有意留白，不做宽松映射）**

| 卡面 | 需要的维度 | 本轮行为 |
|---|---|---|
| `友方使用情报牌时` | **词条**（`Keywords.Has`） | ✅ 映射（`owner.same` ＋ `eventCard.keyword:情报`） |
| `使用海军牌时` | 子类别 **tag**（`TagData.ContainsTag`） | ❌ 仍显式失败（**不**降级为"打出任意牌"） |
| `敌方指令使用时`／`友方使用英国指令时` | **卡类型**（`CardDefinition.Category`）／**阵营** | ❌ 仍显式失败 |
| `友方使用“计划”时`／`敌方抽取“抵抗”时` | **卡名**（引号已是 `Filter{name}`） | ❌ 仍显式失败 |
| `敌方使用花费不小于 4 的指令时` | **花费阈值** | ❌ 仍显式失败 |

> 未支持维度一律**保持显式失败**（宁可未解析，也不产出"友方打出任意牌都触发"这类**语义错误**）。

### 3. 对拍刷新

| 指标 | 本轮起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 72.4% | **72.7%** |
| 覆盖率（语义完整口径） | 49.6% | **49.9%** |
| 未解析记录 | 623 | 619 |

**预期收益偏小（＋4 张）的根因（实测，见 grill plan I45）**：`监听型 125` 的构成里
**"事件卡属性过滤"只占 ≈11 条**，且其中**只有 `情报` 落在词条维度**；
其余 11 条分别落在 tag／卡类型／阵营／卡名／花费阈值（＝I42 **甲**）。

### 4. 新增测试

- `EffectParserTests`：`Parse_Maps_EventCard_Keyword_Filter_With_Owner_Conjunction`（含"乙口径边界＝未支持维度仍失败"）、
  `Parse_EventCard_Keyword_Guard_Renders_Real_Csx`；`Generated_Csx_Compiles` 语料 **+3**。
- `EffectRuntimeEndToEndMoreTests`：`Generated_EventCard_Keyword_Listener_Fires_Only_For_Matching_Played_Card`
  ——真对局里 `card.played` 带词条 ⇒ 抽 1 张；**不带词条 ⇒ 零效果**（守卫真生效）。

## E1-47：归属类信号——`unit.damage.dealt` ＋ `card.died.Killer`（**Done**）

> 依据 grill plan **I46** 的实测：`监听型` 余量的大头是"**归属类**"（伤害来源／击杀者／指向／反制）。

### 1. `unit.damage.dealt`（**来源侧**伤害信号）

- **载荷**：`{ Unit＝施动方, Card＝受方（单位或 HQ）, Amount＝实际伤害量 }`；**实际变化量 > 0 才发**（改变才传播）。
- **发射点**：①单位互伤（`HandleDefaultAttackDamageAsync`：攻击者→被攻击者、被攻击者→攻击者**各一条**）
  ②HQ 简路（`HandleUnitAttackAsync`：伤害落定后按 `Health` 差值）。**先落定、后发射**。

### 2. `card.died` 加性携带 **`Killer`**（死亡＋归属，不新增信号）

- 载荷键 `PayloadKiller`（`GameHooks.PayloadKeys` **15→16**）；`CardEventView` 增 `[Optional][Read] Killer` 面。
- `ProcessDeathAsync(unit, ct, killer = null)`；`EmitCardDied(engine, card, killer?, ct)`。
- **⚠️ 关键事实（本轮踩到）**：**"防御归零统一死亡衔接"在伤害门户内先行处理**
  （`ApplyDefenseDamageAsync` → 跑链 → `card.stat.changed` → 总线 → `HandleDefenseDepletionAsync` → `ProcessDeathAsync`），
  ⇒ 战斗路径里那句"结算路径内防护兜底"的 `ProcessDeathAsync(target, killer: attacker)` **通常已被抢占、根本不会执行**
  （实测 `Killer` 为 null）。
- **修法＝伤害来源游标**（`CommandManager._damageSourceCursor`）：伤害门户与死亡衔接**同一同步调用链**，
  故以**结算窗口内**的临时游标承载"本次伤害的施动方"（`WithDamageSourceAsync` 用 try/finally **恢复式**赋值，
  嵌套伤害作用域亦正确）；窗口外（修饰到期等）＝null ⇒ 无击杀者归属（符合语义）。
  死亡衔接与战斗路径两处**都**把游标/显式参数作为 `killer`。

### 3. DSL：**载荷字段守卫**（自指／受方）

- `DslCondition` 增 `payload.self`（`Raw`＝视图属性名 ⇒ `object.ReferenceEquals(view.X, self)`）
  与 `payload.hq`（⇒ `view.X is Hq`）；`EffectCompiler` 以**属性名白名单**收敛（`Card/Unit/Killer/Player/Host/Effect`）。
- `SemanticMapper`：`CombineGuards(params)` 支持多个守卫合取；`PayloadGuardOf` 产出守卫；
  `NeedsPayloadSubject`＋`ContainsSelfSubject` ⇒ **`damage_dealt_basic`／`killed_basic` 必须有"本单位"主体**，
  否则**不映射**（半解析会"任意单位造成伤害/消灭都触发"——**泛触发属语义错误**）。
- **注意（有意留白）**：`友方单位造成 1 点对战伤害时`／`友方单位消灭 1 个敌方单位时`／`目标单位造成伤害时`
  仍需"施动方/击杀者的**归属面**"（`Killer.Owner` vs `self.Owner`）——本轮**未做**，保持显式失败。

### 4. 模板与映射（模板库 **26 → 28**）

| 模板 | hook | 映射触发 |
|---|---|---|
| `damage_dealt_basic` | `unit.damage.dealt` | 短语含 `造成`＋`伤害` 且主体为"本单位"；含 `总部` ⇒ 加 `payload.hq:Card` |
| `killed_basic` | `card.died` | 短语含 `消灭` 且主体为"本单位"（`被消灭` 仍走 `death_basic`） |

### 5. 对拍刷新

| 指标 | 起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 72.7% | **73.7%**（＋15 张） |
| 覆盖率（语义完整口径） | 49.9% | **50.5%** |
| 未解析记录 | 619 | 604 |

### 6. 新增测试

- `EffectParserTests`：`Parse_Maps_Damage_Dealt_And_Killed_With_Payload_Self_Guard`（含"无本单位 ⇒ 不映射"）；
  `Generated_Csx_Compiles` 语料 **+3**。
- `CommandCombatTests`：`Damage_Dealt_Carries_Dealer_Target_And_Amount`（双向、实际变化量）／
  `CardDied_Carries_Killer_On_Combat_Kill_And_Null_Without_Attribution`（对战致死 vs 直接伤害致死）。
- `EffectRuntimeEndToEndMoreTests`：`Generated_Damage_Dealt_Listener_Draws_Only_For_Own_Damage`
  （自指守卫：只有**自己**造成伤害方向触发 ⇒ 手牌恰好 +1）。

### 7. 本轮**未做**（登记）

1. **效应伤害/消灭的归属**：`EffectRuntime.DamageAsync`／`KillAsync` 未带来源 ⇒ 效果伤害/消灭不产生归属
   （需给门面加 `source` 形参并让其走同一游标）。
2. `unit.targeted`（被指向）：**无干净发射点**（指向发生在选靶/打牌链的多个入口，尚无统一受控面）——需先定"指向"的机制承载。
3. `counter.triggered`（反制触发）：发射点明确（`CounterCard.HandleUseCounterAsync`），但需新模板与映射，**下一轮**。
4. 施动方/击杀者的**归属面**（`友方单位造成…`／`友方单位消灭…`）与 `对抗…时` 条件。

## E1-50：归属补全——**主体归属面守卫** ＋ **效应侧归属**（**Done**）

> 用户口径：`添加归属`（承接 E1-47 §7 的第 1、2 项）。

### 1. DSL：载荷字段的**归属面**守卫（`payload.owner.same` / `payload.owner.different`）

- `Raw` ＝ 视图属性名；渲染为
  `(view.X as Orc.Game.Cards.CardBase)?.Owner is { } actorEventOwner && self is CardBase actorSelf
   && actorSelf.Owner is not null && object.ReferenceEquals(actorSelf.Owner, actorEventOwner)`（异主取反）。
- **主体判定顺序（关键）**：先看 **`本单位`**（自指 ⇒ `payload.self`），再看 **`友方`/`敌方`**（归属面）。
  原因：`本单位对敌方总部造成伤害时` 的"**敌方**"是**宾语**——若按"任意位置出现敌方"判归属，
  会得到"同主 ∧ 异主"的**矛盾守卫（恒假）**。
- `NeedsPayloadSubject` 的判据随之改为「`PayloadGuardOf(...) is null`」⇒
  **主体不可辨识（既非本单位、也非友方/敌方单位）⇒ 不映射**（如 `目标单位造成伤害时`），维持显式失败。

### 2. **效应侧归属**（效果伤害/消灭也带来源）

- `EffectRuntime.DamageAsync(target, amount, source, ct)`／`KillAsync(target, source, ct)`——新增 `source` 形参；
  模板 `damage.csx.tpl`／`destroy.csx.tpl` 传 `self`。
- 有来源时改走指挥管理器的受控面：`DealDamageAsync`（**伤害来源游标** ＋ `unit.damage.dealt`）／
  `DealDamageToHqAsync`（HQ 路径）／`KillUnitAsync(unit, killer, ct)`（死亡归属）。
- ⇒ `部署：对一个敌方单位造成 2 点伤害。` 这类**效果伤害**现在也产生 `unit.damage.dealt`（Unit＝宿主），
  致死时 `card.died.Killer` ＝宿主（此前只有**战斗**伤害/击杀才有归属）。

### 3. 对拍刷新

| 指标 | 起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 73.7% | **73.9%** |
| 覆盖率（语义完整口径） | 50.5% | **50.7%** |
| 未解析记录 | 604 | 601 |

### 4. 新增测试

- `EffectParserTests`：`Parse_Payload_Owner_Guard_Renders_Real_Csx`；`Parse_Maps_Damage_Dealt_And_Killed_With_Payload_Self_Guard`
  扩为"本单位／友方单位／敌方单位"三态 ＋ "主体不可辨识 ⇒ 不映射"；csx 编译语料 **+2**。
- `CommandCombatTests`：`Effect_Damage_With_Source_Emits_Dealt_And_Killer`（效应侧归属：dealt ＋ 致死 Killer）。
- `EffectRuntimeEndToEndMoreTests`：`Generated_Friendly_Damage_Dealt_Listener_Ignores_Enemy_Damage`
  （友方面命中、敌方反击方向被挡 ⇒ 手牌恰好 +1）。

### 5. 仍未做（登记）

1. **`counter.triggered`**（≈3，发射点明确）——下一轮最易项。
2. **`unit.targeted`（指向）**：无干净发射点，需先定机制承载。
3. `对抗…时`／计数条件（条件求值面）、机制缺失族（老兵/冲击/转换/修复…）、事件卡过滤其余维度。

## E1-52：**正确性修正**——`state` 路径的"静默丢弃限定词"（**Done**）

> 起因：E1-51 的验收探针把 `state`（`具有`）族的映射逐条打出来后，发现**一批"语法通过、语义错误"**的产出。

### 1. 实测到的错误映射（修正前）

| 卡面 | 修正前产出 | 错在哪 |
|---|---|---|
| `相邻陆军具有 +2 攻击力。` | `buff sel=self atk=2` | 加到了**宿主自己**（应为相邻单位） |
| `本单位左侧所有单位具有 -1 行动花费。` | `costMod sel=all amt=-1` | 作用**全场** |
| `防御力为 1 的友方步兵具有 +2 攻击力。` | `buff sel=one side=friendly atk=1` | 数值取到**阈值 1**（应为 +2）且阈值过滤丢失 |
| `敌方指令具有 +2 花费。` | `costMod sel=one side=enemy amt=2` | 目标应是"敌方**指令**（手势卡）" |
| `友方谢尔曼具有 -1 花费和闪击。` | `costMod + grant`（友方任一） | `谢尔曼` 子类别过滤丢失 |
| `构筑之外的友方单位具有 -2 花费。` | `costMod`（友方任一） | 构筑限定丢失 |
| `所有友方受伤单位具有 +2 攻击力。` | `buff all friendly` | 受伤态限定丢失 |
| `本回合，手牌中的所有单位具有 -1 花费。` | `costMod all until=turnEnd` | 手牌限定丢失（且**选靶面本就不传 zone**） |

**根因**：`AstBuilder` 的未知词只在 `actions.Count > 0` 时进 `objectParts`——**动词之前的未知限定词被整段丢弃**，
`TargetPhrase` 里没有任何记录 ⇒ 语义层"看不见"限定词，只能按已识别的部分（side/量词/兵种）凑一个目标。

### 2. 修法（三处）

1. **`TargetPhrase.HasUnrecognizedQualifier`（新）**：`AstBuilder` 在**动词之前**遇到未知词且其**不是纯句法虚词**时置位；
   `IsSyntacticParticle` 只收虚词（`使/令/将/把/对/向/给/与/和/且/并/的/之/于/为/则`…）与通用名词（`单位/卡牌/牌`）。
2. **宾语区未知词**（`具有 山地` 等）⇒ 同样拒绝（`action.ObjectRaw` 非空）。
3. **两数值＋属性名词＝歧义**（`防御力为 1 的友方步兵具有 +2 攻击力`）⇒ 拒绝（避免把限定值当增益量）。

三条都落 `needsCsx`（"已知语义、待 csx"，符合 E1-23 口径）——**不再产出错误效果**。

### 3. 影响（诚实度提升的量化）

| 指标 | 修正前 | 修正后 |
|---|---|---|
| 结构覆盖率 | 73.9% | 73.9%（**不变**——错误产出转为 needsCsx，仍属"结构可解析"） |
| **语义完整覆盖率** | 50.7% | **48.6%**（**-2.1pp**） |
| 语义完整卡数 | 787 | 755（**-32**） |

> **结论**：这 32 张此前是**被错误计入"语义完整"**的——修正后它们落 `needsCsx`，百分比下降即**真实水位**。

## E1-53：`counter.triggered`（反制触发）（**Done**）

- 信号 `counter.triggered`（载荷 `{Card, Player}`）；发射点＝`CounterCard.HandleUseCounterAsync` 的**激活分支**（恰一次；
  取消分支不发——退点撤销不属"触发"）。四处同步：`Signals` **28→29**、`helpers` 23→**24**、`GameHooksJson`、`GameHooksTests`。
- 模板 `counter_basic`（hook `counter.triggered`，载荷 `{Card, Player}` ⇒ 走**卡面**归属过滤白名单）。
- 映射：监听短语含 `反制` ⇒ `counter_basic`；`友方反制触发时` → `owner.same`、`触发敌方反制时` → `owner.different`。

## E1-54：事件卡属性过滤扩维（**甲**：tag／卡类型／阵营／卡名）（**Done**）

- `DslCondition.EventCardKeyword` → **`EventCardFilter`**（`Raw` ＝ `维度:取值`）：`keyword`／`tag`／`category`／`faction`／`name`。
  编译器按维度渲染真 csx（`Keywords.Has`／`TryGetData<TagData>().ContainsTag`／`Definition.Category`／`Definition.Faction`／
  `Name`）；**枚举取值经白名单校验**（`Enum.TryParse`＋`IsDefined`，从不把原文拼进代码）；
  各维度用**互不相同的模式变量名**（多条经 `all` 合取时同处一个表达式）。
- 映射：`FindEventCardFilters(rawText, rawOriginal)` 支持**多维合取**（`友方使用英国指令时` ⇒ `faction:Britain` ＋ `category:Command`）；
  **卡名取自原文切片**（`listen.RawText` 已剥引号 —— `友方使用“计划”时` ⇒ `name:计划`）；
  守卫**扁平合取**（不再嵌套 `all`）。
- 副产物：`敌方指令使用时` 也命中（`category:Command` ＋ `owner.different`）——同一语义的另一表达形态。
- **无属性线索**的 `使用…时`（如 `友方使用构筑之外的牌时`）仍显式失败（不泛触发）。

### 对拍刷新（E1-52/53/54 合计）

| 指标 | 起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 73.9% | **74.6%** |
| 覆盖率（语义完整口径） | 50.7% | **49.3%**（含 E1-52 的主动下修） |
| 未解析记录 | 601 | 587 |
| 模板库 | 28 | **29** |

### 新增测试

- `EffectParserTests`：`Parse_Maps_Counter_Trigger_With_Owner_Guard`；`Parse_Maps_EventCard_Attribute_Filters_With_Owner_Conjunction`
  （多维／卡名／指令使用时／无线索失败）；csx 编译语料 **+5**。
- `PlayChainCounterTests`：激活分支纳入 `counter.triggered`（受控变更，取消分支仍只发 `point.changed`）。

## E1-55：**未做**——`aura` op（持续态的正确机制）（**Open**，附设计）

> 用户本轮要求"做 1、2、3"；**1（反制触发）与 2（事件卡过滤扩维）已完成**，**3（aura）顺延**——
> 原因是 E1-52 的实测表明"aura 族"的大部分卡面带着**不可表达的目标限定**（`相邻`／`本单位左侧`／`防御力为 1 的`／
> `受伤`／`手牌中的`／`谢尔曼`），**光有 aura 机制也映射不了它们**；而能被 aura 表达的（可辨识谓词）**本来就已用
> `buff` 近似**。故真正的缺口是"**谓词表达能力**"，不是"缺少 aura op"。

**设计（下轮可直接实施）**：

1. `EffectRuntime.DeclareAuraAsync(Card host, string field, int delta, EffectAuraFilter filter, ct)`——
   经 `GameEnvironment.ResolveFor(host)` → `Auras.Register(AuraDeclaration.Add(host, field, delta, this, predicate))`
   → `environment.RerunAllCardsAsync(ct)`；**来源＝门面实例**（随效果卸载整组撤销）。
2. 谓词由**游戏层**从 `EffectAuraFilter`（side／faction／unitType／keyword／**excludeSelf**／**adjacent**）构造——
   `GameEnvironment` 已提供 `AreAdjacent`／`GetAdjacentUnits`／`GetPositionOf`／`IsOnFrontLine`，**相邻/位置谓词已具备读取面**。
3. 映射判据：**静态（无触发）的 `X 具有 +N` ⇒ `aura`**（动态重算，正确）；**触发体内的 `使…具有…` ⇒ `buff`**（含期限，正确）。
   —— 现状是"静态也用一次性 buff"＝**动/静机制混用**（近似，已由 E1-52 收掉最错的那批）。
4. 需新增：`aura` op 登记 ＋ 模板 ＋ `{{auraFilter...}}` 占位符 ＋ `EffectAuraFilter` 类型 ＋ 端到端用例。

### （本项**已实施**，见 E1-56；上文设计即其实施口径）

## E1-56：`aura` op（持续态的**正确机制**）＋ `excludeSelf` ＋ **`selZone` 收口**（**Done**）

> 用户口径：**先做 3（aura），再补 2（四维运行期端到端）**。

### 1. `aura` op（机制）

- `EffectRuntime.DeclareAuraAsync(host, field, delta, EffectAuraFilter, ct)`：
  `GameEnvironment.ResolveFor(host)` → `Auras.Register(AuraDeclaration.Add(host, field, delta, this, predicate))`
  → `RerunAllCardsAsync(ct)`。**来源＝门面实例** ⇒ 随效果卸载整组撤销；宿主离场由既有「在场」门禁兜住。
- **谓词不进 csx**：csx 只给出 `EffectAuraFilter(Side, Faction, UnitType, Keyword, ExcludeSelf, Zone)`，
  谓词本体由游戏层构造（`MatchesAura`：自身排除 → 阵营面 → 阵营 → 兵种 → 词条 → 区域）；
  csx 侧一行渲染 `{{auraFilter}}`（新占位符，**一次渲染全部实参**）。
  Hmm——`Zone` 经 `IsOnFrontLine`／`GetLineOf` 判定 ✓。
- DSL：`DslOp.Field`（`attack`／`defense`／`opCost`／`deployCost`，**白名单**映射到 `CardStatFields`）；
  `DslFilter` 扩 `Faction`／`ExcludeSelf`；`aura` 登记（必需 `field`＋`amount`；`amount` 允许负——同 `costMod`）。
- **动/静分工（本轮修正的机制口径）**：
  - **静态/持续文本（无触发且无期限）** ⇒ `具有 ±N` 走 **aura**（受益集合随进出/位置**实时重算**）；
  - **触发体内的一次性动作**（`部署：使…具有…`／带 `直到回合结束`） ⇒ 走 **buff/costMod**（可带期限）。
  实现见 `SemanticMapper.ToAuraOps`（`buff(atk)`→`aura(attack)`、`buff(atk,def)`→**两条**、`costMod`→`aura(opCost)`；
  `grant` 无光环面 ⇒ 原样保留并登记）。

### 2. `excludeSelf`（`其他/其它`）回收

- `HasUnmappableStateModifier` **移除** `其他/其它`（不再是"不可表达"）；
  `BuildSelector(target, action, excludeSelf)` 把该维度写进 `DslFilter.ExcludeSelf`；
  `IsSyntacticParticle` 收 `其他/其它/其/他/它`（其文字段须可被语义层消费，否则会被"未识别限定词"守卫拦下）。

### 3. **`selZone` 收口**（E1-41 起一直存在的**丢 zone 近似**）

- `OpTemplateCatalog` 增 `{{selZone}}` 占位符；**8 个走 `SelectAsync` 的 op 模板**由 `null` 改为 `{{selZone}}`
  ⇒ `支援阵线所有单位`／`前线…` 等 zone 限定**首次真正传给选靶**（此前静默丢 zone、按全场取目标）。

### 4. 对拍刷新

| 指标 | 起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 74.6% | 74.6%（**不变**——被回收的 `其他` 族此前已计为"可解析"） |
| 覆盖率（语义完整口径） | 49.3% | **49.6%**（`其他` 族由 `needsCsx` 升为**可直接运行**） |

> **诚实说明**：本轮机制价值 > 覆盖率数字——`aura` 修正了"静态持续态用一次性修饰器近似"的机制错配；
> 结构性增益已被 E1-52 的修正"吃掉"（那些卡早已计入可解析），故数字只涨在**语义完整**口径上。

### 5. 新增测试

- `EffectAuraTests`（新）：`Declared_Aura_Filters_Beneficiaries_And_Reevaluates_Dynamically`
  （阵营面/兵种面/排除自身三向断言 ＋ **登记后新加入者亦受益＝动态重算**）／
  `Aura_Is_Removed_With_Its_Source_Effect`（宿主离场 ⇒ 光环不再合成）。
- `EffectParserTests`：`Parse_Maps_State_Verb_To_Buff_Grant_And_CostMod` 扩为"静态⇒aura／触发体⇒修饰器／`其他`⇒excludeSelf"；
  `Parse_Reports_NeedsCsx_For_Unmappable_State` 增"相邻陆军／本单位左侧"两例。

### 6. **补 2**（用户要求"再做 2"）：事件卡过滤**四维运行期端到端**

- `EffectRuntimeEndToEndMoreTests.Generated_EventCard_Attribute_Filters_Fire_Only_For_Matching_Cards`：
  两张宿主各挂两条监听（**tag＝海军**／**卡类型＝指令＋敌方归属**／**阵营＝英国**／**卡名＝"计划"**），
  逐一 `EmitCardPlayed` 匹配卡与非匹配卡 ⇒ **命中各 +1、非命中零变化**。
  —— 至此 E1-54 的五个维度（词条已在 E1-42 验证）**全部有运行期证据**。
- 实现要点：监听宿主须**真实上场**（支援位 0 由测试基座占用 ⇒ 用 1、2）；被使用卡只需**实例**，
  用 `PlayChainTestKit.InstantiateLoadedAsync(..., toHand: false)` 避免干扰手牌计数；
  非匹配探针统一用"普通兵"（无 tag／非指令／日本／名不匹配）。

## E1-57：**真实数值条件**——把"占位条件"变成可求值（**语义完整口径主攻**）（**Done**）

> 用户口径：**先做语义完整口径**（乙口径＝"全部效果都无占位条件、无 `needsCsx`"的卡占比）。

### 1. 体检（决定性数据）

探针把 0 效果卡之外的"非语义完整"卡逐类打出：

- **含占位条件** = **78 张**，而其**种类只有 6 种**——全部是**数值比较算子**被当成 `raw` 条件：
  `不小于`(33)／`不大于`(30)／`大于`(8)／`小于`(4)／`至少`(3)。
- **含 `needsCsx`** = **318 张 / 274 种子句**（**长尾**：最多 7 次，绝大多数 1~2 次）——多为 KARDS 专有机制
  （双倍伤害／洗入卡组／复制／门禁`无法攻击`／`开发`／`山地`／`老兵`…）。
- ⇒ **单点最大杠杆＝数值条件**：`raw` ⇒ `if (false)` 意味着这 78 张的效果**根本不执行**（不只是"占位"）。

### 2. 交付

- **AST**：新增 **`ComparisonPhrase`**（E1-57）＋ `ClauseNode.Comparison`——
  `左度量（算子**之前**的过滤短语/阵营/区域 ＋ `…数` 计数标记）:算子:右操作数（算子后的数值，
  或"同一度量在另一方"）`。**顺带修正**：算子后的数值此前会被当成动作的"第二数值"
  （`消灭 1 个花费不大于 3 的单位` 的 `3` 曾被读成 `payload.second`）。
- **DSL**：
  - `DslCondition.Compare`（`Raw` ＝规范串 `count=s=friendly:gte:#3` 等）——**求值是纯函数**，故可安全参与 `&&` 合取；
  - `DslFilter` 增**阈值**（`ThresholdField`/`ThresholdOp`/`ThresholdValue`）＋ `EffectSelector.Threshold`
    （`EffectThreshold(Field, Op, Value)`）——**目标阈值过滤**（`消灭 1 个花费不大于 3 的单位`）。
- **编译器**：`ComparisonSpec.IsValid`（**只放行词表内的取值**——不把未受控文本拼进 csx）＋
  渲染 `EffectRuntime.EvaluateCondition(self, "spec")`；`{{selThreshold}}` 占位符（8 个选靶模板接上）。
- **运行期**：`EffectRuntime.EvaluateCondition`（**静态纯函数**）——度量＝`count=s=&lt;side&gt;`（单位数，双方均可）／
  `points=s=friendly`（剩余指挥点数）／`stat=f=defense;s=friendly;z=hq`（己方总部**生命值**）；算子＝`gte/lte/gt/lt/eq`；
  右操作数＝`#n` 或**同类度量**（`count=s=enemy`）；不可求值/含对手对象/无归属 ⇒ **false（不抛错）**。
  `EffectTargetResolveJudicator` 增阈值过滤（按目标卡**有效值**）。
- **映射**：自有子句的比较 ⇒ 真实条件；同子句的比较 ⇒ 选择器阈值（**不再落 `raw`**）；
  未覆盖形态（`大于敌方总部` 的**对手对象**引用／`对战词条数`／`花费至少为 1` 的**下限语义**）⇒ 仍 `raw`（**不静默执行**）。

### 3. 对拍刷新

| 指标 | 起点 | 终点 |
|---|---|---|
| 覆盖率（结构口径） | 74.6% | 74.6%（**不变**——这些卡本已"可解析"，只是效果为 `if(false)` 死代码） |
| **覆盖率（语义完整口径）** | **49.6%** | **52.2%**（+2.6pp / **+39 张**） |
| 含**占位条件** | 78 | **32**（-46） |
| 语义完整卡数 | 771 | **810** |

### 4. 新增测试

- `EffectConditionTests`（新）：`SelectAsync_Applies_Target_Threshold`（阈值过滤选中唯一满足者）／
  `EvaluateCondition_Compares_Unit_Counts_And_Hq_Defense`（计数/双方计数/总部生命值 ＋ **非法与不可求值一律 false**）。
- `EffectParserTests`：`Parse_Maps_Numeric_Comparison_To_Condition_And_Target_Threshold`
  （全局条件四种形态 ＋ 目标阈值 ＋ **未覆盖形态仍为占位**）；csx 编译语料 **+3**。

### 5. 仍未做（登记）

1. **反对手（敌方 HQ）属性引用**：`若友方总部防御力大于敌方总部` —— 求值层缺"对手对象"取面。
2. **`对战词条数`／`动员单位`／`上回合没有被攻击`** 等非数值条件（需状态/历史查询面）。
3. **`花费至少为 1`**：是**下限钳制**（对修正后的花费取值），不是条件——需独立 op 语义。
4. `needsCsx` 长尾（318 张/274 种）：多为 KARDS 专有机制（双倍伤害/洗入/复制/门禁/开发/山地/老兵），
   需**机制实现**而非解析改动。

## E1-58：**交付状态与暂停决议**（用户口径：先做到这里，后续完善游戏层表达力再拓展）

> **决议**：解析侧**暂停拓展**；**先完善游戏层表达力（机制／信号／选靶面），再回来接**。

### 1. 交付状态（一把尺子）

| 水位 | 含义 | 数值 |
|---|---|---|
| ① 结构可解析 | 能编译成**真 csx**（逐条真编译校验） | **74.6%**（1159/1553） |
| ② 语义完整 | 无占位条件、无 `needsCsx`/`csx` ⇒ **直接可跑** | **52.2%**（810） |
| ③ `needsCsx` 留痕 | 读懂了但机制缺失 ⇒ 空壳（**不猜着执行**） | 321 张 |
| ④ 占位条件 | 条件暂不可求值 ⇒ **空转**（不误跑） | 32 张 |
| ⑤ 未解析 | 显式失败 + 诊断 | 394 卡 / 587 记录 |

规模面：模板效果 **29**／op 原语模板 **19**／词表 **10**／对外信号 **29**／判定器 **19**；
全量测试 **1267 项全绿**（0 Skip）；**R8 违规面 0**（不静默丢弃）。

### 2. 停止依据（不是能力不足，而是**引擎缺机制**）

未解析 587 条的原因分布：**未识别动作词 375**（多为机制描述 `无法…`/`升为老兵`/`反制其部署`/`与其战斗`）／
无法识别动作短语 89（卡池内容层：天气/重复此效果）／监听型 80（老兵·指向·反制·条件态）／事件不在子集 43（`抉择`）。

**⇒ 继续堆词表只能推高 ①，推不动 ②。** 故将主攻切到游戏层。

### 3. 恢复条件（游戏层，按性价比）

1. **"等同于…"类动态数值 ＋ 事件数值透传**（≈42，最易——照 `unit.damage.dealt` / `counter.triggered` 的四处同步模式）。
2. **总部（HQ）作为效果对象**（`使友方总部获得…防御力／具有烟幕`）——需 HQ 属性修饰面。
3. **选靶面扩容**（≈12）：`相邻`／`本单位左侧`／`陆军（地面组）`／`手牌中的`／按卡类型锁定。
4. **钳制语义**（`花费至少为 1`，非条件）。
5. **机制实现批次**：门禁／洗入卡组／复制／双倍·溢出伤害／开发·抽取选择／冲击／收缴·协力·流亡／
   **老兵**（监听 ≈17）／预报·天气／山地／钳击／转换／隐蔽揭示／撤退·返回手中。
6. **条件续补**：对手对象取面／状态·历史条件（`上回合没有被攻击`、`动员`）。
7. **modal（抉择）**。

### 4. 解析侧遗留小口子（不阻塞，可随时做）

单位组维度（陆军/空军/海军）／`aura` 谓词扩展（相邻·阈值·受伤态·位置）／静态词条授予（`grant` 随在场持续）／
I35 自指过滤回收（正确性）／`hand`·`deck` 区域应显式失败／`nested` 运行时落地／端到端断言补齐（`shuffleIn`·`move`）。

> 完整的"能力边界＋例子＋归因"见 `交接文档.md` **§6**（接手先读）。
