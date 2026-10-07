# 实现 Grill 计划 — 老兵与隐蔽（模板效果补全）

> 用途：记录**未解决**的实现问题（实现阶段）；每轮更新（新增、标记已决/失效）。
> 阶段：实现对齐（2026-10-06 启动）。需求规格已冻结于《需求文档.md》（S1-S10；N1-N3 按拟定执行）。
> 方式：逐场进行（每个实现主题一场，解决完一个再下一个）；每场产出＝设计结论＋落点＋验收（写入《实现文档.md》对应步骤）。

## 场次结构
- **场 E1**：老兵机制（升级执行体／触发器／信号／数据映射／读取面）
- **场 E2**：隐蔽机制（状态承载／判定器豁免／揭示／例外改写）
- **场 E3**：解析层（词法／模板效果／op／语义映射／覆盖率样本）
- **场 E4**：验收与实施拆分（样本口径、回归、委托链）

## 勘验中（已委托 CodeAnalysis ×3，2026-10-06）
- A1 老兵机制勘验：组件替换落点／触发器跨卡调用与自托管先例／信号新增步骤／数据映射／读取面／风险。
- A2 隐蔽机制勘验：单位状态承载／判定器调用点与 moding／『效果→单位集合』路径全集／攻击时点／揭示流程／观察面。
- A3 解析层勘验：Lexicon 扩展与测试约束／监听映射／『揭示：』前缀处理／模板效果 schema／op 占位符／runtime 连接／覆盖率样本。

## 未决问题清单（勘验回来后逐场细化）

### 场 E1（老兵）【勘验 A1 完成 ✅ 2026-10-06】
**关键现状**：
- 老兵全库零实现；数据 `BecomesVeteran:`≥37 / `VeteranOf:`≥35 成对（现归留痕）。
- 组件替换唯一硬缺口＝**战斗数值基准替换/受控写面**（容器无 ReplaceData；`BattleStatsData` 语言级只读＋测试断言"只读基准不变"）。两陷阱：①攻/行动费链起点读 `UnitStateData`（仅换 battleStats 不达标）；②效果 `otherTriggers` 无寻址面（"专属触发器"改用**卡侧具名主动触发器**先例）。
- 词条集：`ClearAllExceptAsync` 强制保留位（无全清）；替换非原子（半态风险）。
- 落层建议：UnitCard 门户 `PromoteToVeteranAsync`（或 VeteranRules 服务）＋受控写面（仿 S10 FactionCostData 先例）＋复用清损伤/清修饰/词条原语＋单次 RequestRerunAsync＋新信号（5 步＋2 处计数同步）。
- 兼容断言 4 处：KeywordBatch2Tests:73（15→16）、GameHooksTests:194（29→30）+:258（24→25）、CardDataBodyTests:140-148 改写。
**自裁（将记录于实现文档，可复核）**：
- 信号名：`unit.upgraded`（升为老兵）／`unit.revealed`（揭示）——与解析层 hooks 对齐后冻结。
- BattleStats 受控写面（B 路线，S10 先例）＋ UnitStateData 攻/行动费受控刷新路径。
- "专属触发器"＝卡侧具名主动触发器（RegisterNamedTrigger 先例）。
- 承载形态方向：数据加载层识别 `BecomesVeteran:`/`VeteranOf:` → 卡定义新字段（细节由实现者细化申报）。
**待用户裁定**：E1-Q1 升级处置口径（词条集/修饰清空范围＋运行时授予）。

### 裁定队列（2026-10-06 第 1 批）【全部已决 ✅】
- E1-Q1 升级处置口径【已决：a 全清口径】——损伤清零＋修饰器全清（含永久型）＋词条集全清（含运行时授予）→ 换新。
- E2-Q3 揭示时点【已决：a 伤害结算前揭示】。
- E3-Q2 「使其/对其」回指缺口【已决：b 列申报、本批验收避开】。
- E4-2 代表场景【已决：照推荐 V1-V5＋C1-C4、C5、C6、C9；V6 申报】。
- 其余自裁技术决定【用户同意】：信号名 `unit.upgraded`/`unit.revealed`；受控写面路线；"专属触发器"＝卡侧具名主动触发器；「揭示」双角色方案 B；例外请求级形态；钳击配对剔除隐蔽。

### Q-E4-3 实施拆分方案【已批准：开工 2026-10-06】
- S1（老兵机制）→ S2（隐蔽机制）→ S3（解析层）**三单串行**；每单 CodeImplementation 启用 grill。
- **S1 已完成 ✅（2026-10-06）**——新增 12 测试（V1-V5＋清空/失败/映射专项）＋四套件 1279 全绿；构建 0 错误。契约冻结（供 S3）：『老兵触发器』名＝`VeteranRules.TriggerName`；发动面＝`UnitCard.InvokeVeteranTriggerAsync`；csx 入口＝`EffectRuntime.UpgradeAsync`；『老兵』标记＝`KeywordIds.Veteran`（词条 15→16）；读取口＝`VeteranRules.IsVeteran`；三态＝`VeteranPromotionResult`（Promoted/AlreadyVeteran/Rejected 六类原因）。遗留：`AstBuilder.cs:231` CS0219 在途警告（EffectParsing，S3 处理）；「类型/费用不同的老兵对」「光环/其它非词条承载的清空范围」＝边界申报。
- **S2 已完成 ✅（2026-10-06）**——隐蔽标记（词条 16→17）／判定器接线（生产内置段＋默认剔除＋请求级覆盖＋moding 口）／剔除点（无头选靶・光环・钳击）／揭示服务（结算前、攻击者先）／unit.revealed（信号 30→31）；新增 20 测试＋四套件 1300 全绿。S3 对接冻结项见实现文档。批外登记：『获得隐蔽』通用授予通路＝批外；『处于隐蔽回合数』＝效果层自管。
- **S3 已完成 ✅（2026-10-06）**——词法／模板效果（29→31）／op（upgrade・reveal）／『揭示：』方案 B／AstBuilder 警告消除；新增 15 测试＋四套件 1315 全绿；构建 0 警告 0 错误。
- **批次收官（S1-S3）✅（2026-10-06）**：总验收通过；待申报汇总见《实现文档》；下一阶段＝词条效果化批 0（已发起）。

