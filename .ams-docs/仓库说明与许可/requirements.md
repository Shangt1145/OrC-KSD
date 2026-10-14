# Requirements — 仓库说明文档（README）与版权许可声明

为公开仓库 `OrC-KSD` 建立对外入口 README 与版权/许可声明：以 **AGPL-3.0-or-later ＋ 对具名 Godot 项目（`https://github.com/Shangt1145/Godot-orcKsd`）的附加许可豁免** 为核心，使"引擎开源、该前端可自选许可"的意图在法律文本与说明文档上同时成立且不自相矛盾。

## Scope

- Includes：
  - 仓库根 `README.md`（对外入口说明，先中文后英文）。
  - 许可与版权声明文件：AGPL-3.0-or-later 官方全文（逐字）＋ 具名豁免条款。
  - 配套声明段：版权归属、第三方依赖与商标免责（见 R4、R5）。
  - 与推送配套的仓库事实与范围规则同步（见 R6、R7）。
- Excludes：
  - 不修改业务代码与测试（`src/**`、`tests/**`、`samples/**` 逻辑不变）。
  - 不改动 `OrcEngine.sln(x)` 的工程构成。
  - 不为 KARDS 官方卡牌数据/导入脚本（交接说明 D1 扣下项）做任何入库动作。
  - 不为该 Godot 前端仓库产出任何文件（见 R8）。
  - 不把法律术语登记进根 `CONTEXT.md`（该文件为领域术语表，见 R10）。
  - 不替代法律意见（文本不承诺法律效力审查）。

## Constraints

- 公开不可逆：任何入库内容即时对全网可见；不得包含明文密钥、第三方受限内容。
- 许可文本保真：AGPL 官方全文须逐字不改；例外须以"附加许可（additional permission）"形式书写，不得删改原协议义务。
- 编码与格式：UTF-8 无 BOM；与仓库既有中文 Markdown 风格一致。
- 一致性：README 中的许可描述必须与许可文件正文一致，不得出现两套口径；中英两版内容不得互相矛盾。
- 仓库既有运营约束（交接说明第五节的入库范围与 SOP 白名单）须先同步更新，否则新增根文档会被判为越界。

## Requirement Items

### R1: 仓库对外入口说明（README.md）
- Status: Confirmed（内容细节见实现 grill）
- Scenario/Trigger: 任何人首次打开 `https://github.com/hitman-OLD6/OrC-KSD`。
- Behavior: 根 `README.md` 应说明本项目是什么（Orc 引擎内核 ＋ KARDS 风格游戏层）、由哪些工程构成、如何构建与运行示例、许可与例外是什么、以及内容边界（不含官方卡牌数据）；**先中文、后英文**。
- Acceptance:
  - [ ] 仓库根存在 `README.md`，且顶部即含项目定位与许可一句话。
  - [ ] 含工程构成表（与 `OrcEngine.sln` 实际工程一致）。
  - [ ] 含可执行的构建/测试命令（`dotnet build/test OrcEngine.slnx`）。
  - [ ] 含许可章节，指向许可文件，并复述具名豁免的适用范围与未覆盖项。
  - [ ] 含第三方依赖与商标免责段。
  - [ ] 中文段与英文段均完整、无占位符，且相互一致。
- Terminology: —

### R2: AGPL 开源许可声明
- Status: Confirmed
- Scenario/Trigger: 任何人使用、修改、分发本项目。
- Behavior: 项目原创代码以 **AGPL-3.0-or-later** 授权；仓库内提供协议官方全文（逐字），并说明具名豁免的存在。
- Acceptance:
  - [ ] 仓库根存在协议全文文件，内容与官方文本逐字一致。
  - [ ] README 与许可文件中的版本标识一致（`or-later`）。
  - [ ] 已说明"含附加许可，故非纯标准 AGPL"，并接受 GitHub 对许可的识别表现。
- Terminology: —

