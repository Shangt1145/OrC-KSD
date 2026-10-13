# Requirements — 前端迁移至引擎新资产模型

把前端（Godot 项目 `D:\Godot-orcKsd-main`）基于旧静态游戏层自造的卡包/资产子系统，迁移到引擎（OrC 工作区 `d:\OrC-KSD`）的新资产模型之上，并把"UI 投影"与"卡包词条加载"下沉为**引擎侧插件（后端 `.ui`）**，使前端仅"拉取投影响应"；同时补齐引擎侧的运行期卡注册、词条覆盖、卡包生命周期等能力。

## Scope

- Includes:
  - 中立契约（view DTO）上移到后端 `.ui`。
  - 引擎新增 cardpack → 卡数据 + 效果装配管道；卡包生命周期（启用集/多版本/热加载/卸载/重载/回滚/快照）归 `EngineHost`。
  - 运行期卡注册 API（引擎级 + 对局级）与运行期效果声明面。
  - 词条：json 资产承载 + 插件加载 + 覆盖语义 + 对局快照 + 封存运行期注销。
  - 后端 `.ui` 插件包：整盘投影提供者 + 词条加载；宿主新接口面与包注入。
  - 前端 `bridge` 适配；删除 `PackSystem`/`PackLibrary`。
  - 生成器 `tools/build_builtin_packs.py` 改造为新格式。
  - 缺陷报告 + 交接报告（落**前端仓库**）。
  - 术语「投影」「投影提供者」登记入 `CONTEXT.md`。
- Excludes:
  - 卡包管理 UI（导入/启停/删除）——**交前端开发者**，本期只做引擎 API。
  - `proto/ui/**` 的表现/交互重做（除 `proto/ui/bridge/OrcMatchRunner.cs` 必要适配）。
  - 一次性"旧 `pack.json` → 新 `manifest.json`"迁移脚本（旧源码在他人手中，直接给干净终态）。
  - 真内置包数据生成（`artifacts/kards-api-all-cards-*.json` 缺失）。
  - asm 加载器（本期不走 asm 载体）。

## Constraints

- 尽量不动前端**逻辑代码**：冻结 `proto/ui/screens/**`、`proto/ui/components/**`、`proto/ui/anim/**`；例外 `proto/ui/bridge/OrcMatchRunner.cs`。
- 功能缺陷**只写报告**、不直接修（管理 UI 缺失等）。
- **前端改动清单必须逐项写入报告**（改了哪些文件、改了什么、为什么）。
- 引擎不引入 UI 概念进内核最小面（走扩展上下文接口先例）。
- 直接产出**干净终态**、不留兼容层/双真源。
- 术语以引擎根 `CONTEXT.md` 为准；显式推翻交接报告 §8.1「Selectors/UI 契约不迁移」。
- 验证用合成卡包样本（真数据缺失）。

## Requirement Items

### R1: 中立契约（view DTO）上移后端
- Status: Confirmed
- Scenario/Trigger: 后端插件需要构造前端依赖的 view 对象时。
- Behavior: `UiCardDefinition`/`UiCardView`/`UiMatchView`（含 `Visibility`/`DtoKind` 等必要枚举）迁至后端 `.ui`；前端与插件共同引用；**形状不变**。
- Acceptance:
  - [ ] `proto/ui/**`（冻结区）零改动即可编译。
  - [ ] 引擎/插件不依赖前端工程（无反向依赖）。
- Terminology: 投影

### R2: 引擎维护卡数据（cardpack 管道）
- Status: Confirmed
- Scenario/Trigger: 加载/新建对局时。
- Behavior: 引擎从 cardpack 的 `assets[]` 产出「卡定义集 + 内联效果预制体 + 效果声明 + 效果库」，并暴露"快照 →（定义集 + 效果装配物）"读面。
- Acceptance:
  - [ ] `Orc.Packages` 具备 cardpack → 卡数据的装载能力。
  - [ ] 前端无需自跑 `CardDataLoader`/`CardEffectRegistry`/`PrefabManager` 装配。

### R3: 卡包生命周期归引擎
- Status: Confirmed
- Scenario/Trigger: 启用/停用/热加载/卸载/重载/回滚卡包。
- Behavior: `EngineHost` 持有启用集与多版本；卡包热加载默认只对新对局生效；**重载允许替换，但禁止在对局进行中重载**。
- Acceptance:
  - [ ] 对局创建用 `AssetCatalogSnapshot`（版本锁定、对局内不可变）。
  - [ ] 进行中对局拒绝重载（明确失败）。

### R4: 前端对局漏斗适配（仍 `new Match`）
- Status: Confirmed
- Scenario/Trigger: 建立对局。
- Behavior: 前端仍构造 `Match`，但定义集与效果装配物取自 R2 的引擎读面；前端不再持有 `PackSystem.Enabled`。
- Acceptance:
  - [ ] `OrcMatchSession` 不引用 `PackSystem`/`PackLibrary`。
  - [ ] "注册先于卡加载""挂载复验"不变量保持。

