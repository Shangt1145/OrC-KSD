# Requirements Grill Plan — Lua 相关代码封存

> 本 grill 聚焦：封存范围、封存去向与形态、编译区 / github 范围口径、提交与验证。

## Resolved

- **Q1 封存范围 → B**（2026-10-08）：封存 `src/Orc.Lua/`(16)＋`tests/Orc.Lua.Tests/`(8)＋`OrcEngine.sln` 项目条目；`.ams/context/lua-diy/CONTEXT.md` **保留**并补「已封存」状态；`docs/` 中提及 Lua 的正文**不改**（保留历史记录）。
- **Q2 封存去向 → a**（2026-10-08）：物理移动整目录到 `.ams/sealed/`，随后 `git rm -r` 原路径。
- **Q3 github 范围口径**（由 Q2=a 决定）：采用「物理移动 + `git rm -r`」；**已知不可逆残留**：公开提交历史中 Lua 源码仍可被检索。
- **Q4 `.gitignore`** → **无需改动**（`.ams/*` 除 `context/` 外已被忽略，`.ams/sealed/` 天然不入 github）；此前 D2 提议的规则本任务不涉及。

## Unresolved Questions

### Q5: 提交粒度与验证口径 In Progress
- **Q5a 提交粒度**：
  - 硬约束：`OrcEngine.sln` 改动与 `git rm -r` 两目录**必须同一提交**（分开则中间态 sln 引用不存在工程 → 不可构建）。
  - a) 单一 `chore:` 提交（工程范围 + 文件封存 + `lua-diy` 标注）；
  - b) `chore:`（sln + `git rm`）＋`docs:`（`lua-diy` 标注＋`lua-seal` 文档）两提交，沿用既有「代码/文档分开」惯例。
- **Q5b 验证口径**：是否要求封存后 `dotnet build OrcEngine.sln` 0 错误；是否需运行其余测试项目（`Orc.Tests`／`Orc.Game.Tests`／`Orc.Script.Tests`）。
- **Q5c 归档结构**：`.ams/sealed/` 下是否保留原层级（`sealed/src/Orc.Lua`、`sealed/tests/Orc.Lua.Tests`）；是否剔除 `bin/`、`obj/`。
- **Q5d 是否本次一并 push**（含按 github SOP 走代理 + 三重校验 + 核验）。

### Q6: 后续同步与交接登记 Pending
- 是否把「Lua 已封存」写入 `.ams/context/github/交接说明.md` 的「已定策略 / 状态快照」，并随本轮提交。

### Q7: 「移出编译区」的复查余量 Pending
- `OrcEngine.slnx` 为空解决方案（不含项目），本任务不触碰；需确认是否另有构建脚本/IDE 配置引用 Lua（当前检索：无）。
