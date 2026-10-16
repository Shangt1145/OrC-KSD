# Implementation — 交接文档更新与 GitHub 托管业务永久交接

实现 grill 已闭合（无遗留问题）：规则先落地并实测，再改写权威文档，最后按 SOP 提交推送。**全部步骤已完成（2026-10-09）**。

## Scope
- Target: `.gitignore`、`.ams/context/github/交接说明.md`、`.ams/context/github/` 下 4 份对齐文档
- Excludes: 业务源码、`OrcEngine.slnx`、`docs/` 正文、D1 扣下项、已封存 `Orc.Lua`

## Implementation Steps

### S1: `.gitignore` 落地过程物规则（D2）
- Status: Done
- Target: `.gitignore`
- Approach: 追加两类规则——① `.ams-docs` 默认拒绝 + 仅放行 `*.md`；② 目录名纵深规则（8 种变体）。用 UTF-8 无 BOM 追加（`AppendAllText` + `UTF8Encoding($false)`）。
- Acceptance:
  - [x] `git diff -- .gitignore` 仅显示新增行，无既有行改动
  - [x] `git check-ignore -v` 对 `.ams-docs/**/*.md` 返回 `!/.ams-docs/**/*.md`（即不被忽略）
  - [x] 对 `_probe/probe.cs`、`_tmp/scan3.py`、松散 `run_test_suites.py` 均返回忽略规则
- Rationale: 目录名变体 6 天新增 3 种，规则兜底优于人工记忆。
- Terminology: 过程物目录

### S2: 验证规则覆盖面
- Status: Done
- Target: 工作区
- Approach: 统计未跟踪项，并核查 `.ams-docs` 剩余未跟踪项是否全为 `.md`。
- Acceptance:
  - [x] 未跟踪项 182 → **85**
  - [x] `.ams-docs` 未跟踪项**非 md 者为 0**
  - [x] 已跟踪文件中无过程物（故无需 `git rm --cached`）
- Rationale: 规则的唯一价值是"不靠人记得排除"。
- Terminology: 过程物目录

### S3: 改写 `交接说明.md`（Q1=c）
- Status: Done
- Target: `.ams/context/github/交接说明.md`
- Approach: 全篇修订——① 定位改为"任何接手者（人或 agent）"；② 新增第二写手与多写手要点；③ 第四-1 条改为"直连优先、代理兜底"并作废旧说法；④ 第五节改为规则化排除 + 证据以 md 留档；⑤ SOP 增 ①b 远程探测、⑤ 条件代理、"别人先推了"处置、③ 增 b2 复核；⑥ 排障表增 4 行；⑦ 第八节 D1~D6 收敛 + O1/O2；⑧ 第十节快照更新；⑨ 第十一节增"手工排除疲劳""远程超前未察"两例；⑩ 新增第十二节交接验收清单。
- Acceptance:
  - [x] 全文无"必须先探测代理端口"等与实测矛盾的表述
  - [x] 工程表标注 `Orc.Lua` 已封存、说明 `OrcEngine.slnx` 为空
  - [x] 含第十二节可勾选验收清单（10 项）
- Rationale: "永久交接"要求文档脱离"绑定某个会话"的语境，并能自证可执行。
- Terminology: 永久交接

### S4: 补齐对齐文档
- Status: Done
- Target: `.ams/context/github/requirements.md`、`requirements-grill-plan.md`、`implementation.md`、`implementation-grill-plan.md`
- Approach: 记录本轮 grill 的决策链（含实施期新增的 Q4 规则形态选择）。
- Acceptance:
  - [x] R1~R5 均 Confirmed 且验收项打勾
  - [x] grill 计划中未决问题清零
- Rationale: 永久交接需保留"为什么这样做"的决策链。
- Terminology: 永久交接

### S5: 提交与推送
- Status: Done
- Target: git 仓库
- Approach: `chore:`（`.gitignore`）＋ `docs:`（5 份文档）；按 SOP ①b 先探测远程，再推送并核验。
- Acceptance:
  - [x] 两个提交；③ 校验两段为空
  - [x] 推送后 `ls-remote` = `HEAD`，`status -sb` 无 ahead
- Rationale: 沿用既有 SOP 与提交粒度。
- Terminology: github 范围

## 验证结果（2026-10-09）
- `.gitignore`：新增规则生效；未跟踪项 182 → 85；`.ams-docs` 未跟踪项非 md 归零
- 已跟踪文件中过程物数量：0
- 提交前校验：白名单越界 0、黑名单命中 0

---

## 追加轮次（2026-10-16）——三项收口

用户指令：`1：a`、`2：同意`、`3：改`。

### S6: `.sln` 补齐 MCP 三工程（1a）
- Status: Done
- Target: `OrcEngine.sln`
- Approach: 为 `Orc.Mcp`、`Orc.Mcp.Tests`、`Orc.Mcp.Stdio` 生成 GUID，补 `Project`／`ProjectConfigurationPlatforms`／`NestedProjects` 三节。
- Acceptance:
  - [x] `.sln` 与 `.slnx` 工程数均为 15
  - [x] `dotnet build OrcEngine.sln` 输出 `Orc.Mcp`／`Orc.Mcp.Stdio`／`Orc.Mcp.Tests` 三行
- Rationale: 修掉「用 `.sln` 构建静默漏编 MCP」的陷阱。

### S7: 忽略外发交付包（2）
- Status: Done
- Target: `.gitignore`
- Approach: 追加 `/.ams-docs/**/发布包/`，**置于 `!/.ams-docs/**/*.md` 之后**（目录被排除后其中的 md 无法再放行）。
- Acceptance:
  - [x] `git check-ignore` 命中发布包内 md
  - [x] 普通任务 `.md` 不受影响（`check-ignore` 无输出）
  - [x] `.ams-docs` 未跟踪残留为 0
- Rationale: 发布包是交付前端（Godot）侧的外发物，非本仓库成果；此前每轮靠手工排除。

### S8: 修正 SOP ③b 的 `quotepath` 缺陷（3）
- Status: Done
- Target: `.ams/context/github/交接说明.md`（§六 ③b、§七 排障表、§十一 代价记录）
- Approach: ③b 补 `-c core.quotepath=false` 并 `Trim('"')`；黑名单模式增 `发布包`／`orc-sandbox`；排障表与代价记录各增对应条目。
- Acceptance:
  - [x] ③b 命令行含 `core.quotepath=false`
  - [x] 第十一节新增「校验静默失效」一例（含 D1 项险入库的经过）
- Rationale: 未加该开关时，模式里的中文（`官方卡牌`／`发布包`）**永不匹配**，校验形同虚设——2026-10-16 实测漏报。

### 追加轮次验证结果（2026-10-16）
- `.sln`／`.slnx` 工程数：15 / 15；`dotnet build OrcEngine.slnx` 与 `.sln` 均 0 错误
- 测试（`.slnx`）：1523 通过 / 0 失败
- 发布包：`git ls-files` 为空；`.ams-docs` 未跟踪残留 0

