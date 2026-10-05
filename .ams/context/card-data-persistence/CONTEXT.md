# 卡牌数据体持久化与加载 — 决策摘要

本域记录"卡牌数据体持久化与加载"任务的关键决策摘要。术语权威定义见项目根 `CONTEXT.md`；
需求与实现定稿见同目录 `requirements.md`、`implementation.md`（grill 过程见两份 grill 计划）。

## Decisions

- **数据体形态**（2026-10-05）：元数据（`id` / `name`）＋ `components` 组件定义集；明文 UTF-8、单卡一文件（`*.card.json`）、目录扫描；**相同项对齐官方语义**（`type` 为列表、`set` 不入体、`kredits`→部署费、`operationCost`→行动费）；Orc 额外项（`name` / `tags` / `inline` 效果的 `injects`）另加。
- **组件模型**（2026-10-05）：组件＝**定义**（实现"可反序列化组件"接口、自校验 fail-fast）＋ **loader**（装配到卡）；**静态注册面**注册序＝第一段顺序；**两段式**（第一段＝内置按注册序、第二段＝扩展/社区按数据体声明序）；构造期组件（`factionCost` / `battleStats`）在卡构造时装配；`CardDefinition` 的组件集为**唯一真源**，既有 12 参数构造与全部读面保留（内部派生）。
- **`RebuildFromPersistence` 退场**（2026-10-05）：其"持久化重建"职责由组件 loader 模型取代，扩展点删除；等价能力＝扩展组件 loader。
- **词条分流**（2026-10-05）：数据体写**官方英文标识**，经代码常量映射表转 `KeywordIds`；参值后缀（`heavyArmor1` / `intel2`）显式拆解；未实现项（`guard` / `shock` / …）进 `UnmappedAttributes` **只读留痕**（不 fail-fast、不写日志）。
- **效果接线**（2026-10-05）：数据体 `effects` 组件仅作**声明**（`prefabs` 引用 id 或 `inline` 内联文本）；内联＝**载入期分配/校验 id → 注册预制体 → 并入声明**（装载链因此只有"引用"一条）；引用时要求**预制体注册面先于卡加载就绪**。
- **注入落地**（2026-10-05）：`EffectPrefab.injects`（`{ target, band, event, priority }`）→ 装载链**框架侧**经 `Effect.InjectByFramework` 注入**宿主具名触发器**（宿主卡按名解析；本版仅支持默认区段）；`EffectSnapshot.SchemaVersion` **1 → 2**（旧版＝结构化失败）。
- **部署逻辑收敛**（2026-10-05）：废弃 A4 路径（`DeploymentLogicData` / `Registry` / `LoadContext` / `Rules` 与加载链步骤整体删除）；`UnitCard.DeployKeywordTrigger`（默认区段）取代之——部署链①段与"重放部署"共用同一触发器（**不发射链级信号**）；部署类效果经效果 ＋ `injects` 表达。
- **范围与验收**（2026-10-05）：不做导出方向、不做热重载、展示/本地化字段（`title`/`text`/`image`/`set`）不入体；验收＝构建 0 警告 ＋ `Orc.Tests` 298 / `Orc.Game.Tests` 707 / `Orc.Script.Tests` 9 全绿；官方导入脚本 `scripts/import_kards_cards.py` 产出 1646 张（`outputs/kards-cards/`；官方语料无效果字段）。
- **运行时组装保留**（2026-10-05）：`DynamicEffectFactory.Instantiate` → `AddEffect` / `MountPassiveEffects` 的手工路径与代码效果工厂路径均保留、语义不变。
