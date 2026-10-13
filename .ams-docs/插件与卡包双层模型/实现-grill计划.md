# Implementation Grill Plan — 插件与卡包双层资产模型

> 状态：**进行中**（需求 grill 已收束，定稿见《需求文档.md》；本文件记录实现阶段待 grill 的问题，按实现逻辑顺序）。
> 相关：`.ams/context/*`（既有工程约束）、既有先例——"判定器收编 K1–K5"、"csx 动态效果（批 4）"、"分发预检器"、"卡牌数据体持久化"。

## 基线（工程与依赖，代码已核）

- 工程：`src/Orc`（内核）→ `src/Orc.Script`（Roslyn csx，逆向依赖内核）→ `src/Orc.Game`（游戏层）。
  铁律：**内核零 Roslyn 依赖**（`Scripting.cs:5-11` 接口倒置）。
- 既有扩展面：`JudicatorRegistry`（内核，对局级）、`PrefabManager`（内核，扫 `*.prefab.json`）、`TriggerRegistry`（游戏层静态）、`KeywordRegistry`/`CardComponentRegistry`（游戏层静态）。
- 装配中枢：`Match.Initialize`（`Match.cs:393`，41 步）。
- 分发预检器先例：dry-run 只读校验（Errors/Warnings 分列、Error>0 不放行）——R6/R7 可复用其报告口径。

## 待 grill 的问题（按实现逻辑顺序）

### P1: 实现批次划分（S 序列）与工程边界/依赖方向【已答：a+c 合并】
裁定（2026-10-12）：**a+c 合并**。
- 内核 `Orc`：资产服务抽象（与 `Judicator` 同层）＋ 资产命名文法/解析 ＋ csx 契约（接口倒置）。
- `Orc.Script`：csx「注册期入口」实现。
- **新增 `src/Orc.Packages`**：包管理器 ＋ `EngineHost`/`PackageRuntime` ＋ manifest ＋ 版本解析。
- `Orc.Game`：资产服务内置实现 ＋ 游戏层资产迁移。
- 依赖方向：`Orc.Packages → Orc.Game → Orc.Script → Orc`（单向）。
- S 序列（13 步）确认。

配套：S 序列提案（已确认）——
- S1 术语登记（CONTEXT）＋ 需求归档
- S2 内核：资产服务抽象（与 `Judicator` 同层）＋ 资产命名文法/解析
- S3 内核↔`Orc.Script`：csx「注册期入口」契约扩展
- S4 游戏层：静态资产数据化（`Faction`/`UnitType` → 资产服务＋数据表）＋ 消费点改造
- S5 包管理器基础：manifest schema ＋ 发现/清单解析
- S6 包管理器：版本选择 ＋ 依赖拓扑 ＋ 加载顺序 ＋ 失败隔离
- S7 `EngineHost`：插件冷加载 ＋ 资产目录（多版本并存）
- S8 插件装配：混合包（数据JSON/csx/可选程序集）
- S9 卡包：热加载 ＋ 注入当前对局 ＋ 装配优先级
- S10 moding 装配：插件声明 → 每对局 `Initialize` 应用
- S11 三层校验（预检器扩展 ＋ 建局 fail-fast ＋ 装载期隔离）
- S12 内置资产迁移首批（判定器/资产服务 → 内置插件）
- S13 测试与验收（0 警告 ／ 全绿 ／ 端到端）

### P2: csx「注册期入口」契约扩展形态【已答：a】
裁定（2026-10-12）：**选 a——新增并列接口 `IPluginScriptEvaluator`**（`PluginScriptRequest(Source,EntryName,TimeoutMs)` → `PluginScriptResult{Ok(Action<IPluginRegistrationContext>) | Fail}`）；不改既有 `IScriptEvaluator` 语义。
- P2-a：**具名变量 `Register`**（与既有 `HandleAsync` 具名变量约定一致）。
- P2-b：**复用转发** `JudicatorRegistry.Register` / `PrefabManager.RegisterHandler`（不造第二注册面）。
- P2-c（默认·可否决）：允许引用程序集追加 `Orc.Packages`；提供导入便利面缓解"程序集限定名"限制。
- P2-d（默认·可否决）：沿用六类失败分类；缓存键追加**包 GUID ＋ 版本**（防多版本同源串味）。

