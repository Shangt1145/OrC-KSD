# 效果（三条编写路径）

卡牌效果的载体是**效果预制体**（`*.prefab.json`，`EffectSnapshot`，`schemaVersion = 2`）。三条编写路径，表达力从低到高：

1. **效果 DSL**（推荐起点）：`{ "template": "<模板id>", "fills": { "<槽位名>": [ ...op... ] } }`，经效果解析器的模板与编译链产出真实现。
2. **预制体 JSON**（复杂场景）：直接写 `EffectSnapshot` 结构。
3. **csx 源码**（逃生舱，默认关闭）：预制体内联一段 C# 脚本，经弱沙箱过滤后编译挂载。

超表达力时**必须显式失败**（返回诊断），不得静默降级为"空效果"。

## 触发起点：模板效果

引擎内置 31 个模板效果（`src/Orc.Game/EffectParsing/Templates/*.tpl.json`），模板 id 即触发时机：

| 模板 id 形态 | 含义 |
|---|---|
| `deploy_basic` | 部署时 |
| `death_basic` | 死亡时 |
| `played_basic` | 打出时 |
| `drawn_basic` | 被抽到时 |
| `discard_basic` / `burn_basic` / `hand_add_basic` | 弃置 / 爆牌 / 加入手牌 |
| `turn_start_basic` / `turn_end_basic`（含 `*_before_basic`） | 回合开始 / 结束 |
| `attack_basic` / `damaged_basic` / `combat_survived_basic` | 攻击 / 受伤 / 战斗存活 |
| `killed_basic` / `destroy_basic` | 击杀 / 被毁 |
| `position_basic` / `placed_basic` / `join_basic` / `load_basic` | 位置变化 / 放置 / 加入 / 装载 |
| `stat_basic` / `shuffle_basic` / `acted_basic` / `slot_gained_basic` / `slot_lost_basic` / `counter_basic` / `reveal_basic` / `veteran_basic` | 数值变化 / 洗切 / 行动 / 槽位得失 / 反制 / 揭示 / 老兵 |

用 `docs.read` 读本文件可看到全部模板 id 分节；`diag.hooks` 导出信号与判定器清单（事件名的权威来源）。

## 接线方式

- **挂到卡上**：卡数据体 `components` 里的 `effects` 组件声明"引用预制体 id"或"内联预制体文本"。
- **装载时机**：位置即声明——卡 `effects` 在卡装载期装载；词条绑定在授予期装载；运行期注入由效果逻辑触发。
- **运行期动态挂载**（沙盒常用）：`EffectRuntime.AttachPrefabAsync(target, prefabId, ct)` / `AttachSnapshotAsync(target, snapshotJson, ct)` / `CompileAttachAsync(target, dslJson, effectId, ct)`；卸载 `DetachEffectAsync`。挂载凭据随卡销毁自动撤销。

## 沙盒里的最小效果验证流程

1. `effect.write`（或 `card.write` 内联）写入 `pack/effects/<id>.prefab.json`。
2. `asset.validate` 校验（结构 + 引用完整性）。
3. `card.load` 把引用该预制体的卡注册进对局。
4. `sandbox.spawn` 造卡 → 用 `sandbox.emit`/`sandbox.damage` 构造触发场景。
5. `observe.segments` 看事件流段（因果条目）确认效果真的跑了。

## csx 逃生舱（默认关闭）

- 服务层启动参数 `--csx` 才启用；未启用时不要尝试。
- 沙箱为**尽力而为的弱沙箱（同进程、非安全边界）**：程序集白名单 + 文本级关键字黑名单 + 超时（逻辑中断）。
- 入口写法：脚本内定义具名变量 `HandleAsync`，值为 `Func<视图类型, Context, CancellationToken, Task>`。

## 常见坑

- **占位条件**：解析到但不可求值的条件会编译为 `if (false)`（效果空转）——沙盒验证时若"什么都没发生"，优先检查是否存在占位条件。
- **触发者语义**：效果引发的信号，其"触发者卡牌"是效果宿主卡；主动操作则为空。写效果时不要假设触发者总是施动单位。
