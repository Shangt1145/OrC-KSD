# Requirements — OrC 效果解析器（卡面文本 → 效果序列化文本）

> ⚠️ **接手请先读 `交接文档.md`**（当前状态/交付物/剩余问题/硬约束/下一步）。
>
> **修订记录**（本文件为需求定稿，任务期间经用户裁决多次演进，以下为**已被后续决策覆盖**的条款）：
> - R2/R1 曾限定"只保证可反序列化、不保证 csx 可运行" ⇒ 后续扩展为 **csx 真编译 + 真运行**（hook 型监听已端到端验证）。
> - 曾限定"不触动 `src/Orc` 内核" ⇒ 后续为"可运行"按需做了**最小加性修改**（清单见交接文档 §2.2）。
> - R5 的 MVP 原语集已扩张（新增 `destroy`/`pin`/`silence`/`addToHand`/`shuffleIn`/`costMod`/`gainSlot`/`loseSlot`/`gainPoint`/`losePoint`/`nested`/`needsCsx` 等），表达力边界与语料覆盖率以交接文档 §5 为准。
> - R10 术语已写入根 `CONTEXT.md`（模板效果 / 效果 DSL / 词法单元 / 词法层 / 语法树 / 效果解析器）。
> - **R8 曾被实测违反**：语料 1553 条中 **295 条卡面"既无效果、也无诊断"＝静默丢弃** ⇒ **E1-38 已修复**
>   （诊断口径＝**整效果**粒度：`ops == 0 && failures == 0` 时补一条诊断；对拍报告新增「既无效果、也无诊断」守门列，当前 **0**）。
> - **R5 子集边界继续按语料扩充**：E1-39 新增信号 `unit.combat.survived`（27 条信号）与归属过滤的**玩家面**
>   （`owner.same.player`/`owner.different.player`，供无事件卡的 `slot.*` 载荷使用）；新增 3 个监听骨架（模板库 26 个）。
> - **R8 验收口径补强（用户裁决）**：**"结构可解析"计入覆盖率**，但**报告必须分层披露**
>   （语义完整／含占位条件／含 `needsCsx`／未解析）——避免"占位效果"抬高数字（E1-41）。
> - **R5 表达力继续扩充（归属类）**：E1-47 增信号 `unit.damage.dealt`（来源侧：`{Unit, Card, Amount}`）与
>   `card.died` 的**加性**载荷 `Killer`；DSL 增载荷字段守卫 `payload.self`／`payload.hq`（自指与受方面）；
>   模板库 28 个。**归属的"施动方/击杀者"面**（`友方单位造成…`）与本轮未做项见 `implementation.md` E1-47 §7。
> - **阶段决议（E1-58，用户口径）**：**解析侧暂停拓展**——结构可解析 74.6%／语义完整 52.2%，
>   剩余缺口**绝大多数是游戏层机制缺失**（未识别动作词 375 条多为机制描述）；**先完善游戏层表达力（机制/信号/选靶面），再回解析侧**。
>   能力边界、缺口归因与恢复条件见 `交接文档.md` §6。
> - **R5 条件求值面（E1-57）**：数值比较（单位数/指挥点数/总部生命值）⇒ **真实条件**（`DslCondition.Compare`，纯函数求值）；
>   **目标阈值**（`消灭 1 个花费不大于 3 的单位`）⇒ 选择器过滤（`EffectThreshold`）。**规范串只放行词表内取值**，
>   不可求值一律 false（不静默执行）。语义完整口径 49.6% → **52.2%**（占位条件 78 → 32）。
> - **R5 机制口径修正（E1-56）**：**静态/持续**文本（`X 具有 ±N`，无触发）改走**光环**（`aura` op ＋ `EffectRuntime.DeclareAuraAsync`，
>   受益随进出/位置**实时重算**，来源＝门面 ⇒ 卸载即撤）；**触发体内**仍走修饰器（可带期限）。同轮补上
>   `DslFilter.ExcludeSelf`（`其他/其它`）与 `{{selZone}}`（此前**静默丢 zone**）。
> - **R8 反向执行（E1-52）**：`state`（`具有`）族被查出 **8 类"静默丢弃目标限定词"的错误映射**
>   （如 `相邻陆军具有 +2 攻击力` 加成到宿主自己）⇒ 改判 `needsCsx`；**语义完整覆盖率主动下修**
>   （50.7% → 49.3%）——"可解析"不等于"可直接运行"，报告的分层披露正是为此。
> - **R5 继续扩充**：E1-53 增信号 `counter.triggered`（29 条信号）；E1-54 事件卡过滤扩为**五维**
>   （词条/tag/卡类型/阵营/卡名，取值经枚举白名单）；模板库 29 个。
> - **R5 归属补全（E1-50）**：DSL 增载荷字段**归属面**守卫 `payload.owner.same`/`payload.owner.different`
>   （回收 `友方单位造成…`／`友方单位消灭…`）；**效应侧归属**——`EffectRuntime.DamageAsync`/`KillAsync` 增 `source` 形参
>   （模板传 `self`）⇒ 效果伤害/消灭也产生归属。**主体判定有序**（先"本单位"、再"友方/敌方"），
>   主体不可辨识 ⇒ **不映射**（不泛触发）。
> - **R4/R5 继续扩充**：E1-41 增动作键 `具有`（属性/状态描述 → `buff`/`costMod`/`grant`，含**单子句多宾语**展开）
>   与 `buff`/`costMod` 的**期限**（`until`＝`turnEnd`／`nextOwnerTurnStart`，落地为既有 `ModifierExpiry`）；
>   并修"自指（无目标）op 的选靶＝`self`"（原缺省 `one` 会打到第一个单位）。
>
> 状态：**需求定稿**（下方 R1–R10 保留原始裁决，作为设计来源）。
> 一句话：为 OrC 引入三层流水线——OrC 提供**模板效果**（挖空 handler 的预制体）、**DSL**（官方带参原语 + csx 逃生舱）作中间层、**tokenizer + AST** 从中文卡面文本生成 DSL；最终产物为**带 csx 的 `EffectSnapshot`（效果预制体）JSON 文本**。解析器表达力为 OrC 引擎表达力的**显式子集**（参照 kards-diy 水平）。