### 场 E2（隐蔽）【勘验 A2 完成 ✅ 2026-10-06】
**关键现状**：
- covert 承载候选：**A 词条标记（推荐**——`CardAttributeMap.Exact`＋`KeywordIds`＋`KeywordRegistry` 三处小改；先例：被压制/烟幕/`SuppressRules`）；B `UnitStateData` 字段（与"效果层自管 handler"规格不吻合）。
- **接线落差**：`targeting.candidate.eligibility` 生产零调用（仅测试经 `JudicatorSelectionRule` 包装接入 Targeter；真实目标选择流程＝粗筛 allowedSet）——"指令效果索敌统一走判定器"属**未接线**，需新接线（接入形态：调用点按 AsFilter 包装或新收口）。
- 剔除点：**必须改**＝①无头选靶链（`EffectRuntime.SelectAsync`→`effect.target.resolve`，8 op 共用；保留 self 分支/覆盖 random 顺序）②光环受益收集（`CollectAuras` 单点收口）；**不必改**＝攻击候选/单目标动作/修饰链；**待裁定**＝钳击配对候选。
- moding＝纯替换（无 next）；单卡例外三选一：整体更换规则／请求级组合／窗口模式（R2）。
- 揭示挂钩＝`HandleUnitAttackAsync` 入口（推荐 A；HQ 目标不揭示）；揭示时点（伤害前 vs 战斗后）待裁定（R4）。
- 揭示落点＝新服务 `RevealAsync`（SuppressRules 型）＋UnitCard 具名"揭示触发器"（部署词条触发器先例）＋新信号（5 处同步：GameUpdates/GameHooks/GameHooksJson/两个计数断言测试）。
- 观察面＝段轮询/即时监听自动覆盖（新信号发射点位于动作作用域内即满足）。
**待裁定（将逐轮对齐）**：
- E2-Q1 隐蔽状态承载：选 A（词条标记）？【推荐 A】
- E2-Q2 指令索敌接线形态＋剔除收口（含单卡例外形态——整体更换／请求级组合／窗口）【R1/R2/R3】
- E2-Q3 揭示时点（伤害前揭示 vs 战斗后揭示）【R4】

### 场 E3（解析层）【勘验 A3 完成 ✅ 2026-10-06】
**关键现状**：
- 管线：词法（最长匹配）→ Segmenter（仅 。！？；切分）→ AstBuilder（Named＝首 token 具名触发词＋冒号；Listen＝时/后后缀）→ SemanticMapper（事件模板表/监听链/守卫）→ EffectCompiler。
- **「揭示」双角色（硬约束：字面唯一类别）**：推荐**方案 B＝actions.json（Action）＋扩展 `DetectTrigger` 支持「动作词＋冒号」前缀＋EventTemplates 加 `reveal_basic`**——否则「揭示：」前缀**静默误归 deploy_basic**（现状）；必须同批扩展（只加动作不扩展会恶化语义）。
- 「升为老兵时」监听：`ResolveListenTemplate` 新分支＋`OwnerFilterableTemplates` 白名单（"友方单位升为老兵时"需归属守卫）。
- 模板效果：新 2 个（`veteran_basic` / `reveal_basic`）；hooks 加载不校验信号但须与游戏层信号名一致；模板计数 29→31。
- op：新 2 个（upgrade / reveal）＝csx.tpl＋DslOpRegistry＋runtime API 三件套（csx 实编译测试强制 API 存在）。
- 同步清单（8 项）：动作键白名单、模板计数、op 清单、信号计数（29→31）、监听体代词回指等。
- 覆盖率现状：结构 74.6%／语义 52.2%；基线样本①-⑩（⑧「揭示：」静默错误重点防恶化）。
**待裁定（将逐轮对齐）**：
- E3-Q1 「揭示」双角色方案：选 B（推荐）？【openDecision】
- E3-Q2 「使其/对其」载荷回指缺口：本批补 or 列申报？（老兵/隐蔽多样本依赖；当前解析为 sel=self 打宿主）【openDecision】
- E3-Q3 新信号定名（unit.*）与模板 hooks 对齐——随 A1/A2 信号设计一并定（实现细节，拟与用户确认一次）。

### 场 E4（验收/拆分）
- Q-E4-1 验收就绪定义【已答：a】——机制级端到端代表场景＋卡池其余文本清点申报。
- Q-E4-2 代表场景草案（请增删）【待答】
  - 老兵：V1 在场回合数升级链／V2 对敌总部伤害→升级＋升级时效果／V3 友方升为老兵监听／V4 老兵筛选／V5 对战存活升级／V6 完全修复升级（依赖 G22 信号→建议申报，不入验收）。
  - 隐蔽：C1 被指令指向拒绝／C2 被攻击揭示／C3 主动攻击揭示／C4 移动不解除／C5 「揭示：若是友方回合+2攻」／C6 「部署：揭示1个隐蔽单位」／C7 友方隐蔽被揭示监听／C8 隐蔽筛选（-1花费）／C9 例外（可指向）。
