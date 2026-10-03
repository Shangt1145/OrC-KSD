# Requirements — GitHub 仓库初始化与进度同步

将本地工作区 `d:/OrC-KSD` 与远程空仓库 `Shangt1145/OrC-KSD` 建立版本控制关联，使其可作为向他人同步项目进度的通道。

## Scope
- Includes:
  - 工作区根目录文档：`USER.md`、`PROJECT.md`、`COMMUNICATION.md`、`RULE.md`
  - `docs/`：项目文档归集目录，当前含 `初始设计文档.md`
  - `.ams/context/`：agent 产出的专用文档（`CONTEXT-MAP.md`、`github-init/`），与 `docs/` 并行
  - `.ams-docs/`：需求/实现文档注册表（`registry.json`、`registry.md`，当前为空）
- Excludes:
  - `ams/`：另一主力开发 agent 的可执行程序、日志与 AI 服务配置（含明文 API Key 与 68.5MB 二进制）
  - `.ams/` 下 `context/` 以外的子目录：`skills/`、`packages/`、`versions/`

## Constraints
- **仓库归属**：`Shangt1145/OrC-KSD` 的所有者为 `Shangt1145`，非本机用户；用户对其默认无写权限，可见性亦无法由用户更改。当前该仓库为空（`size = 0`）、public、`default_branch = main`、允许 fork。
- 远程仓库公开，提交内容对全网可见；公开不可逆。
- 认证方式：HTTPS + Git Credential Manager 浏览器登录（系统级 `credential.helper=manager` 已就绪）。
- 提交署名：`hitman-OLD6 <2872740945@qq.com>`（QQ 邮箱将公开可见，用户已知悉）。
- 不在提交范围内的已知敏感项：`ams/ai-services/f-m-1..6.json` 明文 API Key、`ams/Orange.Ams.UI.exe`（68.5MB）。

## Requirement Items

### R1: 提交范围
- Status: Confirmed
- Scenario/Trigger: 初始化仓库并向他人同步进度时。
- Behavior: 版本控制覆盖根目录 4 个文档、`docs/`、`.ams/context/`、`.ams-docs/`；其余路径不纳入。
- Acceptance:
  - [ ] `git status` 中不出现 `ams/` 下任何文件
  - [ ] `.ams/` 下仅 `context/` 被纳入版本控制
  - [ ] `.ams-docs/` 两个文件被纳入版本控制
  - [ ] 根目录 4 个 `.md` 与 `docs/初始设计文档.md` 被纳入版本控制
- Terminology: —

### R2: 远程关联与首次推送
- Status: Draft（待 Q10 确认写权限后可定稿）
- Scenario/Trigger: 在工作区执行一次性初始化。
- Behavior: 本地建立 Git 仓库（分支 `main`），关联远程 `origin`，把 R1 范围内的文件纳入首次提交并推送到远程 `main`；推送时通过 GCM 浏览器登录完成认证。
- 前置条件: 用户须对 `Shangt1145/OrC-KSD` 具备写权限（collaborator）。
- Acceptance:
  - [ ] 工作区存在 `.git/`，当前分支为 `main`
  - [ ] `git remote -v` 显示 `origin` 指向 `https://github.com/Shangt1145/OrC-KSD.git`
  - [ ] 首次提交内容严格等于 R1 范围，无 `ams/` 与 `.ams` 非 `context` 内容
  - [ ] 远程 `main` 分支存在，本地与远程一致

### R3: 他人获取与更新进度
- Status: Draft
- Scenario/Trigger: 他人需要查看进度，或用户需要同步新进度时。
- Behavior: （待 Q4 / Q10 对齐）
- Acceptance:
  - [ ] （待确认）