### P2 旧提案（存档）
现状 `IScriptEvaluator.Evaluate(ScriptRequest)` 只回吐 `Func<TView,Context,CancellationToken,Task>`（`Scripting.cs:71-105`）；`ScriptRequest` 必带 `ViewType`（用于绑定）；缓存键含 `ViewType+EntryName`；白名单程序集＝`System.*`＋`Orc`＋`Orc.Game`，导入＝`System.*`＋`Orc.Core`＋`Orc.Cards`；屏蔽词含 `Assembly`/`File`/`Environment`。
插件注册项（资产服务/判定器/效果 handler/moding）**没有 ViewType**，故需新契约。
- a) **新增并列接口 `IPluginScriptEvaluator`**：`PluginScriptRequest(Source, EntryName, TimeoutMs)` → `PluginScriptResult{ Ok(Action<IPluginRegistrationContext>), Fail(category,error) }`；csx 入口约定具名变量 `Register`；宿主执行之，脚本经 ctx 受控方法注册。
- b) **扩展 `ScriptRequest`**：加 `EntryKind`（TriggerHandler / PluginRegistration）；插件项 `ViewType` 可空；返回未绑定 `Delegate`，调用方按 kind 包装。
- c) **独立脚本模块加载器**（不纳入 `IScriptEvaluator` 家族）：直接 Roslyn 编译＋反射取入口，宿主自管。

### P3: 资产服务在内核的落点【已答：c】
裁定（2026-10-12）：**选 c——抽公共机制内核基类 ＋ 独立 `AssetServiceRegistry`**（与 `JudicatorRegistry` 并列实例）；名字域隔离，保住"判定器名＝受控常量集"不变量。
- P3-a（同意）：**泛型机制基类**（如 `EntryRegistry<TEntry>`，承载条目+moding栈+句柄）。
- P3-b（同意）：底层沿用无类型 `object[]? Invoke(object[]?)`（与判定器对称）＋ 上层薄强类型包装。
- P3-c（同意）：moding 与判定器**同栈语义、后者胜**，仅名字域不同。

### P3 旧提案（存档）
⚠️ **既有不变量冲突**：`JudicatorRegistry` 明示"**判定器名＝受控常量集合为唯一称谓来源**"（`JudicatorRegistry.cs:23`、`JudicatorBinding.cs:39`）。若把插件命名的资产服务塞进**同一注册表同一名字域**（选项 a），会**打破该不变量**（插件资产名将成为第二称谓来源）。
- a) 内核新增 `AssetService` 抽象（与 `Judicator` 同层）＋ **并入同一 `JudicatorRegistry`**（同表不同名域）——撞上述不变量。
- b) 不进内核：资产服务＝游戏层概念，用现有判定器承载（不新增类型）——语义扭曲（"存"当"算"）。
- c) **抽出公共机制内核基类 ＋ 独立 `AssetServiceRegistry`**（与 `JudicatorRegistry` 并列的实例）——名字域隔离，机制复用。

### P3-a 公共机制的抽取方式【待答】
泛型基类（如 `EntryRegistry<TEntry>` 承载"条目+moding栈+句柄"）／各自独立实现（复制）／共享接口不共享实现？

### P3-b 资产服务的调用契约【待答】
沿用 `object[]? Invoke(object[]?)` 无类型面／新增强类型面（如 `AssetEntry GetAsset(kind,id)` / `IReadOnlyList<AssetEntry> List(kind)`）？资产数据是否缓存于服务内（"储存"语义）？

### P3-c moding 语义一致性【待答】
资产服务的 moding 是否与判定器"同栈语义、后者胜"完全一致？（建议：一致，仅名字域不同。）

### P4: 静态资产数据化的改造范围与过渡策略【已答：b】
裁定（2026-10-12）：**选 b——string 常量类 ＋ 真源迁移**。`enum` → `FactionIds`/`UnitTypeIds` 之类 string 常量类（对齐既有 `KeywordIds` 模式）；真源＝资产服务；约 45 个测试文件机械替换。
- P4-a（同意）：**string 常量类**（保留可读符号）。
- P4-b（同意）：**两者兼有**——静态属性入资产条目数据，可改写裁决入资产服务。
- P4-c（同意）：`Rarity`/`CardCategory` **本次不做**，留后续批次。
- P4-d（默认·可否决）：卡数据 `faction`/`types` 改**资产 id**（小写如 `germany`/`infantry`）；官方语料枚举名→资产 id 走**显式映射表**（对齐 `CardAttributeMap` 先例），未命中＝留痕隔离。

