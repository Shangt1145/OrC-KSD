# Requirements Grill Plan — OrC 效果解析器（卡面文本 → 效果序列化文本）

> 本 grill 聚焦：OrC 侧"中文卡面文本 →（tokenizer / AST 中间层）→ DSL → 带 csx 的完整效果预制体文本"这一能力的**对外行为**。
> 基线：已读 kards-diy 解析管线（`compiler.js`/`primitives.js`/`parser-v2.js`/`effect-contract.js`）与 OrC 效果载体（`src/Orc/Cards/Prefabs.cs`、`PrefabJson.cs`、`src/Orc.Game/Cards/Data/*`）。

## 已识别的核心矛盾

- **X1（产物-可执行性）**：kards-diy 的 DSL 既是解析产物又是执行格式（纯数据）；OrC 的 `EffectSnapshot` **不是纯数据**（事件须带 handler 来源 csx/assemblyKey）。→ 已由 Q3(csx) + Q2(A) 消解。
- **X2（"子集"参照物）**：OrC 表达力＝任意 C#（图灵完备），"子集"需收窄到**可枚举原语集**。→ 由 Q3/Q5 定义。
- **X4（覆盖 Q1 排除项）**：Q1(b) 曾排除"产出 EffectSnapshot / 生成 C# 脚本"，Q2 已推翻。→ 最终产物就是带 csx 的预制体。

## 已决问题

### Q1: 目标产物形态 {Completed}
- (b) 新增 OrC 声明式"效果 DSL"作为**独立中间层**；该 DSL 表达力为 OrC 引擎的**显式子集**。
- DSL 最终产物：**带 csx 的 `EffectSnapshot`（效果预制体）JSON 文本**（X4 覆盖 Q1 原排除项）。

### Q1a: 是否必须"可被 OrC 加载执行" {Completed}
- (ii) 本轮**不做**通用解释器/运行时装载（Excludes）；只交付 DSL schema + 序列化/校验 + 转换 + 解析器。

### Q2: "转换"的含义与产物 {Completed}
- (A) 生成器；产物＝**带 csx 的效果预制体**（`EffectSnapshot` JSON，事件 handler 为模板生成的 csx 内联）。
- 优先级：**先做** "DSL →（模板）→ 带 csx 的 EffectSnapshot"。

### Q2a: 产物链层数 {Completed}
- **甲1**：DSL 为**独立可序列化的中间层**。
- 架构：① OrC 提供**模板效果**（主触发器固定，仅部分逻辑可变更）→ ② DSL＝模板 + 定制（handler/参数）→ 带参数 DSL 生成效果预制体 → ③ tokenizer + AST 从文本生成 DSL。
- 简化：**主触发器放在模板里**，DSL 可定制部分＝handler。

### Q3: DSL 定制内容形态 {Completed}
- **丙（混合）**：官方原语＝带参 op（声明式，解析器可产出）；另设 **csx 逃生舱原语**（本质也是带参原语，落地为**动态 handler + csx 注入**，由人工填，解析器不产出其 csx 内容）。

### Q4: 模板效果的形态与来源 {Completed（已修订）}
- 初定 **甲**：模板＝OrC 原生 `EffectSnapshot` 骨架，从完整预制体"挖空" handler。
- **修订（实现 grill 中途）**：改为**为模板效果设计专门的"模板效果预制体"格式 ＋ 显式 handler 槽位声明**，**不依赖**实际预制体（见 R4-Rev）。
- 槽位：**语义槽位名 + 映射**；DSL 只认槽位名。
- 位置：**`src/Orc.Game`**。
- 产物落点：生成 **`EffectSnapshot` JSON 文本**（可选落成 `*.prefab.json`）。

### Q5: 官方原语集范围与首批取舍 {Completed}
- **丙**：以 kards-diy 卡面语料**高频句式**驱动，**只纳入 OrC 已具备机制**，缺者走 csx 逃生舱。
- MVP：**1 个 trigger 骨架 + 3~5 个高频 op 模板**（如 `damage`/`draw`/`buff`/`grant`/`move`），端到端打通后按语料扩充。
- 约束：**本轮完全不碰 `src/Orc` 内核**；只生成文本，不装载/执行。