## Scope
- **Includes**：
  - 三层流水线：① 模板效果 ② DSL ③ tokenizer + AST。
  - **先交付转换段**：DSL →（模板）→ 带 csx 的 `EffectSnapshot` JSON 文本 ＋ 两级模板库 ＋ DSL schema/序列化/校验。
  - **后交付解析段**：文本 → tokenizer → AST → DSL（含多效果切分）。
  - 模板库：源自**完整预制体"挖空" handler**，两级（trigger 骨架 + op 语句模板）。
- **Excludes**：
  - 通用效果解释器 + 运行时装载/执行（产物只保证"可反序列化"，不保证"可运行"）。
  - `src/Orc` 内核改动（本任务完全不碰）。
  - 卡级字段聚合与接线（"做完再接线"）。
  - UI/AI/网络；与 kards-diy 的数值对拍。

## Constraints
- 构建 0 错误 / 0 警告；测试全绿。
- **不触动 `src/Orc` 内核**；模板库与解析产物落 `src/Orc.Game`。
- DSL 表达力**必须是 OrC 引擎表达力的子集**（缺者走 csx 逃生舱，不得凭空扩展引擎语义）。
- **句式（结构规则）由官方（我们）内建维护；词表（词汇内容）外置**。
- 模板效果**依赖游戏资产里的效果预制体**，故其位置固定在 `src/Orc.Game`。

## Requirement Items

