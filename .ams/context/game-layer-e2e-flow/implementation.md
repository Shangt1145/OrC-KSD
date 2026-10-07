# Implementation — 游戏层对局端到端流程文档

实现 grill 已对齐并完成撰写。产物：主索引 1 个 + 分文件 9 个（`docs/游戏层对局端到端流程.md` + `docs/game-flow/01..09`）。

## Scope

- Target: `d:/OrC-KSD/docs/` 下"游戏层对局端到端流程"多文件文档。
- Excludes: 前端 UI 实现代码；源码/模板 JSON 内容；信号目录与输入点签名表全文（改为链接）。

## Implementation Steps

### S1: 文件切分与目录/命名
- Status: Done
- Target: `docs/游戏层对局端到端流程.md`（主索引）+ `docs/game-flow/01..09`（按簇分组）
- Approach: 主文件承载索引/路由/总表；分文件按簇覆盖 R2 清单，抬头含覆盖/内容边界/相关文件。
- Acceptance:
  - [x] 主文件存在且链接全部分文件。
  - [x] 分文件 9 个齐备。

### S2: 主索引文件
- Status: Done
- Target: `docs/游戏层对局端到端流程.md`
- Approach: 8 节（模型/基线 → 生命周期总览 → **任务→文件 路由表** → 分文件索引 → hook 总表 → 效果总表 → 术语速查 → 既有文档链接）。
- Acceptance:
  - [x] 含任务→文件路由表（每条 1–2 文件）。
  - [x] 含 hook 总表（接入点/观察面/扩展点三类）。

### S3: 各流程分文件
- Status: Done
- Target: `docs/game-flow/01..09`
- Approach: 按主文件编号序撰写；每文件写前做代码符号复核；每文件按 R6 模板。
- Acceptance:
  - [x] 覆盖 R2 清单 ①–⑲。
  - [x] 每文件含"分步骤（步骤→代码位置→语义→信号→hook）"。
  - [x] 主干链 6 条含 mermaid（01 初始化、02 回合、04 打出/部署、05 交战、05 死亡、01 生命周期总览）。

### S4: hook 总索引表
- Status: Done
- Target: 主文件 §4
- Approach: 列＝类型｜面名(符号)｜所在流程/步骤(文件§)｜介入/观察方式｜可用性。
- Acceptance:
  - [x] 三类（接入点/观察面/扩展点）均有覆盖且落位。

### S5: 效果预制体总表 + 效果体系总述
- Status: Done
- Target: 主文件 §5 + `08-效果体系.md`
- Approach: 以 `*.tpl.json`（29 个）为主，列 id ↔ 流程位/信号 ↔ inject 目标；追加效果体系总述（DSL/模板/prefab/装载链/injects）。
- Acceptance:
  - [x] 29 个模板 id 与 `Templates/` 目录一致。
  - [x] 含装载链与 injects 落地。

### S6: 缺口标注
- Status: Done
- Target: 各分文件"能力现状"栏
- Approach: 以 `GameHooks.PendingTriggers`（27 项：6 已具备/13 Deferred/8 NotPlanned）为权威 + 跨层已知缺口（钳击 internal、支援线仅 HQ 槽）。
- Acceptance:
  - [x] 缺口与 `GameHooks.cs` 一致。
  - [x] 单列"跨层已知缺口"。

### S7: 成品校验
- Status: Done
- Target: 全部文档
- Approach: 校验清单核对。
- Acceptance:
  - [x] 引用的类型/方法/信号/模板 id 存在（抽查通过）。
  - [x] hook 三类覆盖且落位。
  - [x] 模板 id 与目录一致（29/29）。
  - [x] 缺口与 `GameHooks` 一致。
  - [x] 主文件路由表每条 1–2 文件。

## Rationale（关键决策）

- 落点＝引擎仓 `docs/`：文档主体是对引擎代码的流程梳理，需与代码同版本维护、避免第二真源。
- 口径＝实况 + 缺口标注：引擎确有后置/不做机制，只写实况会误导前端。
- hook 用「接入点/观察面/扩展点」：遵 `CONTEXT.md`，"hook"为内核窄义词。
- 多文件 + 渐进式路由：服务"agent 只读最相关 1–2 文件"的检索体验。
- 效果以 `*.tpl.json` 为主：模板与对局事件天然一一对应，是流程↔效果的引用入口。

## 过程发现（已在文档中标注）

- `GameEntryPoints.cs` 把 `MulliganReplace`/`MulliganDone`/`Concede` 记为 `PendingDelivery`，实际 `Match.cs` 已落地（且无 `MulliganReplace` 成员）——**索引滞后**，文档以代码为准。
- 单位交战不走内核 `AttackFlow`/`DamageFlow`；真源在 `CommandManager`。
- 致死时 `card.died` 可能先于门户末尾 `card.damaged` 出现（`HandleDefenseDepletionAsync` 触发）。