### P4 旧提案（存档）
**波及面实测**（`rg` 计数）：`Faction.` 命中约 **53 文件**（src 仅 4：`CardDefinition`/`BuiltInCardComponents`/`FactionRules`/`Match`；其余约 45 为 tests）；`UnitType.` 命中约 **39 文件**（src 仅 3：`CombatTypeGroups`/`CommandManager`/`BuiltInCardComponents`；其余为 tests）。枚举定义 4 个：`Faction`/`Rarity`（`TagData.cs`）、`UnitType`（`UnitType.cs`）、`CardCategory`（`CardCategory.cs`）。
- a) **一次性全量爆改**：删除枚举，全部调用点改字符串/服务查询（含约 45 个测试文件）。
- b) **符号保留＋真源迁移**：`enum` → **string 常量类**（如 `FactionIds.Germany`），真源＝资产服务；属性/关系（主国/可盟/类型组）迁到资产条目；调用点机械替换。
- c) **保留 enum 不动**，仅新增资产服务做查询——❌ 违反 R1 验收"无编译期枚举真源"。

### P4-a 枚举符号去留【待答】
改为 string 常量类（保留可读符号）／完全删除（调用点用裸字符串或服务查询）？

### P4-b 属性/关系承载【待答】
`FactionRules.MajorFactions`/`IsValidAlly`、`CombatTypeGroups` 类型组 → **资产条目属性**（数据）／**资产服务方法**（逻辑）／两者兼有？

### P4-c 范围边界：`Rarity`/`CardCategory` 是否纳入【待答】
需求只点名"阵营/单位枚举"。`Rarity`（稀有度，枚举）与 `CardCategory`（卡类别，枚举）本次是否同样数据化？（它们与"卡包"关系更近。）

### P4-d 卡数据 schema 与官方语料【待答】
`*.card.json` 的 `faction`/`types` 字段（现值＝枚举名如 `Germany`/`Infantry`）——是否改为资产 id（如 `germany`）？官方 1646 张卡的取值如何对齐资产条目（映射表 / 大小写规则）？

### P5: GUID8 归属 ＋ manifest schema 与包目录布局【已答：包GUID8】
裁定（2026-10-12）：**GUID8 归属＝包**；**包内所有资产继承包的 GUID**。
资产全名 ＝ `{类型名}：{资产名}@{包GUID8}#{版本}`；默认**资产版本随包版本**（版本唯一源＝包；manifest 的 `assets[]` 可省 `version`）——可否决。
- P5-a（同意）：字段集如草案；`assets[].version` 随包（可省）。
- P5-b（同意）：`plugins/` 与 `packs/` **分置**。
- P5-c（同意）：**JSON**。

### P5 旧提案（存档）
⚠️ **需求内部不一致**：Q5-a 答"资产名＝类型＋**包**GUID 前 8 位"，而 Q5-c 定稿文法写的是 `{类型名}：{资产名}@**{资产GUID8}**#{版本号}`——GUID8 到底归属**包**还是**资产自身**？必须澄清（两类后果不同：包GUID → 同包资产共享 GUID8、命名稳定；资产GUID → 每个资产各需一个稳定 GUID、同名风险更低但登记成本更高）。

manifest 草案（待确认）：
```json
// plugins/<包GUID8>/manifest.json
{
  "kind": "plugin",              // plugin | cardpack
  "id": "orc.core.keywords",     // 人类可读包 id
  "guid": "ab12cd34-…",          // 稳定身份（GUID8＝前8位）
  "version": "1.2.0",            // x.y.z
  "dependsOn": [ { "id": "orc.core.base", "version": "1.*.*" } ],
  "assets": [
    { "type": "csx",  "name": "faction.service", "path": "csx/faction.csx",  "version": "1.0.0" },
    { "type": "json", "name": "faction.table",   "path": "data/factions.json", "version": "1.0.0" }
  ]
}
```
目录布局草案：
```
plugins/<包GUID8>/manifest.json
plugins/<包GUID8>/data/*.json      # 数据资产
plugins/<包GUID8>/csx/*.csx        # 逻辑资产
plugins/<包GUID8>/asm/*.dll        # 可选程序集（逃生舱）
packs/<包GUID8>/manifest.json
packs/<包GUID8>/cards/*.card.json  # 卡牌数据
```