### R1: 三层流水线（总纲）
- Status: Confirmed
- Scenario/Trigger: 给出一段中文卡面效果文本，最终产出效果序列化文本。
- Behavior: 系统应采用三层：① **模板效果**（OrC 提供，主触发器固定、仅部分逻辑可变更）→ ② **DSL**（模板 + 定制：handler/参数；带参数 DSL 生成效果预制体）→ ③ **tokenizer + AST**（文本 → DSL）。
- Acceptance:
  - [ ] 三层职责边界明确、可各自独立检验。
  - [ ] 数据流单向：文本 → DSL → 预制体。
- Terminology: [卡牌数据体](../../../CONTEXT.md)、[效果快照/预制体](../review-chain/CONTEXT.md)

### R2: 转换段产物形态
- Status: Confirmed
- Scenario/Trigger: DSL 实例被转换。
- Behavior: 最终产物为 **OrC 原生 `EffectSnapshot`（效果预制体）JSON 文本**（schemaVersion=2）：**主触发器来自模板效果**，**事件 handler 为 csx 源码**（由 DSL 渲染、内联）。产物落点为 `EffectSnapshot` JSON 文本（可选落成 `*.prefab.json`）。
- Acceptance:
  - [ ] 生成文本可被 `PrefabJson.Deserialize` 成功解析。
  - [ ] 产物主触发器来自模板；`events[].csx` 来自 DSL。
  - [ ] 不产出 `assemblyKey` 路径的 handler。

### R3: DSL 形态（混合）
- Status: Confirmed
- Scenario/Trigger: 用 DSL 描述一个效果的定制内容。
- Behavior: DSL 为**独立可序列化的中间层**，含两类内容：
  - **官方原语**＝带参数的 op 模式（声明式，解析器可产出）；
  - **csx 逃生舱原语**：参数为 csx 脚本，落地为**动态 handler + csx 注入**；由人工填，解析器**不产出其 csx 内容**（仅可产出"这是逃生舱"）。
- Acceptance:
  - [ ] 官方原语均为带参 op，可被解析器产出。
  - [ ] 存在 csx 逃生舱原语，转换时以动态 handler + csx 注入落地。
  - [ ] 逃生舱的 csx 内容不参与解析段自动生成。

### R4: 模板效果（专用格式 / 槽位 / 位置）
- Status: Confirmed（**已修订**：见 R4-Rev）
- Scenario/Trigger: 转换器需要一个可复用、可参数化的效果骨架。
- Behavior: **为模板效果设计专门（专用）的"模板效果预制体"格式**（**不依赖**实际/原生预制体）；模板在其中**显式声明 handler 槽位**——把部分 handler 声明为 **DSL 可填写槽位**（因一个效果可有**多个 handler**，如"部署"需注入主动触发部署效果的 handler）。槽位以**语义槽位名 + 映射**表达，**DSL 只认槽位名**，不认预制体内部路径。转换器据模板 ＋ DSL 生成原生 `EffectSnapshot`。位置固定在 `src/Orc.Game`。
- **R4-Rev（实现 grill 中途需求变更）**：原定"模板＝OrC 原生 `EffectSnapshot` 骨架、从完整预制体挖空"**作废**；改为上述**专用模板效果格式**。
- Acceptance:
  - [ ] 存在独立的"模板效果预制体"格式定义。
  - [ ] 模板可声明多个具名 handler 槽位。
  - [ ] DSL 通过槽位名填写；不引用预制体内部路径。
  - [ ] 模板库位于 `src/Orc.Game`。

### R5: 表达力子集边界与首批范围
- Status: Confirmed
- Scenario/Trigger: 决定纳入哪些 DSL 原语与模板。
- Behavior: 以 **kards-diy 卡面语料高频句式**驱动，**只纳入 OrC 已具备机制**；OrC 缺者走 csx 逃生舱。首批（MVP）：**1 个 trigger 骨架 + 3~5 个高频 op 模板**（如 `damage`/`draw`/`buff`/`grant`/`move`），端到端打通后按语料扩充。
- Acceptance:
  - [ ] MVP 原语集明确且端到端可跑。
  - [ ] 每个官方原语都能对上一个 OrC 既有机制（子集可审计）。
  - [ ] 超出子集者一律归入 csx 逃生舱。

