# Requirements — Targeter 桥接重设计

> 状态：Confirmed（需求 grill 已退出；Q2/Q4 为推定值，见 R8/R9，可在实现期纠正）

把 targeter 由"平铺槽位 + 一次 Begin 全部完成 + 请求级取消"重构为：效果按需组装（或复用预制模板）的**多步选择流程**；流程内含多个**选择器**，选择器由后端定义契约、前端实现视觉与填写方案；前端走队列逐个取选择器并运行其视觉效果；选择器返回 Rust 风格 Result（用户可取消）；targeter 内部以分支代码消费选择器 Result、决定继续或中止；对外返回 targeter 级 Result，供使用者处理（可取消本次放置 / 卡牌打出）。

## Scope
- Includes: 选择器契约与生命周期；targeter 多步流程与分支；选择器 Result 与 targeter Result；取消在 targeter 内的传播；选择器的参数化与模板/运行时组装/进程内共用；前后端职责边界。
- Excludes: 具体选择器的前端视觉实现（前端侧资产）；发起选靶的业务规则（打出/交战判定器）；现役 `TargetFilter` 业务规则本身。

## Constraints
- **封闭类型族（不采用开放集）**：UI 不考虑热更新，选择器类型不由前端派生/注册（Q5 决策）。
- 校验＝**双层（Q22＝ii）**：结果语义（`Ok`/`Cancelled`/`Failed` 的判定）由选择器（前端）自行给出并信任；后端仍对 `Ok` 产出做业务校验（∈ 候选 ∧ 有效 ∧ 域判定），不通过＝**升级为 `Failed` 交 targeter 内部处理**（同 Q8a，支持重试）。

## Requirement Items

### R1: 选择器槽位模型（多槽位 / 前端队列逐个取）
- Status: Draft
- Scenario/Trigger: 后端提交 targeter 后。
- Behavior: targeter 内含多个选择器槽位；每个槽位由 UI 定义其填写方案并（内联）定义结果；前端走队列模式逐个取 targeter 的选择器，并运行前端定义的视觉效果。
- 已定决策（Q7）：采用 **b（逐步产出·拉取式）**——前端每次向 targeter 取"下一个选择器"，targeter 内部执行至下一交互点才产出；分支＝内部代码流程。
- 已定决策（Q15）：选择器具**稳定身份**；重试＝**同一选择器重入**（targeter 处理 `Failed` 后可把同一选择器再次交付前端，前端据同一 id 提示"选择无效、请重选"）。
- Acceptance:
  - [ ] {待定：逐个取用的契约入口（"取下一个选择器"）、选择器耗尽/流程结束的判定、重试上限}
- Terminology: Targeter, 选择槽位

### R2: 选择器返回 Rust 风格 Result（可取消）
- Status: Draft
- Scenario/Trigger: 用户操作选择器时（含取消）。
- Behavior: 选择器的返回值＝Rust 风格 Result（三段式：`Ok(T)` / `Cancelled` / `Failed(reason)`）。
- 已定决策（Q12）：选择器产出类型＝**编译期泛型就地声明**（`Selector<TResult>`），Result 为泛型。
- 已定决策（Q8）：Result 形态＝**a（三段式）**——`Ok(T)` | `Cancelled` | `Failed(reason)`；取消为一等状态（与现役 `TargetingResult` 三态同构）。
- 已定决策（Q8a）：内容不合规（如选中非法目标）＝**ii**——产生 `Failed` 结果交 targeter 内部处理，**支持重试**（不沿用现役"拒绝即继续等待"）。
- 已定决策（Q16）：`Failed(reason)` ＝**封闭枚举**，当前含 `InvalidSelection`（非法目标），预留 `Fault`（后端异常）；`Cancelled`／`Failed`／`Ok` 为三个出口，语义可与现役 `TargetingEndReason` 映射。
- 已定决策（Q17）：重试**设次数上限**，达上限＝targeter 级 `Failed`。
- 实现决定（A 决策，用户确认）：`min=0` 的多选**允许空提交＝`Ok(空)`**（如换牌"不换牌"）；点选型 `min>0` 空提交＝`Failed`。
- 已定决策（Q20）：**不允许"空选"作为 `Ok`**；是否空提交按**选择器类型**判定——**拖拽型**（松手/离开屏幕且无目标）＝`Cancelled`；**点选型**（有提交门控，若仍空提交）＝`Failed`（err）。选择器不引入"可选空选"语义。
- Acceptance:
  - [ ] {待定：`Failed(reason)` 取值集合、重试上限值、取消的承载形态}
- Terminology: {待定}

### R3: 专门选择器族与参数化 / 复用
- Status: Draft
- Scenario/Trigger: 构造 targeter 时。
- Behavior: 每种视觉表现有专门的选择器（指令从手牌指向、单位从手牌拖出、场上单位指向等）；选择器有自己的参数；构造 targeter 支持"预构建模板 / 运行时组装 / 进程内共用"。
- 已定决策（Q5）：采用 **c（混合）**——后端定义选择器基类/参数模型 + 校验；前端实现呈现与交互驱动；**不开放集**：专门选择器由后端库内定义（封闭类型族），前端不派生新类型，只做「类型 → 视觉」映射。
- 已定决策（Q6）：选择器**在定义处就地声明产出数据类型**（目的＝减少运行时组装的模板代码量）。
- 已定决策（Q13）：模板粒度＝**c（两级）**——选择器模板（基座）＋ targeter 模板（上层，引用选择器模板）。
- 已定决策（Q13a）：**模板/定义 与 运行实例分离**——模板静态、可进程内共用、无运行期身份；实例每次步骤产生，携带身份（供重试重入）、绑定本次参数、承载 Result。
- 已定决策（Q21）：交互语义策略（含空提交判取消 / 判 err）**由选择器定义携带**；**信任选择器自行返回结果**（前端负责）——与 Q5"后端校验"的张力见 Constraints 与 Q22。
- 已定决策（Q10）：现役选择器雏形（`SelectorSlots`、`HandOriginSlots`、`CardTriggerView.SelectorSlots`）**并入新族**（不保留为兼容面）。
- Acceptance:
  - [ ] {待定：选择器枚举清单、参数模型、复用方式}
