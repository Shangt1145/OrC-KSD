# Implementation — OrC 效果解析器

> 实现 grill 进行中。步骤按逻辑顺序排列；**先做转换段（S1–S5），后做解析段（S6–S11）**。本文件即实现计划。

## Scope
- Target: `src/Orc.Game` 内新增命名空间（模板库 + DSL + 转换器 + tokenizer/AST/解析器）。
- Excludes: `src/Orc` 内核改动；通用解释器/运行时装载；卡级字段接线。

## Implementation Steps

### S1: DSL schema 与序列化/校验
- Status: Todo
- Target: `src/Orc.Game/<新命名空间>` —— DSL 实例模型 + JSON 序列化/反序列化 + 校验器。
- Approach: 定义 DSL 实例（模板引用 + op 序列 + 参数 + csx 逃生舱的模型）；System.Text.Json 序列化；fail-fast 校验。
- Acceptance:
  - [ ] DSL 实例可序列化/反序列化。
  - [ ] 非法 DSL 被显式拒绝并给出原因。
- Rationale: 一切以 DSL 为中间层，schema 先行。
- Terminology: [效果 DSL](../requirements.md)

### S2: 两级模板库（trigger 骨架 + op 语句）
- Status: Todo
- Target: `src/Orc.Game` 内模板资产与加载。
- Approach: 模板＝从完整预制体"挖空" handler 得到的 `EffectSnapshot` 骨架；两级（trigger 骨架模板 / op 语句模板）；确定载体与"挖空/填空"约定。
- Acceptance:
  - [ ] 两级模板可被转换器取用。
  - [ ] handler 挖空位置可被 DSL 填充。
- Terminology: [模板效果](../requirements.md)

### S3: 转换器（DSL → 带 csx 的 EffectSnapshot）
- Status: Todo
- Target: 转换器模块。
- Approach: DSL 实例 → 用 op 语句模板渲染 handler csx → 填入 trigger 骨架模板 → 产出 `EffectSnapshot` JSON 文本。
- Acceptance:
  - [ ] 输出为合法 `EffectSnapshot` JSON（schemaVersion=2）。
  - [ ] 主触发器来自模板、handler 来自 DSL。
  - [ ] csx 逃生舱以动态 handler + csx 注入落地。

### S4: MVP 原语与模板落地
- Status: Todo
- Target: 1 个 trigger 骨架 + 3~5 个高频 op 模板（`damage`/`draw`/`buff`/`grant`/`move`）。
- Approach: 按 kards-diy 语料选定首批；逐个给出 DSL 原语定义 + op 语句模板 + 与 OrC 机制的映射说明。
- Acceptance:
  - [ ] MVP 端到端可跑（DSL → 预制体文本）。
  - [ ] 每个原语均可对上一个 OrC 既有机制。

### S5: 转换段验收（往返 + 诊断）
- Status: Todo
- Target: 测试。
- Approach: DSL → 预制体 → `PrefabJson.Deserialize` 往返断言；超子集文本显式失败诊断。
- Acceptance:
  - [ ] 往返一致性测试为绿。
  - [ ] 超子集输入产出显式"未解析"记录。

### S6: 外置词表资产
- Status: Todo
- Target: 词表（ACTION/SIDE/ZONE/FILTER/COND/NUM）+ 卡名词表接口。
- Approach: 词表外置；句式内建。

### S7: tokenizer
- Status: Todo
- Target: 12 类 token 的词法层。
- Approach: 词表最长匹配扫描；标点/引号屏蔽/换行零权重。

### S8: AST 构建与多效果切分
- Status: Todo
- Target: 单效果语法树 + 卡面切分。
- Approach: kards-diy 切分语义（`。`硬 / `；`软 / 换行零权重 / 首句为触发）。

### S9: AST → DSL 生成
- Status: Todo
- Target: 解析段出口。
- Approach: 语法树 → DSL 实例；超子集 → 显式失败。

### S10: 语料验收与覆盖率报告
- Status: Todo
- Target: kards-diy 卡池语料 + 官方 json 样本。

### S11: 术语登记与文档
- Status: Todo
- Target: 根 `CONTEXT.md`（tokenizer/AST/效果解析器/效果 DSL/模板效果）。
