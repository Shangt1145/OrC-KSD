# Requirements — 卡牌数据体持久化与加载

把"卡牌数据"的真源从纯代码注册扩展为可持久化的数据体（文件形态），并定义其加载逻辑，使卡牌数据可离线编辑/分发/载入对局。

> 状态：**已对齐（需求 grill 收束）**。裁决过程见 `requirements-grill-plan.md`。

## Scope

- Includes:
  - 卡牌**数据体格式**：一段元数据 ＋ `components` 组件定义集（明文 UTF-8）。
  - **组件 loader 架构**：游戏层静态注册面（组件类型名 → loader），卡牌加载器两段式调用。
  - **载入**：目录扫描、单卡隔离与报告、装配顺序、卡牌库/效果预制体注册面的生成。
  - **官方语料导入脚本**：官方 `kards官方卡牌.json` → Orc 自定义格式（单向）。
  - **部署逻辑收敛**：废弃 A4 路径；"部署词条"改为独立主动主触发器 ＋ 部署链注入；"重放部署"＝直接再调该触发器。
- Excludes:
  - 展示/本地化字段（多语言 `title`/`text`、`image`、`set`）——不入机制数据体。
  - 对局存档、对局级卡牌 ID 水位线、实例运行态组件（`UnitStateData`/`CommandData`）、卡组构筑数据（`PlayerDeckConfiguration`）。
  - **数据导出方向**（运行时对象 → 数据体文本）——本批不做。
  - **热重载**（文件监控自动重载）——本批不做。

## Constraints

- **不得取消运行时组装路径**：代码侧"先实例化效果预制体、再独立挂载"（`DynamicEffectFactory.Instantiate` → `AddEffect`/`MountPassiveEffects`）与代码效果工厂路径均保留（测试不必套预制体实例化）。
- **允许构造期加载组件**：`FactionCostData`/`BattleStatsData` 的构造期装配方式保留，组件 loader 可在构造期被调用。
- **装配顺序硬约束**：引用效果预制体 id 时，**效果预制体注册面必须先于卡牌加载/构造就绪**。
- 沿用既有持久化口径：明文 UTF-8、`schemaVersion` 强校验不降级、单卡隔离 + 报告。
- 工程红线：**待确认**（Q27：`src/Orc` 必须改动——`TriggerPrefab` 新增 `inject` 字段）。

## Requirement Items

### R1: 卡牌数据体的范围与用途
- Status: Confirmed
- Scenario/Trigger: 需要离线编辑/分发/载入一张卡（或一个卡包）时。
- Behavior: 数据体自包含描述一张卡＝元数据 ＋ 组件定义集；明文 UTF-8，供构建期产出与社区手工编辑；**单一路径**（无代码 `effectId` 混排、无部署逻辑段）。
- Acceptance:
  - [ ] 一张可从数据体完整还原的卡能装配进对局并正常工作。
  - [ ] 文件为 UTF-8 明文、可手工编辑。
- Terminology: 卡牌定义、效果预制体

### R2: 卡牌声明效果预制体（引用或内联）
- Status: Confirmed
- Scenario/Trigger: 加载一张卡时，其声明的效果被装载。
- Behavior: 卡的数据体可**引用**效果预制体 id（`prefabs: [id]`，要求预制体注册面先就绪），也可**内联**效果预制体文本（自包含，单文件随卡分发）；效果预制体的主触发器声明 `hooks`（挂总线）与 **`inject`（注入目标触发器/band，框架侧执行注入）**。
- Acceptance:
  - [ ] 一张卡同时支持"引用预制体 id"与"内联预制体文本"两种写法，且都能装载成功。
  - [ ] 引用写法下，预制体未注册＝单卡隔离记录（不崩）。
  - [ ] 代码侧两条既有路径（代码效果工厂 + 运行时组装）不受影响。
- Terminology: Hook、效果预制体

