# Implementation — Targeter 桥接重设计

> 状态：已定稿为可执行计划（实现进行中）。批次：**批 1＝S1–S2（纯新增）**；**批 2＝S3–S7（整体切换）**（I6＝b）。

## Scope
- Target: `src/OrC.Game/Targeting/` 全族重构（选择器契约 + targeter 流程宿主 + 桥接契约）＋ 迁移调用方（`CommandManager`/`PlayManager`/`MulliganManager`/`PincerSystem`）＋ 测试/sample/文档。
- Excludes: 前端（UI 侧）视觉实现；选靶业务规则本身（打出/交战判定器）。

## Implementation Steps

### S1: 核心 Result 类型（选择器级 + targeter 级）
- Status: Done
- Target: `Targeting/SelectorResult.cs`、`Targeting/TargeterResult.cs`、`Targeting/SelectorEvent.cs`、`Targeting/SelectorPresentation.cs`
- Approach: 三段式 `SelectorResult<T>`（`Ok`/`Cancelled`/`Failed(reason)`）与非泛型 `TargeterResult`；失败原因封闭枚举（`InvalidSelection`、`RetryLimitExceeded`、预留 `Fault`）；语义事件（`PickEvent`/`DropEvent`/`CancelEvent`）；呈现数据（`SelectorPresentation`）。
- Acceptance:
  - [ ] `SelectorResult<T>.Ok` 携带强类型产出；`Cancelled`/`Failed` 无产出
  - [ ] 失败枚举可程序化判别
  - [ ] 语义事件可表达"点选提交/拖拽落点/显式取消"
- Rationale: 需求 Q8/Q16/Q18；I2a=c。
- Terminology: 选择器、Result

### S2: 选择器契约与定义/实例分离
- Status: Done
- Target: `Targeting/Selector.cs`、`Targeting/Selectors.cs`
- Approach: `abstract class Selector<TResult>`（定义/模板）＋ `ISelectorInstance`（非泛型基：身份/呈现/提交）＋ `SelectorInstance<TResult>`（实例：判定 + 业务校验 + 承载结果）；参数模型（引用集/选项/名单）；专门选择器族（场上单位指向/手牌起始指向/放置后指向/空槽/手牌/换牌/选项/卡牌）；`SelectorNames` 常量。
- Acceptance:
  - [ ] 定义与实例分离；实例可在异构序列中以 `ISelectorInstance` 持有
  - [ ] 选择器定义携带交互模式（点选/拖拽），据以判定空提交语义
  - [ ] 模板＝静态定义实例，可进程内共用
- Rationale: 需求 Q5/Q6/Q10/Q11/Q12/Q13/Q13a/Q20/Q21。
- Terminology: 选择器、模板

### S3: 桥接契约重定义
- Status: Done
- Target: `Targeting/ITargeterBridge.cs`
- Approach: `ITargeterBridge.BeginTargeting(ITargeterSession session)`（同步交付会话）；`ITargeterSession.NextAsync()` 拉取下一个选择器（`null`＝流程结束）＋ `Result`；选择器实例承载提交（`ISelectorInstance.Submit(SelectorEvent)`）；`CollectCandidatesAsync`/`ITargetingResponder` 退场。
- Acceptance:
  - [ ] 前端可按队列逐个 `NextAsync` 取选择器；耗尽后读 `Result`
  - [ ] 提交经语义事件（非 `bool` 拒绝路径）
- Rationale: 需求 Q7/Q14/Q15/Q19/Q20/Q20a/Q21；I2=b；I2a=c；I5=a。
- Terminology: 桥接、会话

### S4: targeter 流程宿主与 TargeterManager 改造
- Status: Done
- Target: `Targeting/ITargeterFlow.cs`、`Targeting/TargeterManager.cs`
- Approach: `manager.RunAsync(Func<ITargeterFlow, ...> flow, param)`；`flow.Step(选择器, 参数)` / `flow.Retry()`；`TargeterManager` 保留 FIFO 串行队列、终局门禁、留痕；执行时经桥接交付会话。
- Acceptance:
  - [ ] 组装方可在 flow 内写分支并 `await flow.Step(...)`
  - [ ] 拉取式：前端逐个取，耗尽后得 `TargeterResult`
  - [ ] 终局门禁：对局已结束＝即时 `Failed`、零副作用
  - [ ] FIFO：并发请求排队、不拒绝
- Rationale: 需求 Q2/Q7/Q15/Q17/Q18、Q3；I3=c；I5=a。
- Terminology: 指示器、流程宿主

