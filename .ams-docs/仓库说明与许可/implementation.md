# 实现文档 — 仓库说明文档（README）与版权许可声明

实现依据：同目录 `requirements.md`（R1–R10 已全部裁定的部分）。本文件随实现 grill 逐轮细化，最终定稿为可执行计划。

## 一、交付文件清单

| # | 路径 | 作用 | 状态 |
|---|---|---|---|
| 1 | `README.md` | 对外入口：中文段 ＋ 英文段 | **已落盘** |
| 2 | `LICENSE` | AGPL-3.0-or-later 官方全文（**逐字，不增不删**） | **已落盘**（34523 字节 / 662 行） |
| 3 | `LICENSE-EXCEPTION.md` | 具名附加许可（中英对照） | **已落盘** |
| 4 | `.ams/context/github/交接说明.md` | 修订 §1 事实、§3 所有者、§5 允许名单、§6 白名单与暂存行、§10 快照 | **已完成** |
| 5 | `.ams-docs/仓库说明与许可/**` | 本任务过程文档（需求/实现/两份 grill plan） | 已完成 |

**不产出**：前端仓库的任何文件（R3d）；`THIRD-PARTY-NOTICES.md`（见 I3）；空文件 `PROJECT.md`／`USER.md` 不作改动（见 I2）。

## 二、`LICENSE` 取文与校验

- 来源：GNU 官方 `https://www.gnu.org/licenses/agpl-3.0.txt`（UTF-8 无 BOM，LF）。
- 校验：首行须为 `GNU AFFERO GENERAL PUBLIC LICENSE`，版本行含 `Version 3, 19 November 2007`；文本**不得**加入版权行或例外内容。
- 网络依赖：按交接说明第四节"直连优先、代理兜底"处理；取文后逐字比对官方文本，不手工改写。
- **实测（2026-10-14）**：直连一次取回成功。校验结果：34523 字节；首三字节 `32 32 32`（无 BOM）；**无 CRLF**（纯 LF）；662 行；`L1 = "                    GNU AFFERO GENERAL PUBLIC LICENSE"`、`L2 = "                       Version 3, 19 November 2007"`；含 `Additional Terms`（§7）与 `Remote Network Interaction`（§13）；无 HTML 包装。

## 三、`LICENSE-EXCEPTION.md` 正文（草稿 v1，待逐句确认）