### R3: 具名豁免条款（受益人＝指定的那一个 Godot 项目）
- Status: Confirmed
- Scenario/Trigger: `https://github.com/Shangt1145/Godot-orcKsd` 这一仓库使用、链接、修改或再分发本项目。
- Behavior: 以 AGPL 附加许可形式，仅对该**具名仓库**豁免 AGPL 义务，使其整体可按自选许可存在与分发；其余 Godot 项目与非 Godot 宿主不受此豁免，仍受完整 AGPL 约束。
- Acceptance:
  - [ ] 受益人由唯一标识确定，不使用"以下/任何 Godot 项目"等泛化措辞。
  - [ ] 明确列出被豁免的义务（§5 传递与合并作品、§13 网络披露等）与**未被豁免**的义务。
  - [ ] 五个具体场景的文本结论一致：① 该仓库源码公开并按自选许可；② 该仓库自行构建发行物；③ 该仓库被他方 fork 或改名；④ 第三方自己的 Godot 项目使用本库；⑤ 把本库改造成非 Godot 服务端对外服务。
  - [ ] 文本形式为附加许可，未删改 AGPL 原文义务。
- Terminology: —

### R3a: 具名对象的标识方式
- Status: Confirmed
- Behavior: 豁免对象以 **GitHub 仓库坐标 `https://github.com/Shangt1145/Godot-orcKsd`** 唯一标识；条款中不使用 `OrC-KSD.Godot`／`Godot-orcKsd-main`／`Kards.Ui.*` 等本机或工程族名号作为识别依据。
- Acceptance:
  - [ ] 标识可被第三方独立核验（含仓库坐标或等价可定位信息）。
  - [ ] 全仓库文档中对该项目的称谓统一，无三名并存的歧义。
- Terminology: —

### R3b: 豁免边界（派生、改名、发行物）
- Status: Confirmed
- Behavior: 例外**不覆盖**任何分叉/派生版本，**不覆盖**改名或迁移后的仓库；但**覆盖该仓库自身产出的发行物**（游戏/工具的构建分发物）。
  - 修订记录：Q11a 原答"不覆盖 release 产物"，经 Q14 裁定为 **b（纳入该仓库自身产出的发行物）**，故本项以 R3b 现文为准；"不覆盖 release 产物"仅保留"不覆盖他方 fork 的产物、不覆盖本引擎仓库对外分发物"的含义。
- Acceptance:
  - [ ] 三种情形各有明确文字结论，无留白。
  - [ ] 条款明确区分"该仓库自身产出的发行物（覆盖）"与"fork/改名后的产物（不覆盖）"。
- Terminology: —

### R3c: 放行程度（本库修改是否仍需开源）
- Status: Confirmed
- Behavior: 采用**强豁免**——该仓库内对本库的私有修改亦不必公开；豁免不限于"仅原版本库"。
- Acceptance:
  - [ ] 裁定结果在条款中以单句可辨识的措辞表达。
  - [ ] README 对该点的复述与条款一致。
- Terminology: —

### R3d: 例外文本的落点
- Status: Confirmed
- Behavior: 例外**仅在本（引擎）仓库声明**，不为该 Godot 前端另出文件。
- Acceptance:
  - [ ] 落点与本机可行性一致（前端副本无 `.git`，不产出前端文件）。
  - [ ] 前端自身的许可状态作为**已接受的例外项**记录（见 R8）。
- Terminology: —

### R3e: 标识失效的处置（改名/迁移）
- Status: Confirmed
- Behavior: 条款明确写出"**该仓库改名或迁移即本例外失效**"，不设"自动跟随其后继仓库"的表述。
- Acceptance:
  - [ ] 条款含"改名/迁移即失效"的显式句子。
  - [ ] README 复述该失效条件。
  - [ ] 与 R3b「不覆盖改名」不矛盾。
- Terminology: —

### R4: 版权归属声明
- Status: Confirmed
- Scenario/Trigger: 任何人需要确认权利人身份以取得额外授权或履行保留声明义务。
- Behavior: 版权行采用固定署名、开放式年份区间与联系邮箱：`Copyright (C) 2026- hitman-OLD6 <2872740945@qq.com>`。
- Acceptance:
  - [ ] 版权行与上述文本一致，无占位符遗留。
  - [ ] 与 `git log` 实际作者情况不冲突（见 R9）。
- Terminology: —

### R4a: 授权人与受让账户的口径
- Status: Confirmed
- Scenario/Trigger: 例外的受让人是 `Shangt1145` 名下的仓库，而版权人是 `hitman-OLD6` —— 用户已确认二者为不同主体。
- Behavior: 条款须显式写成"**由版权人（`hitman-OLD6`）向该仓库（`Shangt1145/Godot-orcKsd`）授予附加许可**"，不使用"本版权人的项目"一类暗示同一性的措辞。
- Acceptance:
  - [ ] 条款中授权人与受让人分别显名，措辞不暗示二者同一。
  - [ ] README 复述时不出现"官方前端"之类暗示所有关系的描述。
