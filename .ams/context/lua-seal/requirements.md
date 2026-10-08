# Requirements — Lua 相关代码封存

将 Lua 相关代码移出编译区与 GitHub 范围并封存，使仓库与构建不再包含 Lua 能力，同时保留本地可回溯归档与既有知识文档。

## Scope
- Includes: `src/Orc.Lua/`（16 个被跟踪文件）、`tests/Orc.Lua.Tests/`（8 个被跟踪文件）、`OrcEngine.sln` 中两者的 `Project` / `ProjectConfigurationPlatforms` / `NestedProjects` 条目。
- Includes: `.ams/context/lua-diy/CONTEXT.md` 补「已封存」状态标注。
- Excludes: `docs/` 中提及 Lua 的正文（`game-flow/08-效果体系.md`、`kards-diy-*.md`、`orc-用户输入点分析报告.md` 等）——保留原状，不改写。
- Excludes: `.ams/context/lua-seal/` 之外的本任务无产物；`/ams/`、`/outputs/`、`**/bin/`、`**/obj/` 为既有红线，保持不变。
- Excludes: `outputs/web-research/filament-demo-policy.lua`（本就在 GitHub 范围外）。

## Constraints
- **公开仓库不可逆**：推送后无法撤回；推送前须过白名单/黑名单校验。
- **原子性**：`OrcEngine.sln` 改动与两目录移除必须同一提交，保证任一提交均可构建。
- 不得破坏其余工程（`Orc`、`Orc.Game`、`Orc.Script`、`Orc.Game.Sample`）的构建与测试。
- 归档不得进入 GitHub 范围（依赖 `.ams/*` 忽略规则实现，不新增 `.gitignore` 规则）。

## Requirement Items

### R1: 移出编译区
- Status: Confirmed
- Scenario/Trigger: 使用 `OrcEngine.sln` 构建解决方案时。
- Behavior: `OrcEngine.sln` 不再包含 `Orc.Lua` 与 `Orc.Lua.Tests`；其余项目照常构建。
- Acceptance:
  - [ ] `OrcEngine.sln` 中各节（`Project` / `ProjectConfigurationPlatforms` / `NestedProjects`）检索不到 `Orc.Lua`（含 GUID `068C67BA…`、`6B78C5DC…`）
  - [ ] `dotnet build OrcEngine.sln` 0 错误
  - [ ] 其余测试项目仍可运行且全绿
- Terminology: 编译区（= `OrcEngine.sln`，`OrcEngine.slnx` 为空不承载项目）

### R2: 移出 GitHub 范围
- Status: Confirmed
- Scenario/Trigger: 推送后浏览远程仓库时。
- Behavior: 远程 `main` 的工作树中不再包含 Lua 代码文件。
- Acceptance:
  - [ ] `git ls-files src/Orc.Lua tests/Orc.Lua.Tests` 为空
  - [ ] 远程 `main` 上两目录不存在（`ls-remote` 后核验提交内容）
  - [ ] 已知残留：公开历史仍含 Lua 源码（不可逆，接受）
- Terminology: github 范围

### R3: 封存（可回溯归档）
- Status: Confirmed
- Scenario/Trigger: 日后需取回 Lua 实现时。
- Behavior: 本地 `.ams/sealed/` 保留完整副本，且不被 git 跟踪。
- Acceptance:
  - [ ] `.ams/sealed/` 下 24 个被跟踪文件齐全（另含 csproj 等）
  - [ ] `git check-ignore` 命中归档路径（即不被跟踪）
  - [ ] 原位 `src/Orc.Lua`、`tests/Orc.Lua.Tests` 已不存在
- Terminology: 封存

### R4: 知识文档状态标注
- Status: Confirmed
- Scenario/Trigger: 阅读 `.ams/context/lua-diy/CONTEXT.md` 时。
- Behavior: 文档顶部标明「已封存」，并指向归档位，避免被读作"现役能力"。
- Acceptance:
  - [ ] `lua-diy/CONTEXT.md` 含「已封存（2026-10-08）」与归档路径
  - [ ] 其余决策正文不改写
- Terminology: 封存
