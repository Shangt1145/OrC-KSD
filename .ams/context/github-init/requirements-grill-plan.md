# Requirements Grill Plan — GitHub 仓库初始化与进度同步

> 本轮 grill 聚焦：写权限与协作模型（仓库归属方非用户，推翻「用户是仓库所有者」的隐含假设）。

## 未决问题

### Q12: `outputs/` 是否入库 Completed
- 触发：提交 `4b0292c` 因使用 `git add -A` 意外带入 `outputs/web-research/`（35 文件 / 1.3MB，含 667KB 的 GitHub API 原始响应与 58KB 网页快照）。
- 决策：a —— **排除整个 `outputs/`**；`.gitignore` 增加 `/outputs/`，并从索引移除已跟踪文件（`git rm -r --cached outputs`，本地文件保留）。结论性内容应写入 `docs/` 或 `.ams-docs/`。
- 附带强化（用户要求）：每次推送前**逐项审查待提交清单**，排除没必要/不能上传的文件；出现未预期路径先汇报再决定。
- 原则确立：**产出入库，过程不入库**。

### Q11: 提交范围是否扩展至源码（需求变更）Completed
- 决策：a —— 范围扩展至源码与工程文件；`.gitignore` 追加 `**/bin/`、`**/obj/`、`.vs/`、`TestResults/`、`*.user`、`*.suo`。
- 结果：提交 `190986f`（32 个文件：28 新增 + 4 修改），254 个 `bin/obj` 编译产物被忽略，推送验证通过。

### Q11-原始记录
- 触发：用户要求「push 一次」，但工作区新增内容超出原 R1 范围。
- 实测：未跟踪文件 282 个，其中 **254 个是 `bin/`、`obj/` 编译产物（9.34MB）**；其余 28 个为 `OrcEngine.sln`、`OrcEngine.slnx`、`src/Orc/Core/*.cs`(10)、`src/Orc/Orc.csproj`、`tests/Orc.Tests/*.cs`(4)、`tests/Orc.Tests/Orc.Tests.csproj`、`.ams/context/CONTEXT.md` 与 `card-engine/`、`orc-engine/` 下 CONTEXT.md(3)、`.ams-docs/2de7063f-.../`(7 个需求/实现/grill 文档)。
- 敏感扫描：新增文本文件中未命中 `sk-*`/`apiKey`/`password`/`Bearer`。
- 冲突点：原 R1 未包含 `src/`、`tests/`、解决方案文件，属提交范围变更。
- 选项 a：扩展至源码与工程文件，并新增 .NET 忽略规则（`bin/`、`obj/`、`.vs/`、`*.user`）——推荐。
- 选项 b：只提交文档，源码留在本地。
- 选项 c：全部提交（含编译产物）——不建议。

### Q10: 用户对 `Shangt1145/OrC-KSD` 的写权限 Completed
- 实测事实（GitHub 公开 API）：`owner.login = Shangt1145`（id 227738197）、`private = false`、`visibility = public`、`description = 114514`、`created_at = 2026-10-03T07:25:31Z`、`size = 0`、`default_branch = main`、`has_issues = true`、`has_pull_requests = true`、`allow_forking = true`。
- 用户陈述：「这个仓库是 Shangt1145 开的，不是我开的」。
- 冲突：原计划以用户身份向该仓库推送，但非所有者默认对该仓库**无写权限**；`git push` 将以 403 失败。
- 选项 a：用户已被加为 collaborator（有写权限）→ 原计划可直接执行。
- 选项 b：尚未被添加，但能联系 Shangt1145 → 本地初始化与推送解耦：先 `init` + `commit`，待对方在 Settings → Collaborators 添加用户账号后再 `push`。
- 选项 c：无法联系或不愿依赖对方 → 改为 fork 到自己账号推送（进度不回流原仓库，除非发 PR 且被合并），或自建新仓库（目标需重新对齐）。

### Q4: 协作模型（他人如何同步回传）In Progress（用户尚未回答）
- 与 Q10 耦合：需明确 Shangt1145 与「其他人」的关系，以及进度是单向流动还是双向。

## 已决问题

### Q1: 同步内容边界 Completed
- 入库：根目录 `USER.md`、`PROJECT.md`、`COMMUNICATION.md`、`RULE.md`；`docs/初始设计文档.md`；`.ams/context/`；`.ams-docs/`。
- 排除：`ams/` 整体；`.ams/skills`、`.ams/packages`、`.ams/versions`。

### Q2: 仓库可见性 Completed（含修正）
- 决策：保持 public。
- 修正：仓库非用户所有，用户无权更改可见性；因此「改 private」分支实际不可执行，public 属于既定事实。

### Q2a: `.ams` 白名单边界 Completed
- 决策：a —— 仅 `.ams/context` 入库。

### Q3: docs 目录矛盾 Completed
- `docs/` 已创建，`初始设计文档.md`（38,597B）已移入；其余 4 个 `.md` 留在根目录。

### Q5: 凭证配置方式 Completed
- 决策：a —— HTTPS + Git Credential Manager 浏览器登录。

### Q7: 提交身份 Completed
- 决策：沿用 `hitman-OLD6 <2872740945@qq.com>`。

## 实现阶段待决

### Q6: `.gitignore` 规则
- 需覆盖 `ams/`、`.ams/skills|packages|versions`。

### Q8: 分支名与首次提交信息
- 远程 `default_branch` 已为 `main`，本地初始化应对齐为 `main`。

### Q9: 原理说明落地形式
- 仅对话说明，或同时落成 `docs/` 内文档。