### P5-a manifest 字段集【待答】
上述字段是否齐备（缺 `name`/`author`/`engineVersion` 约束？`assets[].version` 与包 `version` 关系？）

### P5-b 包类型与目录根【待答】
`plugins/` 与 `packs/` 两个根目录分置，还是统一 `packages/` 下用 `kind` 区分？

### P5-c manifest 格式【待答】
JSON（对齐既有 `*.card.json`/`*.prefab.json` 风格）／其它？

### P6: `Match` 与 `EngineHost` 的接线方式【已答：a】
裁定（2026-10-12）：**选 a——构造注入"目录快照"对象**（`null`＝内置默认，保独立可测）。
- P6-a（同意）：快照**全含**（判定器实例＋modings／资产服务实例＋数据／效果注册／卡定义／版本锁定记录）。
- P6-b（同意）：与既有 `judicatorAssembly`/`effectRegistry` **并存过渡**（快照优先）。
- P6-c（同意）：**记录**本次对局锁定的 (资产,版本)。
- P6-d（确认）：卡包热加载经"建局时生成快照"自然满足 R8"只对新对局生效"。

### P6 旧提案（存档）
`Match` 构造/`Initialize` 如何获得"资产目录快照"？现状已有可选注入参数：`targeterBridge`/`effectRegistry`/`deckConfigForPlayerA/B`/`judicatorAssembly`（`Match.cs:79-90`），且内置判定器注册段为**固定无条件**执行（`Match.cs:514-605`）。
- a) **构造注入"目录快照"对象**（`AssetCatalogSnapshot`/`MatchAssets`）：`Match` 从快照应用判定器/资产服务/效果/modings；`null`＝现有内置默认（保独立可测）。
- b) 全局 `EngineHost.Current` 静态访问（简单，破可测性）。
- c) 现有可选参数**逐个扩展**（`judicatorAssembly` → 全量装配段）。

### P6-a 快照对象的内容粒度【待答】
快照包含哪些：判定器实例＋modings／资产服务实例＋数据／效果注册（`PrefabManager` 内容）／卡定义（`CardLibrary` 内容）／版本锁定记录？

### P6-b 与既有注入参数的关系【待答】
`judicatorAssembly`／`effectRegistry` 是**替换**为快照注入、还是保留并存过渡（测试与 sample 依赖它们）？

### P6-c 不可变性与版本锁定【待答】
快照对局内不可变？是否记录"本次对局锁定的一组 (资产,版本)"供审计/复现？

### P6-d 卡包热加载的进入路径【待答】
新对局建局时从"当前目录 ＋ 卡包注册表"生成快照 → 是否即满足 R8"只对新对局生效"？（预计是，确认即可。）

### P7: 内置注册段去留 ＋ 迁移期双轨 ＋ 内置插件形态【已答：b ＋ 丙】
裁定（2026-10-12）：**主问 b——一刀切**（直删内置注册段、全走插件目录；**允许备份**、**要求一次成型**，不留双轨）；**P7-a 丙**——内置资产放**独立工程**（暂名 `src/Orc.Plugins.BuiltIn`），构建产出**物理插件包**随发布。
- P7-b（同意）：每迁一类即删对应内置段。
- P7-c（同意）：`KeywordRegistry`/`CardComponentRegistry`/`TriggerRegistry` 的内置项属**后续批次**。
- P7-d（同意·但见 P8 冲突）：既有测试保持不变 ＋ 新增插件路径专项测试。

### P8（新发现）：P7=b＋丙 与 P6-a／P7-d 冲突【已答：i】
裁定（2026-10-12）：**选 i**。
- `Match` 的 `null` 快照语义**修订为"空资产目录"**（不再回落内置默认）。
- 提供宿主/测试辅助（如 `TestHost.NewMatch(...)`）：内部完成"加载内置插件包 → 生成快照 → 建局"。
- 既有约 45 个测试文件**机械替换**（`new Match(...)` → 辅助建局）。
- 连带修订：P6-a"`null`＝内置默认"表述已作废。