```md
# 附加许可 · 具名项目例外（AGPL-3.0-or-later）

本文件是 OrC-KSD（下称"本程序"）版权人依 GNU Affero 通用公共许可证第 3 版
第 7 节（AGPL-3.0 §7）授予的**附加许可（additional permission）**。
本文件不替换、不修改 AGPL-3.0-or-later 的任何义务；它仅在本文明确写出的范围内，
对本文明确指定的受益人减免义务。

## 一、授权人与受益人

- 授权人：本程序版权人 **hitman-OLD6 <2872740945@qq.com>**（下称"授权人"）。
- 受益人：位于 **https://github.com/Shangt1145/Godot-orcKsd** 的仓库（下称"指定项目"），
  及其所有者、维护者与在该仓库内工作的人员。

授权人与指定项目的所有者**并非同一主体**。本文件是授权人对另一主体的单方授权。

## 二、授予的额外许可（豁免范围）

在指定项目范围内，授权人额外许可你：

1. **传递组合作品**：以自选条款复制、修改、传递"指定项目源码 ＋ 本程序（含你在其中对本程序的修改）"
   构成的组合作品，无需整体按 AGPL-3.0 授权；
2. **传递发行物**：以自选条款传递**由指定项目自身构建**的发行物（游戏/工具的二进制、数据包、
   安装包等）；
3. **不公开修改**：你在指定项目内对本程序所做的修改**无需公开**，无需向接收者提供对应源码；
4. **网络交互**：指定项目通过网络对外提供服务时，无需依第 13 条向用户提供源码。
5. 为免疑义，上述豁免同时及于 AGPL-3.0 第 4 条（传递原样副本）、第 5 条（传递合并作品）、
   第 6 条（禁止附加限制）与第 13 条（网络交互提供源码）中与本文件授予范围相冲突的要求；
   指定项目仓库内的任意分支、标签与提交均属"指定项目"。

## 三、不被本文件覆盖的部分（仍为完整 AGPL）

1. **分叉与派生**：指定项目被 fork、复制、镜像或据此另建仓库后，该副本不属于"指定项目"，
   其一切传递与网络服务行为仍受完整 AGPL-3.0-or-later 约束。
2. **改名或迁移**：指定项目仓库一旦改名、转移到其他账号或组织、或更换地址，本附加许可即行失效
   （见第五条），且**不自动跟随**其后继仓库。
3. **其他 Godot 项目**：除指定项目外的任何 Godot 项目（其他游戏、编辑器插件、工具等）不受本文件约束。
4. **非 Godot 使用**：把本程序改造或用作非 Godot 宿主（服务端、命令行、其他引擎等）不受本文件约束。
5. **本程序的其他分发**：本程序脱离指定项目的任何独立分发仍受完整 AGPL-3.0-or-later 约束。
6. **第三方内容**：本文件不涉及、也无权涉及非授权人可授权的内容——例如 KARDS 官方卡牌数据、
   名称、美术与音效（相关权利归 1939 Games）。
7. **商标**：本文件不授予任何商标权，也不表示授权人与指定项目、1939 Games 或 Godot Foundation
   之间存在隶属、赞助或背书关系。

## 四、与 AGPL 的关系

1. 本文件是附加许可，不替换 AGPL-3.0-or-later。对除指定项目以外的所有主体，
   本程序的许可**仍是完整的 AGPL-3.0-or-later**。
2. 未在第二条明确列出的义务一律继续适用。
3. 任何人依 AGPL-3.0-or-later 已获得的权利不因本文件而减少。
4. 依 AGPL-3.0 第 8 条被终止的授权不因本文件而恢复。
5. 接收者可依 AGPL-3.0 第 7 节自行从其副本中移除本附加许可。

## 五、生效、失效与修订

1. 本文件随本程序发布之日起生效。
2. 指定项目仓库一旦改名或迁移，本附加许可自动失效；失效后基于本程序的传递行为须重新满足
   AGPL-3.0-or-later 的全部义务。
3. 仅授权人可修订本文件，修订以本仓库内的文件提交形式作出。
```

英文对照段：以同义英文逐条对照（`Additional Permission — Named Project Exception`），措辞须与中文段一一对应，不得增删条款。

## 四、`README.md` 大纲（待定稿）

```md
# OrC-KSD · 卡牌游戏引擎
> 一句话定位 + 许可一句话（AGPL-3.0-or-later，含一处具名附加许可）

## 这是什么        （引擎内核 Orc ＋ KARDS 风格游戏层 Orc.Game；非商业发行物，无官方卡牌数据）
## 工程构成        （表：Orc / Orc.Game / Orc.Script / Orc.Packages / Orc.UI* / samples / tests）
## 构建与测试      （dotnet build OrcEngine.slnx --no-incremental；dotnet test OrcEngine.slnx）
## 示例            （samples/Orc.Game.Sample 运行方式）
## 许可            （AGPL-3.0-or-later；LICENSE-EXCEPTION.md 的适用范围与未覆盖项；失效条件）
## 第三方依赖与商标（Microsoft.CodeAnalysis.CSharp.Scripting → MIT；xUnit → Apache-2.0；
                     KARDS 商标归 1939 Games；Godot 商标归 Godot Foundation；无隶属或背书）
## 反馈            （Issue / PR）

---（English，自 What this is 起逐节对应）
```

**关键段落草稿·许可段（中文）**：

