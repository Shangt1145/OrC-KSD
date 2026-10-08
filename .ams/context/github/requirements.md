# Requirements — 交接文档更新与 GitHub 托管业务永久交接

更新 GitHub 托管角色的权威交接文档使其与实测事实一致，并使其可被**任意接手者（人或 agent）**直接用于长期承担托管业务。

## Scope
- Includes: `.ams/context/github/交接说明.md`（唯一权威文档）、`.gitignore`（过程物规则）
- Includes: 本目录的 `requirements.md`、`requirements-grill-plan.md`、`implementation.md`、`implementation-grill-plan.md`
- Excludes: 业务源码与测试（本角色不写业务代码）
- Excludes: `.ams-docs/` 过程物、`outputs/`、`ams/`
- Excludes: 已封存的 `Orc.Lua`（仅在工程表标注为已封存）

## Constraints
- 公开仓库不可逆；文档中的"入库/扣下"规则必须可执行、可验证。
- 文档必须自洽：不保留与实测矛盾的表述。
- `.gitignore` 改动需用户显式授权（Q2-D2 已授权；Q4 语义升级已确认）。

## Requirement Items

### R1: 事实一致性修订
- Status: Confirmed
- Scenario/Trigger: 接手者按文档操作时。
- Behavior: 文档中的环境事实、工程构成、状态快照与实测一致。
- Acceptance:
  - [x] 代理/直连表述与实测一致（"直连优先、代理兜底"）
  - [x] 工程表标注 `Orc.Lua` 已封存；`OrcEngine.slnx` 为空的事实写明
  - [x] 状态快照更新至 `d18a49d` 一带
- Terminology: 代理端口、编译区

### R2: 永久交接可用性
- Status: Confirmed
- Scenario/Trigger: 新接手者首次独立完成一次同步时。
- Behavior: 文档提供可直接执行的自检与交接验收清单。
- Acceptance:
  - [x] 含「第十二节 交接验收清单」（可勾选）
  - [x] D1~D6 全部给出结论（第八节「已收敛」），另列仍开放项 O1、O2
- Terminology: 永久交接

### R3: 远程超前防护
- Status: Confirmed
- Scenario/Trigger: 存在第二写手（`OrC-KSD UI`）直推时。
- Behavior: SOP 在推送前探测远程，避免因 remote-tracking 过期造成误判或非快进被拒。
- Acceptance:
  - [x] SOP ①b 含远程探测（`git ls-remote` / `git fetch`）
  - [x] 排障表含「`status -sb` 无 ahead 但 push 被拒」一行
  - [x] 第六节含「别人先推了」处置（ff → merge-tree 探测 → rebase；绝不 force push）
- Terminology: 远程超前

### R4: 过程物规则化
- Status: Confirmed
- Scenario/Trigger: `.ams-docs/**` 新增过程物目录变体或松散脚本时。
- Behavior: 由 `.gitignore` 规则自动排除，不依赖人工记忆变体名。
- Acceptance:
  - [x] `/.ams-docs/**` + `!/.ams-docs/**/` + `!/.ams-docs/**/*.md` 生效：md 放行、非 md 排除
  - [x] 目录名纵深规则含 `_tools/ tools/ _work/ isolate/ verify-worktree/ tmp-probe/ _tmp/ _probe/`
  - [x] 文档说明 `.ams/sealed/` 由 `/.ams/*` 自动覆盖
- Terminology: 过程物目录

### R5: 证据留档口径变更
- Status: Confirmed
- Scenario/Trigger: 任务需要留档命令行输出等证据时。
- Behavior: 证据一律以 `.md` 形式写入任务目录。
- Acceptance:
  - [x] 第五节「边界裁量」已改为"证据一律以 `.md` 留档"，废止"单份 `.txt`/`.log` 入库"例外
  - [x] 文档说明该变更的原因（与自动忽略规则冲突）
- Terminology: 交付证据