## 结案（2026-10-12）
实现 grill 决策树闭合；实现文档定稿见《实现文档.md》。开工。

## 实现期新增问题（开工后）

### P9: 常量类命名策略（S4 前置）【本轮主问题·待答】
删除 `enum Faction`/`enum UnitType` 后，符号保留方式两选一，**改动量差异巨大**：
- **(甲) 保留原类名**：`public static class Faction { public const string Germany = "germany"; }`——`Faction.Germany` 调用点**原样编译**（值变 string）；只需改**类型位置**（`Faction`→`string`、`Faction?`→`string?`、`List<UnitType>`→`IReadOnlyList<string>`）＋枚举专属操作（`Enum.IsDefined`/`Enum.TryParse<Faction>`）。预计 src ~14 处、tests 少量。**风险低**。
- **(乙) 改名 `FactionIds`/`UnitTypeIds`**：对齐 `KeywordIds` 命名风格、语义更清晰；但需批量替换大量 `Faction.` → `FactionIds.` 调用点（约 50 文件），且**限定名引用**（如 `Cards.UnitType`）与**类型位置**需甄别，批量脚本易误伤。**风险中高**。

推荐 **(甲)**：命名稍欠理想（像枚举），但把"大爆改"降为"类型位置小改"，显著降低破坏全绿的风险；命名一致性可在后续批次统一（如 `Faction → FactionIds` 的纯机械重命名单独做）。
- 裁定（2026-10-12）：**甲**。已落地，测试仅 6 文件/17 处。

## 实现期新增问题（S12 开工前）

### P10: 内置判定器的「对局协作者」如何经插件装载获得【本轮主问题·待答】
⚠️ **障碍**：S12 要把 `Match.Initialize:514-605` 的 20 条内置判定器注册**迁出**为内置插件的声明。但其中约一半判定器**需要对局内部协作者**（现状以**延后求值 lambda** 传入）：
- `CombatRangeJudicator(Battlefield, owner => EnemyOfPlayer(owner))`
- `CombatTargetLegalJudicator(enemyOf: …)`｜`CombatInterceptionJudicator(Battlefield, enemyOf)`
- `LegEligibilityJudicator(() => CurrentPlayer, Battlefield, isMove)`｜`MoveFrontlineEnemyJudicator(Battlefield, enemyOf)`
- `MoveRevalidationJudicator(lifecycle, legInvoker, battlefield, frontlineInvoker)`｜`AttackRevalidationJudicator(lifecycle, legInvoker, isAttackLegal)`
- `EffectTargetResolveJudicator(Battlefield, …)`
- （自足型：`CostCheck`/`CounterUse`/`TargetCandidateEligibility`/`CombatCounterEligibility`/`CombatAmbushCondition`/`CombatGuardEligibility` 与 5 条 `Point*` 资源判定器——无协作者。）

而插件脚本（csx）**只能经 `IPluginRegistrationContext`** 拿东西——当前受控面无任何对局协作者，故 csx 无法构造这些判定器。三选一：
- **(甲) 受控对局服务面**：受控面增 `IMatchServices`（只读、含**延后求值访问器**：`Battlefield`/`CurrentPlayer`/`EnemyOf(player)`/`EffectRuntime`/`Lifecycle`…）——插件据此 `new` 判定器，与现状 lambda 形态一一对应。与"核心 API 可 moding"意图一致；代价：需界定开放面边界。
- **(乙) 工厂 ＋ 延后绑定**：受控面只收 `RegisterJudicatorFactory(name, Func<IMatchServices, Judicator>)`；`Match.Initialize` 在协作者就绪后调用工厂产出实例。插件声明工厂、不直接接触内部；仍需 `IMatchServices`，但装配时序更松。
- **(丙) S12 分期**：首批只迁**自足型**内置资产（19 词条／资源类判定器／资产服务），**有协作者依赖的判定器暂留 `Match.Initialize`**；后续批次再迁。

推荐 **(甲)**：与现状**延后求值 lambda 一一对应**、改动最小，且正面满足"核心 API 可 moding"；边界经 `IMatchServices` 显式收窄（只暴露判定器实际需要的只读面）。
- 裁定（2026-10-12）：**甲**。已落地（S12a）并被 S12b/S12c 使用。
- 甲＋丙、P8=i 均已落地（S12a–S13）。