### Q6: 交付边界与解析器侧契约 {Completed}
- **甲**：本任务＝**全链分阶段**（先转换段，后解析段）。
- 输入来源：**kards-diy 卡池**（对拍语料）+ **`docs/初始设计/kards官方卡牌.json` 的 `text.zh-Hans`**（真实样本）+ 工具支持**手输**。
- 粒度：**AST 处理单效果边界**；**单牌描述可能含多个效果**（需切分）。
- 语法规则：**混合**——句式（结构规则）由官方（我们）维护；**词表外置**。

### Q7: 多效果切分规则与交付位置 {Completed}
- 切分语义：**甲＝沿用 kards-diy 口径**——`。` 硬边界（后句独立）；`；` 软边界（后句仍受前句触发/条件控制）；换行无权重；无触发词则**首句（至第一个逗号）为触发原语**。
- 产出聚合：**DSL 数组**（一牌多效果）；卡级字段/接线**后置**（"做完再接线"）。
- 交付位置：**`src/Orc.Game` 内新增命名空间**（非新工程）。

## 未决问题

### Q8: 落败处置与验收口径 {Completed}
- 落败处置：**甲**——**显式失败 + 诊断**（产出"未解析"记录：原文片段 + 失败原因，**不产出效果**），缺口可审计。
- 验收：**甲**——**语料驱动 + 往返一致**：kards-diy 卡池取 N 条为语料；断言"生成的预制体文本能被 `PrefabJson.Deserialize` 解析"＋覆盖率报告（先不设硬门槛）。

### Q9: 术语登记 {In Progress}
- 拟新增术语（待用户确认后写入根 `CONTEXT.md`）：`词法单元（token）`、`词法层（tokenizer）`、`语法树（AST）`、`效果解析器`、`效果 DSL`、`模板效果`。
- 沿用既有："卡牌数据体"（根 CONTEXT）、"效果快照/预制体"（review-chain CONTEXT）。

---

## 追加需求：指挥点事件改造（E1-25 后续） {Completed}

> **最终裁决（用户口径）**：
> 1. `指挥点也要同步改造，一个额外获得，一个额外失去＋一个设为和增减的共用入口`；
> 2. `明确尽量把动作缝逻辑放在前两个触发器里，而不是在通用入口破坏兼容性`；
> 3. `新增两个api(增加/失去)分别走两个hook，再来一个ChangePointsAsync给通用无语义的地方用，都走对应的hook`；
> 4. `明确卡效果走增加/失去hook，游戏内通用效果，比如打牌，指挥和反制走通用路径`。
>
> **落地形态**：
> - `GainPointsAsync`（卡效果·增加）→ `point.gained` → 共用 → `point.changed`
> - `LosePointsAsync`（卡效果·失去）→ `point.lost` → 共用 → `point.changed`
> - `ChangePointsAsync(player, value, kind∈{Set,Add})`（打牌/指挥/反制/回合设为）→ 只 `point.changed`
> - `AddPointsAsync` ＝ `ChangePointsAsync(Add)` 薄包装（原校验/数值语义保留）
> - 数字包裹判定器：`resource.point.gain` / `resource.point.lose`（默认恒等）
> - 解析侧：`获得 N 个指挥点` → `gainPoint`／`失去 N 个指挥点` → `losePoint`
> - **6 处归口**：`SettleAsync`(设为)／`OperateCosts.DeductAsync`(行动费)／`CommandCard`／`UnitCard`／`CounterCard`×2
> - 词表冲突处置：`指挥点` 由 `conditions.json`(Cond) 迁至 `filters.json`(Object)——"一字面一类别"。

> 来源＝用户中途口径原文：`指挥点也要同步改造，一个额外获得，一个额外失去+一个设为和增减的共用入口，明确尽量把动作缝逻辑放在前两个触发器里，而不是在通用入口破坏兼容性`。
> **性质＝需求变更/范围扩张**：原委托 `委托-指挥点槽事件改进.md` §7 明确把「点数（`player.Points`）的信号化」列为**不在范围内**（"`AddPoints` 保持现状；user 只要求**槽**"）。