### R3: 全量加载语义
- Status: Confirmed
- Scenario/Trigger: 对局开始加载卡组内全部卡牌时。
- Behavior: 卡组内所有卡牌的效果（卡直接声明的 ＋ 派生的触发器/handler）全部加载——"加载"＝实例化＋登记＋**被动挂载**；**主动效果仅登记、施放时调用**（维持现状语义）。
- Acceptance:
  - [ ] 卡组内每张卡的被动效果均已挂载、主动效果已在容器中可施放。
  - [ ] 装载顺序＝声明序。
- Terminology: —

### R4: 对局中动态加载
- Status: Confirmed
- Scenario/Trigger: 对局进行中需要加载新的效果预制体/卡牌数据时。
- Behavior: 允许 ①运行期注册新效果预制体、②运行期创建新卡、③**给已有卡追加效果预制体**；**hook 不重放**——已错过的 hook 不回填（动态装载的效果可能永不被激活，明确接受）。
- Acceptance:
  - [ ] 三种动态加载对象均有可用通路（③不被"同卡重复声明＝拒绝"挡住）。
  - [ ] 对已错过的 hook，不触发历史回放。
- Terminology: —

### R5: 效果预制体注册面的就绪前提
- Status: Confirmed
- Scenario/Trigger: 卡装载时解析其效果声明。
- Behavior: 装配约定＝**预制体注册面先全量就绪、再逐卡加载**；未命中仍按单卡隔离记录处理（不中断整批）。
- Acceptance:
  - [ ] 正式装配路径下，卡引用的预制体在卡加载前已注册。
  - [ ] 未命中＝隔离记录且不阻断其它卡。
- Terminology: —

### R6: 数据体 schema 取向
- Status: Confirmed
- Scenario/Trigger: 定义数据体的字段名与字面值。
- Behavior: 自定义格式、**相同项对齐官方语义**（字段名与字面值取官方）；`type` 为**列表**；`set` 不入体；Orc 额外项另加；官方语料后续用脚本全量导入。
- Acceptance:
  - [ ] 字段名/枚举字面值与官方一致（`faction`/`rarity`/`type`/`kredits`/`operationCost`/`attributes`）。
  - [ ] 官方语料经脚本可全量产出数据体文件。
- Terminology: 卡牌定义

### R7: 官方语料中"项目未实现的词条"的处置
- Status: Confirmed
- Scenario/Trigger: 脚本导入官方语料（`guard` 124、`shock` 32、`bond` 26、`alpine` 18、`covert` 11、`salvage` 9、`heavyArmor2` 11、`OnlySpawnable` 44、`BecomesVeteran:`/`VeteranOf:` 等）时。
- Behavior: **保真 + 载入期分流**——数据体原样保留官方 `attributes`；载入时按"已实现清单"分流（命中→词条声明；未命中→留痕，不 fail-fast）。`guard` 一视同仁（不特别映射 `isGuard`）。词条本批**只迁移已实现的**。
- Acceptance:
  - [ ] 官方全量语料导入后，载入不因未实现词条中断。
  - [ ] 未实现项有可查留痕。
- Terminology: —

### R8: 数据体＝元数据＋组件集；组件 loader 架构
- Status: Confirmed
- Scenario/Trigger: 加载一张卡时。
- Behavior: 数据体＝元数据 ＋ `components` 组件定义数组；游戏层以**静态注册面**登记"组件类型名 → 组件 loader"；卡牌加载器**两段式**调用：第一段按游戏层 loader 定义顺序（防依赖丢失），第二段对剩余组件按数据体声明序；可入数据体的组件实现**"可反序列化组件"接口**（参照 `ISerializableEffect` 先例）；**允许构造期加载组件**。
- Acceptance:
  - [ ] 社区自定义组件（不在游戏层清单内）可按数据体声明序加载。
  - [ ] 同一数据体内重复声明同一组件＝拒绝。
  - [ ] 未知组件类型＝隔离记录。
- Terminology: 组件 loader、"可反序列化组件"接口