### R6: 解析段契约
- Status: Confirmed
- Scenario/Trigger: 中文卡面文本进入解析段。
- Behavior: 本任务＝**全链分阶段**（先转换段，后解析段）。输入来源：**kards-diy 卡池**（对拍语料）＋ **`docs/初始设计/kards官方卡牌.json` 的 `text.zh-Hans`**（真实样本）＋ 工具支持**手输**。粒度：**AST 处理单效果边界**；**单牌描述可能含多个效果**（需切分）。切分语义**沿用 kards-diy**：`。`＝硬边界（后句独立）；`；`＝软边界（后句仍受前句触发/条件控制）；换行无权重；无触发词时**首句（至第一个逗号）为触发原语**。产出聚合为 **DSL 数组**。
- Acceptance:
  - [ ] 同一张牌的多效果能被切成多个单效果各自进 AST。
  - [ ] `。` / `；` / 换行 / 首句触发的语义与 kards-diy 一致。
  - [ ] 产出为 DSL 数组。
- Terminology: [token/AST](#)（待登记）

### R7: 中间层与语法规则承载
- Status: Confirmed
- Scenario/Trigger: 文本进入 tokenizer 与 AST。
- Behavior: 引入 **tokenizer + AST 作中间层**。**句式（结构规则）由官方（我们）内建维护；词表（词汇内容）外置**。token 类型划分（12 类）：`TRIGGER`（触发词）、`PUNCT`（标点）、`CONNECTIVE`（连接/逻辑）、`PRONOUN`（代词/回指）为内建句式；`ACTION`（动作词）、`QUANT`（量词/选靶）、`SIDE`（阵营）、`ZONE`（区域/落点）、`FILTER`（过滤）、`COND`（条件）、`NUM`（数值/表达式）为外置词表；`UNKNOWN`（未知词）触发显式失败。
- Acceptance:
  - [ ] tokenizer 走"词表最长匹配扫描"（中文无空格）。
  - [ ] 句式内建、词表外置，二者边界清晰。
  - [ ] AST 为单效果语法树。
- Terminology: [tokenizer/AST](#)（待登记）

### R8: 落败处置与验收口径
- Status: Confirmed
- Scenario/Trigger: 文本超出子集；或交付验收。
- Behavior: 落败＝**显式失败 + 诊断**（产出"未解析"记录：原文片段 + 失败原因，**不产出效果**）。验收＝**语料驱动 + 往返一致**（kards-diy 卡池取 N 条；断言生成文本可被 `PrefabJson.Deserialize` 解析 ＋ 覆盖率报告，先不设硬门槛）。
- Acceptance:
  - [ ] 超子集文本产生显式未解析记录，不静默丢弃、不产出占位效果。
  - [ ] 存在往返一致性测试（DSL→预制体→反序列化）。
  - [ ] 产出覆盖率报告（无硬门槛）。

### R9: 交付位置
- Status: Confirmed
- Scenario/Trigger: 代码落点。
- Behavior: 模板库、DSL、转换器、tokenizer/AST/解析器均落 **`src/Orc.Game` 内新增命名空间**（非新工程）。
- Acceptance:
  - [ ] 不新增独立工程。
  - [ ] `src/Orc` 内核无改动。

### R10: 术语登记
- Status: Draft
- Scenario/Trigger: 文档与代码讨论使用统一称谓。
- Behavior: 拟在根 `CONTEXT.md` 登记：`词法单元（token）`、`词法层（tokenizer）`、`语法树（AST）`、`效果解析器`、`效果 DSL`、`模板效果`。
- Acceptance:
  - [ ] 术语写入根 `CONTEXT.md`。