> 本仓库原创代码以 **GNU Affero 通用公共许可证第 3 版或其后续版本（AGPL-3.0-or-later）** 授权，
> 全文见 [`LICENSE`](LICENSE)。
> 另有一项**具名附加许可**（[`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md)）：它**只**对
> `https://github.com/Shangt1145/Godot-orcKsd` 这一仓库（含其自身构建的发行物）放宽 AGPL 义务，
> 且该仓库改名或迁移即失效。**除此之外，本项目的许可就是完整的 AGPL-3.0-or-later**——
> 其他任何 Godot 项目、非 Godot 使用、以及本项目的其他分发，均不受该附加许可影响。

**关键段落草稿·免责段（中文）**：

> 本项目是独立的社区实现，**不是** KARDS 的官方产物；与 1939 Games、Godot Foundation 均无隶属、
> 赞助或背书关系。"KARDS" 是 1939 Games 的商标，"Godot" 是 Godot Foundation 的商标，此处仅作指称之用。
> 本仓库**不包含** KARDS 官方卡牌数据、名称、美术或音效；许可范围仅及于本项目原创代码。

## 五、`交接说明.md` 修订点（待确认，见 I7）

1. §1 项目背景：补一句"仓库已移交至 `hitman-OLD6`"。
2. §3 远程仓库与权限：所有者改为 `hitman-OLD6`；移除已过期的"无写权限/需邀请"表述，或标注其历史时效。
3. §5 允许入库：根目录文档名单追加 `README.md`、`LICENSE`、`LICENSE-EXCEPTION.md`。
4. §6 SOP 白名单正则：追加 `README\.md`、`LICENSE(-EXCEPTION\.md)?`。
5. §10 状态快照：更新提交历史与日期。

## 六、提交与推送（按交接说明第六节 SOP）

```
①  git status -sb / git --no-pager log --oneline -5
①b git ls-remote origin refs/heads/main
②  git add README.md LICENSE LICENSE-EXCEPTION.md .ams-docs .ams/context
③  白名单 + 黑名单 + .ams-docs 仅 md + 目录分组核对（**单独执行，亲眼看输出为空**）
④  git commit -F .git/COMMIT_MSG.txt        （信息：docs: 新增仓库 README 与 AGPL 具名附加许可）
⑤  git remote set-url origin https://github.com/hitman-OLD6/OrC-KSD.git  → git push
⑥  git ls-remote origin refs/heads/main / git rev-parse HEAD / git status -sb
```

**必须扣下**：`docs/初始设计/kards官方卡牌.json`、`scripts/`（D1）；`.ams-docs/前端资产迁移/发布包/**`（过程物，`.gitignore` 已覆盖，提交前仍需核验）。

## 六之二、实现 grill 结论（2026-10-14，用户"全部同意"）

| 编号 | 裁定 |
|---|---|
| I1 | `README.md` = **单文件**，中文段在前、英文段在后 |
| I2 | 空文件 `PROJECT.md`／`USER.md` **不动** |
| I3 | **不新增** `THIRD-PARTY-NOTICES.md`（依赖仅两类，README 段落承载） |
| I4 | **不给** 261 个 `.cs` 加 SPDX 头（§7 通知由 README 许可段指向 `LICENSE-EXCEPTION.md` 满足） |
| I5 | **不改** `.csproj` 元数据 |
| I6 | 保留"仓库内任意分支/标签/提交均属指定项目"澄清；保留"接收者可依 §7 移除本附加许可"；英文段逐条对应 |
| I7 | 交接说明 **5 处全改** |
| I8 | 提交粒度 = **两个提交**（`docs:` ＋ `chore:`） |
| I9 | 推送前**先贴出待提交清单与校验输出**等用户点头 |

## 七、验收清单

- [ ] `LICENSE` 与官方文本逐字一致，无额外内容
- [ ] `LICENSE-EXCEPTION.md` 的授权人/受益人/豁免项/未覆盖项/失效条件齐备，中英一致
- [ ] `README.md` 中英双语完整，许可段与免责段与许可文件口径一致
- [ ] 五场景压力测试结论与条款一致（见 R3 验收）
- [ ] 交接说明的名单与白名单已更新，`README.md`／`LICENSE*` 不再被判越界
- [ ] 推送后 `git ls-remote` 与本地 HEAD 一致，`status -sb` 无差异
- [ ] 扣下项与过程物确认未入库
