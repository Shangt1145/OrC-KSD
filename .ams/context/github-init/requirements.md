# Requirements — GitHub 仓库初始化与进度同步

将本地工作区 `d:/OrC-KSD` 与远程仓库 `Shangt1145/OrC-KSD` 建立版本控制关联，使其可作为向他人同步项目进度的通道。

## Scope
- Includes:
  - 工作区根目录文档：`USER.md`、`PROJECT.md`、`COMMUNICATION.md`、`RULE.md`
  - `docs/`：项目文档归集目录（`初始设计文档.md`）
  - `.ams/context/`：agent 产出的文档与术语表（`CONTEXT-MAP.md`、`CONTEXT.md`、`card-engine/`、`orc-engine/`、`github/`、`github-init/`），与 `docs/` 并行
  - `.ams-docs/`：需求/实现文档注册表与各任务过程文档
  - 原型源码：`OrcEngine.sln`、`OrcEngine.slnx`、`src/**`、`tests/**`
- Excludes:
  - `ams/`：另一主力开发 agent 的可执行程序、日志与 AI 服务配置（含明文 API Key 与 68.5MB 二进制）
  - `outputs/`：agent 的过程性输出（原始网页/接口抓取快照），结论需写入 `docs/` 或 `.ams-docs/`（第 10 轮决策 12.a）
  - `.ams/` 下 `context/` 以外的子目录：`skills/`、`packages/`、`versions/`
  - .NET 构建产物与 IDE 本地状态：`**/bin/`、`**/obj/`、`.vs/`、`TestResults/`、`*.user`、`*.suo`

## Constraints
- **仓库归属**：`Shangt1145/OrC-KSD` 所有者为 `Shangt1145`，非本机用户；用户需被接受为 collaborator 后才具备写权限，且无权更改可见性。
- 远程仓库公开（public），提交内容对全网可见；公开不可逆。
- 认证方式：HTTPS + Git Credential Manager；git 不读取 Windows 系统代理，需显式指定 `http.proxy`（飞鸟加速器 `127.0.0.1:20955`）。
- 提交署名：`hitman-OLD6 <2872740945@qq.com>`（QQ 邮箱将公开可见，用户已知悉）。
- 行尾处理：`core.autocrlf = true`，入库为 LF、检出为 CRLF。
- 并发写入：工作区内另有 AMS agent 持续写入 `src/`、`.ams/context/`、`.ams-docs/`、`outputs/`，提交后可能出现新的未提交改动。
- **提交前逐项审查（用户强制要求）**：每次推送前必须逐项核查待提交清单，排除「没必要/不能上传」的文件；出现未预期路径时先汇报用户，不得自行提交。
- 不在提交范围内的已知敏感项：`ams/ai-services/f-m-1..6.json` 明文 API Key、`ams/Orange.Ams.UI.exe`（68.5MB）。

## Requirement Items

### R1: 提交范围
- Status: Confirmed（第 9 轮扩展源码，第 10 轮排除 `outputs/`）
- Scenario/Trigger: 向他人同步进度时。
- Behavior: 版本控制覆盖根目录 4 个文档、`docs/`、`.ams/context/`、`.ams-docs/`、原型源码与解决方案文件；`ams/`、`outputs/`、`.ams` 非 `context` 子目录、.NET 构建产物不纳入。
- Acceptance:
  - [x] `git status` 中不出现 `ams/` 下任何文件
  - [x] `.ams/` 下仅 `context/` 被纳入版本控制
  - [x] `.ams-docs/` 中文档被纳入版本控制
  - [x] 根目录 4 个 `.md` 与 `docs/初始设计文档.md` 被纳入版本控制
  - [x] `src/`、`tests/`、`OrcEngine.sln(x)` 被纳入版本控制
  - [x] `outputs/` 不再被跟踪（`git ls-files outputs` 为空），且 `.gitignore` 已排除
  - [x] 暂存清单中无 `/(bin|obj)/`、无 `.exe`
- Terminology: —

### R2: 远程关联与推送
- Status: Confirmed
- Scenario/Trigger: 初始化及每次同步进度时。
- Behavior: 本地 `main` 与远程 `origin/main` 保持一致；推送经代理访问 GitHub，认证走 GCM；推送前逐项审查清单。
- Acceptance:
  - [x] `.git/` 存在且当前分支为 `main`
  - [x] `origin` 指向 `https://github.com/Shangt1145/OrC-KSD.git`
  - [x] 远程 `main` 与本地 HEAD 一致
  - [x] `git status -sb` 显示 `## main...origin/main`

### R3: 他人获取与更新进度
- Status: Draft
- Scenario/Trigger: 他人需要查看进度，或用户需要同步新进度时。
- Behavior: 默认按「单向同步」处理（用户推送，他人拉取/浏览）；待 Q4 最终确认。
- Acceptance:
  - [ ] （待确认）

### R4: 交接可执行性
- Status: Confirmed
- Scenario/Trigger: 把 GitHub 同步工作交接给专职 agent 会话时。
- Behavior: 工作区中存在一份自包含的交接文档，接任者无需用户复述背景即可独立完成一次同步。
- Acceptance:
  - [x] `.ams/context/github/交接说明.md` 存在，包含项目背景、仓库与权限、环境事实、提交范围红线、SOP、排障表
  - [x] 已在 `.ams/context/CONTEXT-MAP.md` 注册，可被后续 agent 发现
- Terminology: —