- Terminology: SelectorSlots

### R4: 多步流程与取消传播（单位指向性部署为例）
- Status: Draft
- Scenario/Trigger: 单位指向性部署（先放置槽位选择，再选择性指向）。
- Behavior: targeter 内部先走放置槽位选择，再走单位指向（选择性、非拖拽）；未选中（指令指向空处、部署后指向空处等）时 result 返回对应 cancel；targeter 内部处理该 result，最终返回 targeter 级 result；该 result 可取消本次放置 / 卡牌打出。
- 场景补充（用户）：单位指向性部署＝放置后触发"场上单位部署"；玩家若**反悔，可取消（拖拽离开/点取消按钮）→ `Cancelled` → 撤销这次部署**。
- 已定决策（Q9）：当前需求**只考虑"一步取消"**（某选择器取消 → 整个 targeter 返回取消结果）；但架构须**保留"内部分支处理 result"的表达力**（不阉割为"取消即硬终止"）。
- 已定决策（Q18）：targeter 级 Result ＝**b（非泛型三段式）**——`Ok`（无业务载荷）| `Cancelled` | `Failed(reason)`；各步骤产出由 targeter **内部消费/回写效果上下文**，使用者只判流程成败（决定是否取消本次放置/打出）。
- Acceptance:
  - [ ] {待定：步骤序列、分支、cancel 层级（选择器级 vs targeter 级）}
- Terminology: {待定}

### R5: 前后端契约总纲
- Status: Draft
- Scenario/Trigger: 契约设计与集成。
- Behavior: targeter＝效果按需组装或使用预制模板，内含选择器槽位；选择器槽位＝后端定义、前端实现；targeter 内部有分支代码处理选择器 result，决定流程继续或中止取消；targeter 的使用者处理 targeter 的 result。
- 已定决策（Q14）：组装/发起＝**b（两者皆可）**——效果 handler 按需组装（第 5 点）＋ 引擎流程（指挥/换牌/打出）仍可组装；共用同一 targeter 框架；使用者 `await` 后判 `TargeterResult` 决定是否撤销本次放置/打出。
- Acceptance:
  - [ ] {待定}
- Terminology: {待定}

### R6: 候选与校验数据流（取消收集）
- Status: Draft
- Scenario/Trigger: 选择器需要候选时。
- Behavior: 候选（允许集）由**后端在组装选择器时给出**、随选择器请求交付前端；前端只做呈现与用户输入；提交后由后端校验。**不再要求前端提交"完整可交互引用列表"**（`CollectCandidatesAsync` 退场）；现役粗筛/细筛职责转化为"组装选择器时构造候选"。
- 已定决策（Q19）：**a（取消收集）**。
- 已定决策（Q22）：校验＝**双层（ii）**——语义由选择器定、后端业务校验兜底；后端校验不通过＝升级 `Failed`（同 Q8a）。
- Acceptance:
  - [ ] {待定：候选构造面、校验面}
- Terminology: {待定}

### R7: 重设计的波及边界（保留 / 改造 / 退场）
- Status: Draft
- Scenario/Trigger: 实现范围界定。
- Behavior: **b（主干替换 + 保留横切骨架）**——`TargeterManager`（FIFO 串行/终局门禁）与留痕保留并改造；`CollectCandidatesAsync`、`TargetFilter`、`TargetOutcome`、`TargetSlot` 全族按新模型退场/重做；`TargetingRequestContext` 拆解（允许集→选择器参数、域判定→后端业务校验层）。
- 已定决策（Q3）：b。
- Acceptance:
  - [ ] {待定：逐类型处置清单（保留/改造/退场）}
- Terminology: {待定}

### R8: 迁移范围（一次性替换）
- Status: Draft（推定，待用户确认）
- Scenario/Trigger: 落地迁移时。
- Behavior: **一次性替换、无兼容层**；同步迁移 `MockTargeterBridge`、`DemoTargeterBridge`、现役 `Targeter*` 测试、`docs/ui-开发者交接文档.md` §4.5、`docs/game-flow/06-目标选择（异步倒置）.md`。
- 已定决策（Q2，推定）：a（一次性替换）。
- Acceptance:
  - [ ] 旧契约类型在库内无残留引用
  - [ ] 全部 targeter 相关测试在迁移后转绿
- Terminology: {待定}

### R9: 验收场景
- Status: Draft（推定，待用户确认）
- Scenario/Trigger: 验收时。
- Behavior: **核心验收＝单位指向性部署**（多步：放置 → 选择性指向；含取消撤销、非法目标重试）；其余现役场景（选空槽 / 选移动・攻击目标 / 换牌 / 抉择 / 手牌选择 / 卡牌选择器 / 钳击）按行为等价迁移，既有测试作回归网。
- 已定决策（Q4，推定）：b（核心验收 + 等价迁移）。
- Acceptance:
  - [ ] 单位指向性部署：放置后指向被取消 → 本次部署被撤销
  - [ ] 单位指向性部署：选中非法目标 → 同一选择器重入、可重试
- Terminology: {待定}
