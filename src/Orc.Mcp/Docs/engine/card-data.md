# 卡牌数据体（*.card.json）

单卡一文件、明文 UTF-8；服务层写出时规范化（2 空格缩进、LF、尾换行）。读取宽容：键名大小写不敏感、容忍注释与尾逗号。

## 文件骨架

```json
{
  "schemaVersion": 1,
  "id": "demo.rifleman",
  "name": "示例步枪兵",
  "components": [
    { "component": "typeCategory", "type": ["infantry"] },
    { "component": "factionCost", "faction": "germany", "kredits": 1 },
    { "component": "battleStats", "operationCost": 1, "attack": 2, "defense": 3 },
    { "component": "tagData", "rarity": "standard" }
  ]
}
```

- `schemaVersion`：当前为 `1`。不符＝结构化失败（不静默降级）。
- `id`：卡定义注册 id（同一卡包内唯一；跨卡包引用时由卡包前缀化）。
- `name`：卡牌名称（非空）。
- `components`：组件定义集，**声明序**即装配序（两段式加载：内置 loader 先、其余按声明序）。

## 内置组件（六个）

| 组件名 | 作用 | 关键字段 |
|---|---|---|
| `typeCategory` | 卡类别与单位类型 | `type`（字符串数组，如 `infantry`/`tank`/`artillery`；指令卡省略） |
| `factionCost` | 阵营与部署花费（**必填**） | `faction`（阵营 id，如 `germany`/`neutral`）、`kredits`（部署花费） |
| `battleStats` | 战斗数值 / 行动花费 | `attack`、`defense`、`operationCost`（行动花费） |
| `tagData` | 元数据（**必填**：稀有度） | `rarity`（`standard`/`limited`/`special`/`elite`）、`tags`（子类别字符串数组，可省） |
| `keywords` | 词条声明 | 词条 id 列表与参值（见「词条」文档） |
| `effects` | 效果声明 | 引用效果预制体 id 或内联预制体文本 |

- 必需性：`factionCost` 与 `tagData` 缺失＝定义期 fail-fast 拒绝。
- 未知组件名＝**告警并跳过该组件**（不阻断整张卡）。

## 阵营（faction）

`germany` / `soviet` / `usa` / `britain` / `japan` / `france` / `italy` / `poland` / `finland` / `anzac` / `neutral`。

## 工作流（沙盒）

1. 用 `card.list` 看沙盒卡包里已有哪些卡（含解析失败明细）。
2. 写/改 `cards/<id>.card.json`（路径相对沙盒根；写工具只允许沙盒根内）。
3. `card.load` 逐卡注册进当前沙盒对局（**只追加不覆盖**：改卡须换新 id）。
4. `sandbox.spawn` 造卡放置 → `observe.*` 观察 →（必要时）`sandbox.emit` 构造触发场景。