### S5: 后端业务校验与重试
- Status: Done
- Target: `SelectorInstance`（校验）、`ITargeterFlow`（重试上限）
- Approach: 实例内校验（候选 ∈ ∧ `IsAlive` ∧ 域判定），不通过＝`Failed(InvalidSelection)`；`flow.Retry()` 重入同一实例、上限计数，超限＝`Failed(RetryLimitExceeded)`。
- Acceptance:
  - [ ] 非法选择＝`Failed`（同一实例可重入）
  - [ ] 超上限＝`Failed`，不进入死循环
- Rationale: 需求 Q22、Q8a、Q15、Q17；I4=a；I7=ii。
- Terminology: 选择器

### S6: 迁移调用方与选择器雏形
- Status: Done
- Target: `CommandManager`、`PlayManager`、`MulliganManager`、`PincerSystem`、`CardTriggerView`、`UnitCard`、`CommandCard`
- Approach: 等价迁移到 `RunAsync(flow)`；现役 `SelectorSlots`/`HandOriginSlots` 并入新族（`SelectorNames`）。
- Acceptance:
  - [ ] 4 处调用方行为等价（移动/攻击/选空槽/换牌/钳击）
- Rationale: 需求 Q10、Q14、Q3。
- Terminology: 选择器

### S7: 测试 / 示例 / 文档迁移
- Status: Done（示例/测试基础设施/测试用例均已迁移；docs 待更新）
- Target: `MockTargeterBridge`、`TargeterTestKit`、`DemoTargeterBridge`、`Targeter*Tests`、`docs/ui-开发者交接文档.md`、`docs/game-flow/06-目标选择（异步倒置）.md`
- Approach: 一次性替换（Q2 推定）；测试基础设施提供便捷 API 降低改动量。
- 实际状态：`samples` 编译通过；`MockTargeterBridge`/`TargeterTestKit` 已重建为（仿真式）新契约基座；已删除 8 个旧契约专属测试文件（可由 git 恢复）、新建 `TargeterFlowTests`（8 例，覆盖新契约）；**测试全绿：799/799（0 失败）**；`docs` 未更新。
- Acceptance:
  - [ ] 解决方案可编译
  - [ ] targeter 相关测试转绿（或列出未完成清单）
- Rationale: 需求 Q2（推定）。
- Terminology: 桥接

## Known Issues（实现期发现，待用户裁决）

1. **换牌"不换牌"与 Q20 字面冲突（已按 A 决策维持）**：Q20＝"不允许空选作为 `Ok`"，但换牌 `min=0`（空选＝不换牌）需要空提交合法。当前实现取"**点击型 + min=0 时空提交＝`Ok(空)`**"；用户已确认维持。
2. **`SelectorFailureReason` 新增 `RetryLimitExceeded`**：Q16 只列了 `InvalidSelection`＋预留 `Fault`；为实现 Q17（重试上限）新增了一个枚举值。
3. **`TargeterFailureReason` 取值集合**：比现役 `TargetingEndReason` 略作映射调整（合并/新增 `InvalidSelection`/`RetryLimitExceeded`），映射关系未逐项与需求确认。
4. **`Ref<Entity>` 语义**：新代码按"引用类型（含 `Value` 属性）"使用；`DropEvent.Target`/`SelectorResult.Value` 的可空面已按此实现。
5. **`CardTriggerView.SelectorSlots` 类型变更**：由 `IReadOnlyList<TargetSlot>` 改为 `IReadOnlyList<Selector>`（现役选择器雏形并入新族，Q10）。
6. **`JudicatorSelectionRule.AsFilter()` 改名 `AsPredicate()/Filter()`**：筛选链退场（Q19＝a），选择规则改为"候选过滤谓词"；调用侧（若有）需同步。
7. **测试迁移进行中**：已删除 8 个旧契约专属测试文件（`TargeterSlotTests`/`TargeterSlotFamilyTests`/`TargeterFilteringTests`/`TargeterQueueTests`/`TargeterTerminalTests`/`TargeterUsageTests`/`TargeterGameplayDemoTests`/`TargeterMatchTests`，均可由 git 恢复），新建 `TargeterFlowTests`（8 例）；**已完成迁移**（测试侧仿真层 `LegacyTargetingShims.cs` 承载既有用例；另新增 `TargeterContractTests` 24 例真契约测试）；测试全绿 **823/823**。
8. **docs 已更新**：`docs/ui-开发者交接文档.md`（§4.1/§4.4/§4.5/§4.7/§5）、`docs/game-flow/06`、`game-flow/01`、`game-flow/04`、`orc-用户输入点分析报告.md`、`kards-diy-可hook点清单报告.md`、`.ams/context/targeter/CONTEXT.md` 已同步新契约。
9. **`Targeter` 类被删除**：现役 `Targeter` 请求对象退场，改由 `RunAsync(flow)` 承载；`GameEntryPoints` 已同步，但其他文档/注释可能仍提及。
