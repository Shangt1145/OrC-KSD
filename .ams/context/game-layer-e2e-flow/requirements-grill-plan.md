# Requirements Grill Plan — 游戏层对局端到端流程文档

> 本 grill 聚焦：为 Godot 前端开发者产出一份「游戏层（Orc.Game）对局端到端流程 + hook 位置」文档的**交付形态、边界、粒度与真源**。

## 已收集的事实基线（非问题，作为 grill 依据）

- 引擎真源：`src/Orc.Game`（游戏层）+ `src/Orc`（内核）。
- 已有强相关文档：`docs/ui-开发者交接文档.md`、`docs/orc-用户输入点分析报告.md`、`docs/kards-diy-游戏流程分析报告.md`、`.ams/context/ui-event-stream/ui-integration-guide.md`、`.ams/context/ui-handover/`。
- 代码侧既有索引：`GameEntryPoints.cs`、`GameHooks.cs`、`GameUpdates.cs`。
- 效果预制体实况：`EffectParsing/Templates/*.tpl.json`（29 个模板效果，逐事件对应；内含 `injects` 指向具名触发器）；运行期 `*.prefab.json`（`PrefabManager`，仓库当前 0 个）。

## 已裁决（Completed）

- Q1 交付形态与落点 → **a**：引擎仓 `d:/OrC-KSD/docs/` 新建独立文档。
- Q2 「流程完善」口径 → **乙**：实况 + 显式缺口标注。
- Q3 流程覆盖范围 → **清单全收 ①–⑰ + ⑱初始化补充 + ⑲调试工具**。
- Q4 hook 粒度 → **甲**：逐步骤标注 + 末尾总索引表；**文档不放源代码**。

## Unresolved Questions

### Q9: 「效果预制体」的引用口径【Completed】
- 决策：**乙**——以 `*.tpl.json`（模板效果）为主引用（逐步骤标 id + 末尾总表），并追加一节「效果体系总述」（DSL/模板效果/效果预制体/装载链/injects 落地）；`*.prefab.json` 概念级提及。
- 子问认可：不贴源码、模板 JSON 内容也不贴（只引 id/文件名）。

### Q10: 内核调试 API 是否纳入【Completed】
- 决策：**纳入**，单列标注"内核面（src/Orc）"。

### Q11: 术语确认【Completed】
- 决策：正文用「接入点/观察面/扩展点」，开头说明与 hook 的关系。

### Q12: 每个流程小节的呈现模板【Completed】
- 决策：**乙**——全套模板（触发/前置｜分步骤表｜关联效果预制体｜前端对接要点｜能力现状）；mermaid 仅主干链 6 条（初始化、回合、打出/部署、交战、死亡、终局）。

### Q5: 与既有文档的边界【Completed】
- 决策：自包含流程；信号目录全文/输入点签名表全文改为链接既有 3 份文档。

### Q8: 真源与时效【Completed】
- 决策：以符号名（类型.方法）为准；行号仅参考；标注基线日期 2026-10-07。

### Q13: 文档组织粒度【Completed】
- 决策：**乙**——主文件（索引）+ 各流程分文件。
- 子问：加「前端速查路由」（默认采纳）。
- 待实现 grill：分文件的具体切分方式（每流程一文件 vs 按簇分组）与目录/命名。