### 基线事实（代码核对，含行号）
- **点数写入点共 7 处**，其中 **5 处为旁路直写**（不经 `ResourceManager` ⇒ 不会有任何信号）：
  | # | 位置 | 语义 |
  |---|---|---|
  | 1 | `src/Orc.Game/OperateCosts.cs:28` | 行动费扣除（`owner.Points -= Effective(unit)`） |
  | 2 | `src/Orc.Game/Cards/CommandCard.cs:111` | 指令卡打出扣费（有效部署费） |
  | 3 | `src/Orc.Game/Cards/UnitCard.cs:128` | 单位部署扣费（有效部署费） |
  | 4 | `src/Orc.Game/Cards/CounterCard.cs:119` | 反制激活扣点（有效部署费） |
  | 5 | `src/Orc.Game/Cards/CounterCard.cs:132` | 反制取消退点（按实扣额） |
- **受控面 2 处**：`ResourceManager.SettleAsync`（`Points = PointSlots`＝设为）、`AddPoints(player, amount)`（加值：≤0 拒绝、不钳制到槽、溢出防护、**不发信号**）。
- `AddPoints` 目前**无生产调用方**（仅测试用作"受控加值面"）。

### R1: 「设为与增减的共用入口」的形态与"兼容性"边界 {Completed}
- 用户口径拆解：**额外获得**（信号）＋ **额外失去**（信号）＋ **一个「设为」和「增减」的共用入口**；
  且"尽量把**动作缝**逻辑放在前两个里，**不在通用入口破坏兼容性**"。
- 选项：
  - **甲**：`AddPoints(player,int)` 签名与数值语义**完全不变**（仍拒 ≤0、不钳制、溢出防护），仅内部改走共用收敛点并**新增发 `point.changed`**（不发 gained/lost）；
    另加 `SetPointsAsync(player,int)`；额外获得/失去＝新方法（发 gained/lost → 走共用收敛点）。
  - **乙**：`AddPoints` **完全不动**（连信号也不发——纯遗留面）；共用入口**另开**（`SetPointsAsync` ＋ 增减方法）；
    额外获得/失去走新入口——`AddPoints` 成为"不经信号面的旁路"。
  - **丙**：单一方法 `ChangePointsAsync(player, int value, PointChangeKind kind)`（`Set` | `Add`）＝**唯一**共用入口；
    `AddPoints` 改为其**薄包装**（签名保留、行为等价）；额外获得/失去各为语义前置方法。
- 需裁决：**兼容性**指"签名不变"还是"信号面也不变"？

### R2: 5 处旁路直写是否纳入共用入口（"点数变化是否全覆盖信号"） {Completed}（裁决＝甲 全覆盖）
- 选项：**甲**＝全部 5 处改走共用入口（`point.changed` 全覆盖；**但**多为"费用扣除"，会引入新信号并可能影响部署/指挥流程的既有序列断言）；
  **乙**＝本轮**仅**收敛"设为/增减"入口（`SettleAsync` ＋ 新的加/减点方法），5 处费用旁路**保持直写**（信号面留洞、后续批次再收）。

### R3: 三条信号的命名与载荷 {Completed}（`point.gained`/`point.lost`/`point.changed`；`Amount`/`OldPoints`/`NewPoints`）
- 拟：`point.gained`（载荷 `Player` + `Amount`＝实际 Δ）／`point.lost`（同）／`point.changed`（`Player` + `OldPoints` + `NewPoints`）。
- 或统一资源族命名 `resource.point.*`（与判定器族 `resource.slot.*` 呼应）。

### R4: 点的「额外获得/失去」数字是否也各配"默认返回原值"判定器 {Completed}（采纳；`resource.point.gain`/`resource.point.lose`）
- 与槽对称（`resource.point.gain` / `resource.point.lose`）；判定器总数 19 → **21**（若采纳）。

### R5: `SettleAsync` 的 `Points = PointSlots`（设为）是否改走共用入口并因此发 `point.changed` {Completed}（采纳——走 `ChangePointsAsync(Set)`）
- 若发：回合开始段信号再 +1（`turn.start` 之后多一条 `point.changed`），**新一波受控变更**（回合开始序列断言再改）。

### R6: 效果解析侧是否本轮一并落地 `获得 N 个指挥点` → `gainPoint` / `失去 N 个指挥点` → `losePoint` {Completed}（采纳）
- 语料实测存在真实文本（`获得 1 个指挥点。`／`失去 1 个指挥点。`／`获得 2 个指挥点。`），当前落 `needsCsx` 兜底。
