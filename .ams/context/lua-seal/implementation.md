# Implementation — Lua 相关代码封存

实现 grill 已闭合（无遗留问题）：归档先于移除，sln 改动与文件移除同一提交，验证以「基线/事后构建 + 三测试项目」为准。**全部步骤已完成（2026-10-08）**。

## Scope
- Target: `src/Orc.Lua/`、`tests/Orc.Lua.Tests/`、`OrcEngine.sln`、`.ams/sealed/`、`.ams/context/lua-diy/CONTEXT.md`、`.ams/context/github/交接说明.md`
- Excludes: `OrcEngine.slnx`、`.gitignore`、`docs/` 中提及 Lua 的正文、`outputs/`

## Implementation Steps

### S1: 变更前基线构建
- Status: Done
- Target: `OrcEngine.sln`
- Approach: `dotnet build OrcEngine.sln` 记录基线错误数，作为事后对比参照。
- Acceptance:
  - [x] 基线＝0 警告 / 0 错误（`dotnet` 10.0.302，用时 2.17s）
- Rationale: 避免把既有构建问题误判为本次改动引入。
- Terminology: 编译区

### S2: 归档到 `.ams/sealed/`
- Status: Done
- Target: `.ams/sealed/src/Orc.Lua`、`.ams/sealed/tests/Orc.Lua.Tests`
- Approach: 保留原层级拷贝两个目录，**剔除 `bin/`、`obj/`**；归档后逐一核对被跟踪文件是否齐全。
- Acceptance:
  - [x] `.ams/sealed/src/Orc.Lua` 含 15 个源文件 + `Orc.Lua.csproj`
  - [x] `.ams/sealed/tests/Orc.Lua.Tests` 含 7 个源文件 + `Orc.Lua.Tests.csproj`
  - [x] 归档内不含 `bin/`、`obj/`（实测 0 个）
  - [x] `git check-ignore -v` 命中 `.gitignore:5:/.ams/*`（不被跟踪）
- Rationale: 归档先于移除，保证可回溯且不丢码。
- Terminology: 封存

### S3: `OrcEngine.sln` 移除 Lua 项目
- Status: Done
- Target: `OrcEngine.sln`
- Approach: 三处一并移除——`Project` 块（2 个项目共 4 行）、`ProjectConfigurationPlatforms` 中两 GUID 各 12 行（24 行）、`NestedProjects` 中 2 行。
- Acceptance:
  - [x] `Select-String OrcEngine.sln -Pattern "Orc\.Lua|068C67BA|6B78C5DC"` 无输出
  - [x] 其余 9 条项目条目（含 `src`／`tests`／`samples` 方案文件夹）与配置行完整未损
- Rationale: 与 S4 同一提交，保证任一提交均可构建。
- Terminology: 编译区

### S4: 物理移除原位目录
- Status: Done
- Target: `src/Orc.Lua/`、`tests/Orc.Lua.Tests/`
- Approach: 归档核验通过后删除原位目录（含 `bin/obj`），暂存删除。
- Acceptance:
  - [x] 原位两目录不存在
  - [x] 24 个被跟踪文件在 git 中显示为已删除（D）
- Rationale: 「物理移动 + `git rm`」口径，本地不留原位残留以免误提交。
- Terminology: github 范围

### S5: `lua-diy` 状态标注
- Status: Done
- Target: `.ams/context/lua-diy/CONTEXT.md`
- Approach: 顶部加「**状态：已封存（2026-10-08）**」引用块，指向归档路径；不改写 13 轮决策正文。
- Acceptance:
  - [x] 文档含封存状态与归档路径
  - [x] 原有术语与 13 条决策逐条保留（仅插入 2 行）
- Rationale: 避免文档被读作"现役能力"。
- Terminology: 封存

### S6: 验证
- Status: Done
- Target: `OrcEngine.sln`
- Approach: `dotnet build OrcEngine.sln`；`dotnet test OrcEngine.sln`。
- Acceptance:
  - [x] 构建 0 警告 0 错误（1.74s）
  - [x] 测试全绿：`Orc.Tests` 298 通过、`Orc.Script.Tests` 9 通过、`Orc.Game.Tests` 888 通过，0 失败
- Rationale: 确认无隐式依赖。
- Terminology: 编译区

### S7: 提交与推送
- Status: Done
- Target: git 仓库
- Approach: `chore:`（S3+S4）→ `docs:`（S5 + 本目录三份文档 + 交接说明登记）；随后按 github SOP 现场探测代理端口、三重校验、push 并核验。
- Acceptance:
  - [x] 两个提交，任一提交均可构建（`chore` 提交为原子变更）
  - [x] 推送后 `git ls-remote origin refs/heads/main` 与 `HEAD` 一致
  - [x] `git ls-files src/Orc.Lua tests/Orc.Lua.Tests` 为空
- Rationale: 原子提交 + 既有 SOP。
- Terminology: github 范围

## 验证结果（2026-10-08）
- 基线构建：0 警告 / 0 错误
- 封存后构建：0 警告 / 0 错误
- `dotnet test OrcEngine.sln`：298 + 9 + 888 = 1195 通过，0 失败
- 归档：`.ams/sealed/` 共 24 文件，无 `bin/obj`，已确认不被 git 跟踪
- `OrcEngine.sln`：Lua 相关引用（含两 GUID）清零
