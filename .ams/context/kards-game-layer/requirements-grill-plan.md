# Requirements Grill Plan — KARDS 游戏层全面完备

> 状态：**已收敛**。定稿见 `requirements.md`；实施见 `implementation-grill-plan.md` 与三份实施文档。

## 结案记录

### Q1: 交付物形态  Completed
- 甲：定稿三文档（①完整流程 ②输入接口 ③hook 定义），分别交子 agent 执行；本阶段不改代码。

### Q2: 「忽视能力缺口」边界  Completed
- a：仅忽视 G1-G19；§4 的 #2/#3/#6 纳入，#1/#4/#5 不做；仅做 hook 集。

### Q3: 规则参数基线  Completed
- a：以 OrC 现行 4/5/4 ＋现点曲线为准，不对拍 kards-diy。

### Q4: "完整流程"范围  Completed
- c：最小闭环 ＋ mulligan ＋ 认输/投降；天气/疲劳/开局特殊卡/老兵升级不做。

### Q5: "输入接口"形态  Completed
- b：独立 Move/Attack 入口；统一"UI 只调预行为（`Begin*`）＋桥接应答驱动后续"两段式。
- 5-1＝a（独立 `Begin` 入口）；5-2：单位/指令统一"预打出＋打出"双层触发器（单位→槽位、指令→目标）。

### Q6: "全面 hook 定义"形态  Completed
- c：清单层（零行为改动）＋待补层（27 项对齐，标后置/不做）。

### Q7: 术语 `hook` 重载  Completed
- a：`Hook` 保留内核窄义（订阅契约）；广义面称"接入点/观察面/扩展点"并登记 CONTEXT。

### Q8: 验收与回归口径  Completed
- a：构建 0 警告、三测试项目全绿、`Orc.Game.Tests` 受控随改；`src/Orc` 不触动。

### Q9: 与既有冻结决策关系  Completed
- a：在其上继续；仅已授权受控变更（新增独立移动/攻击入口、`CardBase` targeter 下沉）。

### Q10: DoD 证据形态  Completed
- a：脚本化桥接端到端集成测试走通全流程；不引入可执行宿主。