### R9: 可入数据体的组件清单
- Status: Confirmed
- Scenario/Trigger: 社区/编辑器编写数据体时决定可声明哪些组件。
- Behavior: 第一段（游戏层有序清单，依赖顺序）：
  | 序 | 组件名 | 字段（对齐官方） |
  |---|---|---|
  | 1 | `factionCost` | `faction`, `kredits`（＝花费/部署费） |
  | 2 | `battleStats` | `operationCost`, `attack`, `defense`（仅单位卡） |
  | 3 | `tagData` | `rarity` |
  | 4 | `typeCategory` | `type`（列表；承载 `category` 与 `unitTypes`） |
  | 5 | `keywords` | `attributes`（官方英文标识） |
  | 6 | `effects` | `prefabs`（引用 id 或内联预制体文本） |
  元数据段：`schemaVersion`、`id`、`name`（取官方 `title["zh-Hans"]`）、`tags`（Orc 开放 tag，独立于组件或并入 `tagData`，待实现确认）。
- Acceptance:
  - [ ] 各组件名与字段确定；非单位卡无 `battleStats` 为正常状态。
  - [ ] 第一段顺序＝依赖契约。
- Terminology: 组件 loader

### R10: 文件组织、载入入口与装配关系
- Status: Confirmed
- Scenario/Trigger: 对局装配前载入卡包。
- Behavior: **单卡一文件（`*.card.json`）＋ 目录扫描**（对齐 `PrefabManager.LoadDirectory`）；宿主在对局外载入后把结果传给 `Match`（对齐现状构造注入 `cardDefinitions`，`Match.cs:84`）；载入**生成** `CardLibrary`/`CardEffectRegistry`/`PrefabManager` 的注册项（不建平行装配路径、避免双真源）。
- Acceptance:
  - [ ] 目录扫描可产出"卡定义集 + 预制体注册"，传入 `Match` 后对局正常运行。
  - [ ] 无第二套装配路径。
- Terminology: —

### R11: 校验与失败策略
- Status: Confirmed
- Scenario/Trigger: 载入数据体文件时。
- Behavior: 沿用 `schemaVersion` 强校验（不符＝失败，不静默降级）；**单文件隔离 + 报告**（对齐 `PrefabLoadReport`）；单卡内重复组件＝拒绝；未知组件类型＝隔离记录；枚举字面值经 `Enum.IsDefined` 校验 fail-fast。
- Acceptance:
  - [ ] 坏文件不阻断整批，报告可查。
  - [ ] 版本不符＝结构化失败（不降级）。
- Terminology: —

### R12: 部署逻辑收敛（废弃 A4）
- Status: Confirmed
- Scenario/Trigger: 单位部署时，或需要"重放部署效果"时。
- Behavior:
  - **废弃 A4 路径**（`DeploymentLogicRegistry`/`DeploymentLogicData`/`DeploymentLogicLoadContext`/`CardBase.GenerateDeploymentLogic` 步骤/部署链①段）。
  - **"部署词条"＝独立主动主触发器**（带卡牌/效果参数）；打出单位的链路里加 handler 触发它；该 handler 的注册经**效果预制体声明的 `inject`（注入目标）**、由装载链框架侧执行。
  - **"重放部署"＝再次直接调用同一主动触发器**（定向、不广播信号），入口沿用 `RetriggerSystem`/`RequestDeployAsync`。
- Acceptance:
  - [ ] 部署效果经效果 + `inject` 表达，数据体可持久化。
  - [ ] 重放部署走直接调用，不发射 `unit.deployed`（不惊动其他监听者）。
  - [ ] A4 相关类型/步骤/测试完成迁移，无遗留引用。
- Terminology: 注入（Inject）、主动触发器

### R13: 范围排除
- Status: Confirmed
- Scenario/Trigger: 界定本需求边界时。
- Behavior: 排除——对局级卡牌 ID（水位线）、实例运行态组件（`UnitStateData`/`CommandData`）、卡组构筑数据、数据导出方向、热重载。
- Acceptance:
  - [ ] 上述项无实现项落入本批。
- Terminology: —
