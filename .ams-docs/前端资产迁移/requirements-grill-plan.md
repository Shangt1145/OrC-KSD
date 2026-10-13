# Requirements Grill Plan — 前端迁移至引擎新资产模型

> 聚焦：**前端（`D:\Godot-orcKsd-main`）卡包/资产子系统迁移到引擎新资产模型**；约束＝尽量不动前端逻辑代码、功能缺陷只写报告。
> **范围（2026-10-13）**：从"只改造前端"扩为"**前端 + 引擎补丁**"。

## 基线事实（已核对代码，勿再问用户）

- 前端自造：`PackSystem.cs`、`PackLibrary.cs`（零生产调用）；前端分层 `contracts` ← `core` ← `bridge`(唯一持引擎类型) ← `ui`(Godot，零引擎 using)。
- 对局漏斗：`OrcMatchSession.CreateFromCatalogAsync`(:103) → `CreateNativeAsync`(:194，注册/挂载 :228-240)。
- 引擎：`manifest.json`/`EngineHost`/`PackageActivation`；无 `CardDataLoader`、无 cardpack→卡管道、无"投影回传"通道。
- 插件硬约束：入口固定 `Action<IPluginRegistrationContext>`；csx 白名单不含前端契约程序集；**`asm` 无加载器**；内置包＝"合成 asm 描述符 + 本程序集代码入口"。
- `KeywordRegistry`：进程级静态；重复注册拒绝；绑定一次性；`Unregister` 连带清绑定。
- 卡数据：`CardLibrary.Register` ＝"装配期"语义 + 同 id 重复拒绝；`Match` 装配时一次性注册；`MatchCardService.CreateAsync` 已能按已注册定义造卡放置；`RegisteredDefinitions` 只读；ID 水位线 ⇒ 装配后新实例＝"构筑之外"。
- 既有先例：`CardPackInjection.InjectEffectsAsync`+`ICurrentMatchInjector`（热加载卡包效果注入当前对局，已设计未实现）。
- 现成面：`IGlobalRegistrationServices.RegisterKeyword`/`DeclareKeywordEffectBinding`。
- 交接报告 §8 未做 UI；§8.1 `Selectors` 不迁移（**本任务显式推翻**）。
- `artifacts/kards-api-all-cards-*.json` **不在前端仓库** ⇒ 本期无法真产内置包数据。

## 已定决策

- **D1**：引擎包插件形态；引擎与前端都可改。引擎维护卡数据；插件从卡生成投影；前端仅拉取 view。
- **D2**：**整盘只读投影**（插件产 `UiMatchView`）；输入＝`Card` 实例；**每帧拉取**；DTO 上移中立契约程序集。新增术语「投影」「投影提供者」。
- **D3**：卡包生命周期归 `EngineHost`；效果装配随包进引擎；前端不再持 `PackSystem.Enabled`；前端仍 `new Match`（引擎暴露"快照→(定义集+效果装配物)"读面）；**只做引擎 API**，卡包管理 UI 交前端开发者；卡包词条＝json 资产，"加载词条"放插件。
- **D4**：载体＝**内置插件包 + 本程序集代码入口**；插件与中立契约都在**后端 `.ui` 程序集**；**一个包**兼投影 + 词条加载。
- **D5（词条）**：仅追加不注销；仅对新对局生效（对局开始冻结词条快照、引用移交 `Match`）；注销需冷重启，运行期只允许覆盖引擎持有引用（与 `Match` 快照隔离）。⇒ 动 `KeywordRegistry` + `Match` 持快照。
- **D6（Q7）**：provider 签名＝`UiMatchView Project(Match match, Player viewer)`；前端 bridge 持句柄每帧调；match 相注册；抛错＝运行期隔离；`OrcCardReader`/`OrcMatchReader` 搬入后端插件，art 由前端经构造参传入，`OrcUpdateTranslator` 留前端。
- **D7（Q8）**：通用运行期卡注册 API（不限卡包）：**引擎级 + 对局级**；**只追加不覆盖**；分配新 ID（算"构筑之外"）；**同时支持运行期效果声明**；后端 API、不限调用者。
- **D8（Q9）**：双仓库改造（引擎 `d:\OrC-KSD` + 前端 `D:\Godot-orcKsd-main`）；改生成器 `tools/build_builtin_packs.py` 产新格式；**报告落前端仓库**；**直接产出干净终态、不留兼容层**；真内置包数据不生成（dump 缺失），用合成样本验证。
  - **Q9a**：卡包重载＝**允许替换**，但**对局内不允许重载**。
  - **Q9d**：`PackSystem`/`PackLibrary` 删除；文本→效果能力上移后端，前端只留 DIY 契约。
  - **Q9b（待确认）**：按"干净终态/旧源码在他人手上" ⇒ **不做一次性迁移脚本**。

## Unresolved Questions

### Q10: 宿主给插件的新接口面 In Progress
- 投影 provider 注册通道：(a) **新扩展上下文接口**（沿用 `IMatchPluginContext.Match`/`Binding` 先例）【推荐】；(b) 扩 `IPluginRegistrationContext` 内核最小面；(c) 走 `AssetService`/判定器式统一注册表。
- 动态词条驱动：宿主在卡包 load/unload/reload 成功时回调插件的**生命周期面**（新接口）。
- 代码入口包（`.ui`）注入 `EngineHost` 的方式（现仅磁盘 `PackageDiscovery`）。
- 子问：Q10a provider 注册项形态（单个/具名多注册）；Q10b 生命周期回调时机与失败是否回调；Q10c `EngineHost` 注入 API 形态；Q10d 插件包 id/guid 命名。

### Q11: "尽量不动前端逻辑代码"边界 Pending
- `proto/ui/**` 是否仍冻结；允许改动文件白/黑名单。

### Q12: 验收基线 Pending
- 引擎 `dotnet build/test`（1486 测试）+ 前端工程构建共同验收；前端 `PackSystemTests`/`BuiltinPackTests` 处理。
