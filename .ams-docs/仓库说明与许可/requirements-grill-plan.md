# Requirements Grill Plan — 仓库说明文档与版权许可声明

> 本轮 grill 聚焦：**版权声明的完整性（R9）＋ 推送前置项（更新交接说明范围规则）**。
> 状态仅记未决项；已决项沉淀进 `requirements.md`。

## 已裁定（沉淀于 requirements.md，不再提问）

| 编号 | 裁定 |
|---|---|
| Q1 | 例外语义＝ **c 具名豁免**（仅指定的那一个 Godot 项目） |
| Q11 / R3a | 标识＝ **GitHub 仓库坐标** `https://github.com/Shangt1145/Godot-orcKsd` |
| Q11a / R3b | 边界＝ 仅该仓库；**不覆盖 fork/派生、不覆盖改名或迁移、覆盖该仓库自身产出的发行物**（Q14=b 修订了 Q11a 的"不覆盖 release 产物"） |
| Q11b / R3c | 放行程度＝ **ii 强豁免**（该仓库内对本库的私有修改亦不必公开） |
| Q11c / R3d | 落点＝ **仅本（引擎）仓库声明** |
| Q15 / R3e | 条款显式写"**该仓库改名或迁移即例外失效**" |
| Q2 / R2 | 版本＝ **AGPL-3.0-or-later** |
| Q4 / R4 | 版权行＝ `Copyright (C) 2026- hitman-OLD6`；**默认不含邮箱** |
| — / R4a | `Shangt1145` 与 `hitman-OLD6` 为**不同主体**；条款须分别显名授权的双方 |
| Q5 / R1、R10 | README **先中文后英文**；含工程构成、构建测试命令、许可与例外、免责段 |
| Q11c / R8 | 不为前端产出任何文件；前端无许可＋含 KARDS 官方素材＝**已知残留风险** |
| Q8 / R7 | **更新 `origin` 为 `https://github.com/hitman-OLD6/OrC-KSD.git` 并推送**；推送前走交接说明第六节 SOP，D1 扣下项与过程物不入库 |

## 基线事实（已查证，不提问）

- 仓库 `d:/OrC-KSD` 无 `LICENSE`／`README.md`；根目录另有 0 字节占位文件 `PROJECT.md`、`USER.md`。
- 提交作者分布（实测）：`hitman-OLD6 <2872740945@qq.com>` 56、`OrC-KSD UI <ui@local>` 1、`Codex <codex@localhost>` 1，跨度 2026-10-03 ～ 2026-10-13。
- 本机 `git remote origin` 仍指向 `https://github.com/Shangt1145/OrC-KSD.git`（引擎已移交至 `hitman-OLD6`）。
- 前端：`https://github.com/Shangt1145/Godot-orcKsd`；本机副本 `D:\Godot-orcKsd-main` 无 `.git`；交接说明记载的 `H:\Working Folder\OrC-KSD.Godot` 本机不存在；该仓库无许可证文件，`VERSION = 0.0.0-alpha.24`，工程族 `Kards.Ui.*` ＋ Godot 主工程。
- 前端素材来自 KARDS 发行包（576 张 PNG、53 个 mp3、11 个 nations JSON），非本项目可授权内容。
- 依赖：`Microsoft.CodeAnalysis.CSharp.Scripting 4.11.0`（MIT）、xUnit（Apache-2.0）等，无已知 copyleft 依赖。
- 待推送清单（实测 `git status`）：修改 `提交清单.md`；未跟踪 `.ams-docs/前端资产迁移/发布包/**`、`docs/初始设计/kards官方卡牌.json`（D1 扣下）、`scripts/`（D1 扣下）。

## Unresolved Questions

### Q17: 版权声明的完整性（R9） — In Progress（本轮主问题）

`Codex <codex@localhost>`、`OrC-KSD UI <ui@local>` 各有 1 个提交。候选处置：

- **a**│二者均为本人在本工作区指挥的自动化产物，**合并**进 `hitman-OLD6` 的版权行（单一版权人，不列贡献者）。
- **b**│视为外部贡献者，版权行仍写 `hitman-OLD6`，README 增设"贡献者"段保留其署名。
- **c**│版权行改为 `hitman-OLD6 and contributors` 之类，不主张单一版权人。

### Q18: 推送前的范围规则更新（R6） — Pending（本轮需确认）

推送前须让 `README.md`／`LICENSE`／`LICENSE-EXCEPTION.md` 通过交接说明第五节名单与第六节 SOP 白名单；候选：**a** 一并修订交接说明（推荐，该文档本就由本角色维护）；**b** 仅在本次提交时临时放行、不改文档（下次同步会再次报越界）。

### Q19: 法律术语是否登记进 `CONTEXT.md`（Q10） — Pending（本轮需确认）

`CONTEXT.md` 现为**领域术语表**（引擎/游戏层术语）。候选：**a** 不登记，把许可术语写进许可文件与 README（推荐）；**b** 登记"Godot 例外"条目。

### Q3: 许可文件的载体布局 — Pending（转实现 grill）

推荐：`LICENSE`（AGPL-3.0-or-later 官方全文，逐字）＋ `LICENSE-EXCEPTION.md`（具名附加许可，中英对照）；不使用单文件合并（避免 GitHub 许可识别失效）。是否要 `THIRD-PARTY-NOTICES.md`／给 261 个 `.cs` 加 SPDX 头／改 `.csproj` 元数据，一并确认。

### Q5: README 的载体形态与内容细节 — Pending（转实现 grill）

单文件 `README.md`（中文段＋英文段）还是 `README.md` ＋ `README.en.md`；是否填充空的 `PROJECT.md`／`USER.md`；是否含"当前阶段／已知限制"段。

### Q6: 第三方 IP 与商标免责段文本 — Pending（转实现 grill）

KARDS（1939 Games）与 Godot（Godot Foundation）商标免责的具体措辞；`docs/` 内 KARDS 分析文档是否被"仅覆盖原创代码"一句覆盖。