- Terminology: —

### R9: 版权声明的完整性与历史作者处置
- Status: Confirmed
- Scenario/Trigger: 58 个提交中 56 个为 `hitman-OLD6`，另有 `Codex <codex@localhost>`、`OrC-KSD UI <ui@local>` 各 1 个。
- Behavior: 依裁定 **a**——这 2 个提交视为版权人在本工作区指挥下的自动化产物，**并入** `hitman-OLD6` 的单一版权行，README 不设贡献者段落。
- Acceptance:
  - [ ] 版权行主张单一版权人，与上述裁定一致。
  - [ ] README 不出现与单一版权行矛盾的贡献者表述。
- Terminology: —

### R5: 第三方内容与商标边界
- Status: Draft（措辞见实现 grill）
- Scenario/Trigger: 读者误解"AGPL"覆盖到 KARDS 官方素材或把本项目当作 KARDS 官方产物。
- Behavior: 显式声明本许可仅覆盖本项目原创代码；不含 KARDS 官方卡牌数据/名称/美术/音效（权利归 1939 Games），并含 KARDS/Godot 商标免责段。
- Acceptance:
  - [ ] 声明覆盖"原创代码 / 第三方内容"的边界。
  - [ ] 含商标免责句，且不暗示与 1939 Games 或 Godot 基金会存在隶属/背书关系。
  - [ ] 覆盖范围已与仓库实际内容逐项核对（含 `docs/` 内 KARDS 分析文档）。
- Terminology: —

### R8: 前端项目的著作权状态处置
- Status: Confirmed（范围外，记录残留风险）
- Scenario/Trigger: 指定的 Godot 前端仓库自身无任何许可证文件（＝默认保留所有权利），且含自 KARDS 发行包提取的官方素材。
- Behavior: 依 R3d 裁定，**不**为该前端产出任何文件；其许可状态由该仓库所有者自行处置。本仓库仅在 README 中如实说明例外的适用范围与不含第三方素材，不对前端内容作任何授权表述。
- Acceptance:
  - [ ] 本仓库文档中不含对该前端内容的授权表述。
  - [ ] 前端自身无许可证、且含 KARDS 官方素材这一事实，作为**已知残留风险**记录在交付说明中。
- Terminology: —

### R6: 与仓库运营规则的一致性
- Status: Confirmed
- Scenario/Trigger: 同步 agent 按 `.ams/context/github/交接说明.md` 的范围规则提交/推送；本任务将实际执行推送（R7）。
- Behavior: 依裁定 **a**——**一并修订交接说明**：第五节「允许入库」名单与第六节 SOP 白名单正则纳入新增根文档，并把 §3 已过期的所有者/权限描述更新为移交后事实。
- Acceptance:
  - [ ] `README.md`、`LICENSE`、`LICENSE-EXCEPTION.md` 在同步规则下**不再**被判为越界。
  - [ ] 交接说明中已过期的所有者/权限/署名描述（§3）已更新或标注。
- Terminology: —

### R7: 交付与提交行为
- Status: Confirmed
- Scenario/Trigger: 文件落盘完成。
- Behavior: **将本机 `origin` 配置为移交后的新地址 `https://github.com/hitman-OLD6/OrC-KSD.git`，并执行推送**；推送前按交接说明第六节 SOP 逐项校验，D1 扣下项（`docs/初始设计/kards官方卡牌.json`、`scripts/`）与过程物一律不入库。
- Acceptance:
  - [ ] `git remote -v` 指向新地址。
  - [ ] 推送前校验两段输出均为空（白名单/黑名单）。
  - [ ] `git ls-remote origin refs/heads/main` 与本地 HEAD 一致，`git status -sb` 无差异。
  - [ ] 推送内容不含 D1 扣下项与过程物。
- Terminology: —

### R10: 双语形态
- Status: Confirmed（载体形态见实现 grill）
- Scenario/Trigger: 中文读者与国际读者同时访问仓库。
- Behavior: 内容**先中文、后英文**；两版信息量对等（许可、例外、免责、构建方式均须出现）。法律术语**不**登记进根 `CONTEXT.md`。
- Acceptance:
  - [ ] 中英两版同时存在且顺序为中文在前。
  - [ ] 两版对例外范围与免责内容的表述一致。
  - [ ] `CONTEXT.md` 未被改动。
- Terminology: —
