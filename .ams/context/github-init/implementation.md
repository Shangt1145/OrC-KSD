# Implementation — GitHub 仓库初始化与进度同步

实现阶段对齐中：本地初始化与首次提交无外部依赖，可立即执行；远程推送依赖 collaborator 权限到位。

## Scope
- Target: `d:/OrC-KSD` 的 Git 元数据（`.git/`、`.git/config`）、新增 `.gitignore`、首次提交。
- Excludes: 任何既有文档内容的改写；`ams/`、`.ams/skills|packages|versions` 的入库；仓库设置（可见性、collaborator —— 用户无权限操作）。

## Implementation Steps

### S1: 编写 `.gitignore`
- Status: Todo
- Target: `d:/OrC-KSD/.gitignore`
- Approach: 黑名单排除 + 白名单例外，共 3 行：
  ```gitignore
  /ams/
  /.ams/*
  !/.ams/context/
  ```
- Acceptance:
  - [ ] `git check-ignore -v ams/Orange.Ams.UI.exe` 命中 `/ams/`
  - [ ] `git check-ignore -v .ams/skills/double-grill/SKILL.md` 命中 `/.ams/*`
  - [ ] `git check-ignore -v .ams/context/CONTEXT-MAP.md` 无命中（未被忽略）
  - [ ] `.ams` 下未来新增的工具目录默认不入库（默认拒绝）
- Rationale: 明文 API Key 与 68MB 二进制都在 `ams/`；`.ams` 采用「默认全部忽略 + 只放行 `context/`」，比逐个列举要忽略的子目录更安全。
- Terminology: —

### S2: 初始化本地仓库
- Status: Todo
- Target: `d:/OrC-KSD/.git/`
- Approach: `git init -b main`
- Acceptance:
  - [ ] `.git/` 已创建
  - [ ] `git branch --show-current` 输出 `main`
- Rationale: 远程 `default_branch` 已是 `main`，本地分支必须一致，否则首次推送会多出分支或需额外参数。

### S3: 关联远程仓库
- Status: Todo
- Target: `.git/config`
- Approach: `git remote add origin https://github.com/Shangt1145/OrC-KSD.git`
- Acceptance:
  - [ ] `git remote -v` 显示 fetch / push 两行均指向该 URL
- Rationale: 采用 HTTPS 而非 SSH，以配合已就绪的 Git Credential Manager 浏览器登录（Q5 决策）。

### S4: 精确暂存
- Status: Todo
- Target: Git 暂存区
- Approach: 显式列出路径，不用 `git add -A`：
  `git add .gitignore USER.md PROJECT.md COMMUNICATION.md RULE.md docs .ams/context .ams-docs`
- Acceptance:
  - [ ] `git ls-files` 结果严格等于 R1 允许范围
  - [ ] `git ls-files` 中不含 `ams/`、`*.exe`、`ai-services`、`skills/`、`packages/`、`versions/`
- Rationale: 与 `.gitignore` 构成双保险；即便忽略规则有疏漏，显式路径也不会把密钥带进暂存区。

### S5: 创建首次提交
- Status: Todo
- Target: 本地 `main` 分支
- Approach: `git commit -m "chore: 初始化仓库，纳入工作区文档与 agent 上下文文档"`，署名沿用全局 `hitman-OLD6 <2872740945@qq.com>`
- Acceptance:
  - [ ] `git log --oneline` 有且仅有 1 条记录
  - [ ] `git show --stat HEAD` 的文件集合与 S4 一致
- Rationale: 语义化前缀 `chore` 表明非功能变更；提交信息用中文与项目文档语言一致。

### S6: 推送到远程（前置：写权限）
- Status: Todo（阻塞于 collaborator 权限）
- Target: `origin/main`
- Approach: `git push -u origin main`
- Acceptance:
  - [ ] 推送成功，`git status` 显示与 `origin/main` 一致
  - [ ] `git ls-remote origin` 能看到 `refs/heads/main`
- 风险与退路:
  - GCM 需要人工在浏览器完成授权，属交互步骤。
  - 若用户级配置 `credential.helperselector.selected=<no helper>` 导致不弹窗，退回手动 PAT，或执行 `git credential-manager configure` 重设。
  - 若返回 403，说明 collaborator 权限尚未生效，本地提交保留不动，待权限到位后重试即可。

### S7: 原理与日常流程交付
- Status: Todo（落地形式待 Q9 决策）
- Target: 对话输出；可选 `docs/Git-同步流程说明.md`
- Approach: 讲清四层模型（工作区 → 暂存区 → 本地仓库 → 远程仓库）与日常三条命令（`add` / `commit` / `push`），以及他人如何 `clone` / `pull` 获取最新进度。
- Acceptance:
  - [ ] 用户能复述「改完文件后要做什么才能让别人看到」
