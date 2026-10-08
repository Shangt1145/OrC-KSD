# Implementation Grill Plan — Lua 相关代码封存

> 本 grill 聚焦：执行顺序与可逆点、验证证据、提交拆分。

## Unresolved Questions

### Q1: 执行顺序与可逆点 Completed
- 结论（2026-10-08）：**归档先于移除**——① 拷贝 `src/Orc.Lua`、`tests/Orc.Lua.Tests`（剔除 `bin/`、`obj/`）到 `.ams/sealed/` 并核验数量 → ② 改 `OrcEngine.sln` → ③ 物理移除原位两目录 → ④ 验证 → ⑤ 提交。任一步失败都不会丢代码。

### Q2: 验证证据 Completed
- 结论（2026-10-08）：① 变更前 `dotnet build OrcEngine.sln` 取基线；② 变更后同命令须 0 错误；③ `dotnet test` 跑 `Orc.Tests`、`Orc.Game.Tests`、`Orc.Script.Tests`。

### Q3: 提交拆分 Completed
- 结论（2026-10-08）：`chore:`（`OrcEngine.sln` 移除 + 两目录移除，同一提交保证原子可构建）＋ `docs:`（`lua-diy` 标注 + `lua-seal` 文档 + `github/交接说明.md` 登记）。

### Q4: 归档完整性口径 Completed
- 结论（2026-10-08）：以「git 跟踪的 24 个文件全部存在于归档位」为准绳；`bin/`、`obj/` 不归档。

### Q5: 不做的事 Completed
- 结论（2026-10-08）：不改 `.gitignore`；不触碰 `OrcEngine.slnx`；不改写 `docs/` 中提及 Lua 的正文；不删除 `lua-diy/CONTEXT.md` 的 13 轮决策正文。