### R5: 运行期卡注册 API + 运行期效果声明
- Status: Confirmed
- Scenario/Trigger: 对局中/引擎运行期需要新增可用卡牌。
- Behavior: 后端 API（不限调用者）支持**引擎级 + 对局级**注册新卡定义；**只追加不覆盖**；分配新 ID（判为"构筑之外"）；同时提供**运行期效果声明**；卡包**重载**仍按包粒度整包替换（与"只追加"不冲突）。
- Acceptance:
  - [ ] 同 id 重复注册＝拒绝（运行期 API）。
  - [ ] 新增定义可经 `MatchCardService.CreateAsync` 造卡放置。

### R6: 词条承载与动态注册
- Status: Confirmed
- Scenario/Trigger: 卡包启用/热加载成功后；对局开始。
- Behavior: 卡包以 json 资产声明词条，**由 `.ui` 插件加载**（非引擎硬编码）；仅追加与**覆盖**、**不允许注销**（注销需冷重启）；**仅对新对局生效**——对局开始**冻结词条快照、引用移交 `Match`**，覆盖只作用于引擎持有引用、与对局快照隔离。
- Acceptance:
  - [ ] `KeywordRegistry` 支持覆盖、运行期注销被封存。
  - [ ] 进行中对局的词条行为不受后续覆盖影响。

### R7: 投影插件（整盘、每帧、运行期隔离）
- Status: Confirmed
- Scenario/Trigger: 前端每帧渲染。
- Behavior: 后端 `.ui` 内置插件（代码入口包）提供 `UiMatchView Project(Match match, Player viewer)`；前端 bridge 持 provider 句柄每帧调用；投影抛错＝运行期隔离（保留上一帧 + 记录错误），不炸对局。
- Acceptance:
  - [ ] `OrcCardReader`/`OrcMatchReader` 的投影逻辑在后端插件内。
  - [ ] art 路径由前端经 provider 构造参注入。
  - [ ] `OrcUpdateTranslator` 仍在前端。

### R8: 宿主新接口面与包注入
- Status: Confirmed
- Scenario/Trigger: 插件注册/激活；卡包 load/unload/reload。
- Behavior: 新增扩展上下文接口（投影 provider 单个注册 + 卡包生命周期回调，成功与失败均回调）；`EngineHost` 支持注入代码入口包（加参数，不开第二路径）。
- Acceptance:
  - [ ] 内核最小面（`IPluginRegistrationContext`）未被扩 UI 概念。
  - [ ] `.ui` 代码入口包可进入 `EngineHost` 冷加载。

### R9: 数据格式迁移与生成器改造
- Status: Confirmed
- Scenario/Trigger: 产出/读取卡包目录。
- Behavior: 卡包清单为引擎 `manifest.json`（`assets[]` 声明 cards/effects/keywords）；`tools/build_builtin_packs.py` 改为产新格式；验证用合成样本。
- Acceptance:
  - [ ] 前端不再解析 `pack.json`。
  - [ ] 生成器产物可被 `EngineHost` 发现并装载。

### R10: 前端清理与冻结
- Status: Confirmed
- Scenario/Trigger: 交付。
- Behavior: 删除 `PackSystem`/`PackLibrary`；文本→效果能力上移后端；前端只留 DIY 契约；`proto/ui/screens|components|anim` 冻结。
- Acceptance:
  - [ ] 前端无 `pack.json`/`packs.json` 相关代码。
  - [ ] 冻结区文件除 `OrcMatchRunner.cs` 外无改动。

### R11: 报告
- Status: Confirmed
- Scenario/Trigger: 交付时。
- Behavior: 产出**缺陷报告**与**交接报告**，均落**前端仓库**。
- Acceptance:
  - [ ] 覆盖基线已识别缺陷（附录）。
  - [ ] 交代引擎 API 用法（卡包管理 API 交前端开发者）。

### R12: 术语登记
- Status: Confirmed
- Scenario/Trigger: 交付时。
- Behavior: 「投影」「投影提供者」登记入根 `CONTEXT.md`；显式记"推翻 §8.1"。
- Acceptance:
  - [ ] `CONTEXT.md` 含两条新术语且不与既有冲突。

## 附录：基线已识别缺陷（写入缺陷报告）

1. `PackSystem` 进程级全局 `Enabled`+`KeywordOwner`+全局 `KeywordRegistry` 注册，与引擎宿主隔离/快照语义冲突。
2. `PackLibrary`（导入/启停/删除）零生产调用；承诺的 `PackScreen.cs` 不存在——无卡包管理 UI。
3. `Main.Diy.ReloadPacks` 两趟扫描中途失败脏化进程词条表且不回滚。
4. 文本解析双份实现（`PackCardTextParser` ↔ `tools/build_builtin_packs.py`）易漂移，漂移＝加载期硬失败。
5. `DiyCardCompiler.SetPackKeywords` 只在 `PackLibrary.Reload` 调用，生产内置包路径不调用——DIY 词汇表注入不对称。
6. `PackSystem.Load` "重载替换"在完整校验前先 `RemoveAll`，失败留中间态。
7. 生产对局隐式依赖启动副作用（`ReloadPacks` → 进程全局 → `CreateNativeAsync` 兜底）。
8. `"pack/"` 前缀约定硬编码散布多处。
9. 内置包数据目录不在仓库（依赖外部 dump + 生成脚本）。