## 后续批次（开工前）

### P11: 多阶段激活模型——插件资产的作用域与阶段【本轮主问题·待答】
**实测结论**（代码核对）：
- `TriggerRegistry` ＝ **实例·对局级**（`Match` 里的 `TriggerRegistry` 是成员，非静态）；其注册**时点在对局内**（CommandManager 创建**之后**，需触发器实例）。
- `KeywordRegistry`（19 词条）／`CardComponentRegistry`（6 组件）＝ **静态·进程级**（静态构造注册，早于一切对局；"重复注册拒绝"）。
- `Selectors` ＝ 静态数据（`SelectorNames` 为**冻结常量契约**，仅 `SelectorTemplates` 可迁）。

⇒ 现有"**单相·对局级**激活"装不下：进程级资产须在**冷加载期**注册；而对局级资产又分**两个时点**（判定器须早于 CommandManager；触发器须晚于 CommandManager）。三选一：
- **(甲) 资产级作用域＋阶段声明**：manifest 的 `assets[]` 增 `"scope": "process" | "match"`（默认 match）与 `"phase": "preCommand" | "postCommand"`（仅 match 有意义）；宿主按声明分相执行（每资产自己的 `Register` 入口）。语义最清晰、粒度最细。
- **(乙) 包级作用域**：整包属进程级或对局级；对局级内部再按"是否需协作者"拆两包。粗粒度、包数量增多。
- **(丙) 双入口名**：csx 定义 `Register`（对局级）与 `RegisterProcess`（进程级）＋ `RegisterTriggers`（后置）；宿主按名求值。入口名硬编码、扩展性差。

推荐 **(甲)**：与既有"资产即声明"模型一致；`scope`/`phase` 是纯增量字段，不影响既有包。

### 批次顺序（P11 定后执行）
1. P11 机制（scope/phase 管线）＋ **触发器注册迁出**（match·postCommand）。
2. **词条 19**（process）＋ **数据组件 6**（process）→ 内置插件进程级资产（`Orc.Plugins.BuiltIn` 已有 `InternalsVisibleTo`；进程级注册须早于任何对局 ⇒ 由内置插件的**模块初始化**或 `EngineHost` 冷加载触发）。
3. `SelectorTemplates` 迁移（静态数据）。
4. `Rarity`/`CardCategory` 数据化（同 S4 的类型迁移）。
5. 包管理器高级能力：卸载／重载／回滚。

### P7 旧提案（存档）
基线：`Match.Initialize` 固定内置注册段＝20 判定器（`514-605`）＋ 4 流程触发器 LowLevel（`667-670`）＋ `KeywordEffectAssembly.Assemble`（`699`）＋ `MatchNamedTriggers`（`625`）；另有**静态构造装内置**的 `KeywordRegistry`（19 词条）/`CardComponentRegistry`（6 组件）——它们也是"内置资产"。既有约 45 个测试文件直接 `new Match(...)` 并依赖内置默认行为。
- a) **双轨并存**：内置注册段保留（作为"内置插件"的**实现来源**），插件机制经快照**叠加**；S12 再把内置段逐步改写为"内置插件的资产声明"。测试先绿、再迁。
- b) **一刀切**：S4 直删内置段、全走插件目录，测试同步大改（风险高，违 Q8=c 分批）。
- c) **编译期/配置开关**：内置段以开关保留，默认走插件。

### P7-a 内置插件的形态【待答】
- (甲) **代码内声明的包**（不落文件系统；直接构造 AssetCatalog 条目）——保留编译期安全、零 IO、启动快。
- (乙) **随程序集输出的物理插件目录**（dogfooding 最彻底，"引擎核心零业务资产"的字面实现）——需启动扫盘。

### P7-b 迁移期双轨的清理时机【待答】
每迁一类即删对应内置段／全部迁完统一删？

### P7-c 静态注册面（词条/组件/触发器）的归属批次【待答】
`KeywordRegistry`/`CardComponentRegistry`/`TriggerRegistry` 的静态构造内置项属"后续批次"（Q8=c 首批仅判定器/资产服务），确认？

### P7-d 测试策略【待答】
既有测试保持不变（靠 `null` 快照走内置默认）／新增插件路径专项测试？
