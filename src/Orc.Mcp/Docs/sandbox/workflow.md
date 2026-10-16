# 沙盒创作工作流（卡牌编辑者）

本服务以**沙盒模式**运行：你是卡牌编辑者，可建一个沙盒对局、动态注册新卡与效果、直接构造验证场景。

## 0. 先读限制（每次会话建议先做）

调用 `service.info`，读 `limitations` 数组与门控开关（`promoteEnabled`/`csxEnabled`）。

关键限制（记住这几条就够）：

- 插件**不可热重启**；插件变更须人工重启进程。
- 同一进程**只有一个活跃对局**。
- 沙盒的**进程级变更不回滚**（词条注册面、卡包目录），用 `sandbox.state` 看留痕。
- **写入只允许落在沙盒根内**；正式资产根只读（提升能力默认关闭）。
- csx 默认关闭（弱沙箱、非安全进程边界）。

## 1. 建沙盒局

```
sandbox.create
```

- 生成脚手架：`<沙盒根>/pack/{manifest.json,cards/,effects/}`。
- 沙盒局**默认无卡**；脚手架里有一张 `sandbox.placeholder` 占位卡，仅为满足引擎"卡组名单非空"的构造契约（起手 0 张，故不进手牌）。
- 建局后即处于"进行"态。

## 2. 读文档与参考

```
docs.list                 → 文档清单（含分节目录）
docs.read {id, offset, limit}
diag.hooks                → 事件名/判定器名/触发器分层（权威清单，代码生成）
diag.entry_points         → 引擎公开输入入口全集
card.list                 → 沙盒卡包内已有卡及其解析摘要
effect/prefab 参考：读 pack/effects/*.prefab.json 与文档 effects
```

## 3. 写卡（原文 JSON + 校验）

```
card.write { path: "pack/cards/demo.rifleman.card.json", json: "{...}" }
```

- 写入前解析校验：JSON 非法或结构不符＝拒绝落盘。
- 写出时规范化（固定 schemaVersion、camelCase、LF、2 空格缩进）。
- 覆盖既有文件须显式 `overwrite: true`。

写效果：

```
effect.write { path: "pack/effects/demo.x.prefab.json", json: "{...}" }
```

## 4. 注册进当前对局（逐卡）

```
card.load                 → 把 pack/cards 下全部卡逐卡注册进当前对局
card.load { files: [...] } → 只注册指定文件
```

- **只追加不覆盖**：改卡必须换新 id（例如 `demo.rifleman.v2`），旧定义保持原样。
- 已在库中的 id 会被跳过（返回 `alreadyRegistered`）。
- 注册即对当前沙盒局生效（不需要重建对局）。

## 5. 构造场景与驱动

```
sandbox.spawn { definitionId, owner, zone: "hand" | "support" }
sandbox.kill  { ref }
sandbox.damage { ref, amount, sourceRef? }
sandbox.set_points { owner, delta }
sandbox.emit { event, payload }
match.end_turn
match.concede { owner }
```

- `sandbox.*` 是**调试能力**：绕过输入/费用/相位门禁，但仍走引擎既有管线（伤害走伤害管线、销毁走销毁入口、事件走总线）。
- 需要选位时按 targeting 文档进行两次调用（`targeting` → `target.submit`）。

## 6. 观察验证

```
observe.view              → 摘要
observe.snapshot          → 全量实体（含组件字段；判断"数值/组件是否真的变了"）
observe.segments          → 事件流段（判断"效果跑了哪些信号"）
diag.audit_chain          → 触发器关系图（判断"谁挂在哪个信号上"）
```

## 7. 收尾

```
sandbox.dispose           → 销毁沙盒局（释放活跃对局位）
asset.promote {...}       → 仅在 --promote 开启且无活跃沙盒局时可用：
                            把沙盒产物写入**已存在**的正式卡包目录（不隐式覆盖）
```

## 常见问题

| 现象 | 可能原因 |
|---|---|
| `card.load` 报"已注册（重复注册被拒绝）" | 该 id 已在库中——改卡请换新 id |
| 效果"什么都没发生" | 触发时机未到；或 DSL 编译出**占位条件**（`if (false)`）；先看 `observe.segments` |
| 动作返回 `pending-targeting` | 上一个动作在等目标选择——先 `target.submit`/`target.cancel` |
| 写了新文件但当前对局看不见 | 需要 `card.load` 逐卡注册（热加载只对新建对局生效） |
| 词条注册后被拒绝 | 与既有词条同名（不得劫持）；或跨包已被他人注册 |
