# Requirements Grill Plan — Targeter 桥接重设计

> 本 grill 聚焦：把 targeter 从"平铺槽位 + 一次 Begin 全部完成 + 请求级取消"重构为多步选择流程（选择器由后端定义、前端实现；前端按需逐个取用；三段式 Result；targeter 内部分支；targeter 级 Result 与取消传播）。

## Unresolved Questions

### Q1: 重设计的核心驱动 Completed
- 交互模型（前端逐个取选择器）＋契约形态（Result）为主线；含装配复用与多步流程＋取消传播（用户第 1–5 点）。

### Q2: 兼容与迁移范围 In Progress
- Q3=b 已排除"兼容层"选项 → 推定**一次性替换**，同步迁移 `MockTargeterBridge`、`DemoTargeterBridge`、现役 `Targeter*` 测试、docs（`ui-开发者交接文档.md` §4.5 / `game-flow/06`）。待用户确认。

### Q3: 重设计的契约边界 Completed
- **b（主干替换 + 保留横切骨架）**：`TargeterManager`（FIFO/门禁）＋留痕保留并改造；描述/产出/筛选/槽位族重做或退场。

### Q4: 验收场景 Pending
- 选空槽 / 选攻击目标 / 换牌 / 抉择 / 手牌选择 / 卡牌选择器 / 单位指向性部署。

### Q5: 选择器"定义侧 / 实现侧"落地形态 Completed
- **c（混合）**：后端定义选择器基类/参数模型 + 校验；前端实现呈现与交互驱动；**不开放集**。

### Q6: "内联定义结果"的语义 Completed
- **i**：选择器在定义处就地声明产出数据类型（减少运行时组装模板代码量）。

### Q7: 选择器的枚举与推进模型 Completed
- **b（逐步产出·拉取式）**：前端每次向 targeter 取"下一个选择器"，targeter 执行至下一交互点才产出；分支＝内部代码流程。

### Q8: Result 的具体形态 Completed
- **a（三段式）**：`Ok(T)` | `Cancelled` | `Failed(reason)`；取消一等状态（与现役 `TargetingResult` 三态同构）。

### Q8a: 选择器内容"不合规"的路径 Completed
- **ii**：产生 `Failed` 结果、交 targeter 内部处理，**支持重试**（可能选到非法目标）。

### Q9: 取消语义 Completed
- "分情况；当前**只考虑一步取消**（某选择器取消 → 整个 targeter 返回取消结果），但**保留分支内部处理的表达力**"。

### Q10: 现役选择器雏形的去向 Completed
- **并入新族**（`SelectorSlots`、`HandOriginSlots`、`CardTriggerView.SelectorSlots`）。

### Q11: 前端"实现"的形态 Completed
- 后端库内定义封闭类型族，前端不派生类型，只做「类型 → 视觉实现」映射（同进程引用后端库）。

### Q12: 产出类型"就地声明"的落地 Completed
- **i（编译期泛型）**：`Selector<TResult>`，强类型贯穿 targeter 内部分支。

### Q13: 选择器的模板与"进程内共用"机制 Completed
- **c（两级模板）**：选择器模板（基座）＋ targeter 模板（上层）。

### Q13a: 模板与运行实例的身份关系 Completed
- 模板/定义＝静态、可共用、无运行期身份；实例＝每次步骤产生、携带身份（供重试重入）、绑定本次参数、承载 Result。

### Q14: targeter 的组装面与使用者衔接 Completed
- **b（两级使用者皆可）**：效果 handler 按需组装 ＋ 引擎流程（指挥/换牌/打出）仍可组装；共用同一 targeter 框架；使用者 `await` 后判 `TargeterResult` 决定是否撤销本次放置/打出。

### Q19: 候选与校验的数据流（是否保留"收集＋筛选"） Completed
- **a（取消收集）**：候选由后端在组装选择器时给出、随选择器交付前端；前端只呈现与输入；`CollectCandidatesAsync` 退场。

### Q20: 空选（放弃选择）的语义与 Result 对应 Completed
- **a 且更细**：**不允许"空选"作为 `Ok`**；拖拽型（松手/离开屏幕无目标）＝`Cancelled`；点选型（有提交门控、仍空提交）＝`Failed`。不引入"可选空选"。

### Q20a: 可选步骤声明 Completed
- 不引入"可选空选/跳过"语义；点击选择经**提交门控**消除空选可能，只需"取消"。

### Q21: 空提交判定的归属与 reason Completed
- 前者：交互语义策略（空提交判取消/判 err）**由选择器定义携带**；**信任选择器自行返回结果**（前端负责）。

### Q22: 校验归属的仲裁 Completed
- **ii（双层）**：结果语义由选择器（前端）判定并信任；后端仍对 `Ok` 产出做业务校验（∈ 候选 ∧ 有效 ∧ 域判定），不通过＝升级 `Failed` 交 targeter 内部处理（同 Q8a）。

### Q15: 重试的表达机制 Completed
- **a（同一选择器重入）**：选择器具稳定身份；targeter 处理 `Failed` 后可将同一选择器再次交付前端。

### Q16: `Failed(reason)` 取值集合 Completed
- 封闭枚举，含 `InvalidSelection`，预留 `Fault`；与现役 `TargetingEndReason` 可映射。

### Q17: 重试的终止条件 Completed
- 同意设上限：重试次数上限，达上限＝targeter 级 `Failed`。

### Q18: targeter 级 Result 形态 Completed
- **b（非泛型三段式）**：`Ok`（无业务载荷）| `Cancelled` | `Failed(reason)`；各步骤产出由 targeter 内部消费/回写效果上下文，使用者只判流程成败。
