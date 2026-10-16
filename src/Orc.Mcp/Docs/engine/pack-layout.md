# 卡包目录约定与清单

## 目录结构

```
<卡包根>/
  manifest.json          # 包清单（kind/id/guid/version/dependsOn/assets）
  cards/                 # 卡数据体（*.card.json；只扫描顶层）
  cards-<后缀>/          # 附加卡目录（可选；按目录名排序）
  effects/               # 效果预制体（*.prefab.json）
  texts.json             # 卡面文本（可选；供效果解析器编译）
  keywords.json          # 词条声明与效果绑定（可选）
```

沙盒的目录固定为 `<沙盒根>/pack/{manifest.json,cards/,effects/}`（由 `sandbox.create` 的脚手架生成）。

## manifest.json

```json
{
  "kind": "cardpack",
  "id": "sandbox",
  "guid": "8f2c1f44-....",
  "version": "0.0.1",
  "dependsOn": [],
  "assets": []
}
```

- `kind`：`plugin`（插件，**只能冷加载**）或 `cardpack`（卡包，可热加载）。
- `id`：包 id（包内资产名的一部分）。
- `guid`：**跨版本稳定**的包身份；资产全名里的 `@{guid 前 8 位}` 取自此。
- `version`：`x.y.z` 语义化版本；依赖约束写法 `1.*.*` / `1.^.*`（主版恒锁定）。
- `assets[]`：`{type,name,path,scope,phase}`；`type ∈ json|csx|asm`；`scope ∈ process|match`；`phase ∈ preCommand|postCommand`。

> 沙盒脚手架的 `guid` 一次性生成并持久化——**不要手改**，否则资产全名会变。

## 资产全名（跨包引用用）

```
{类型名}：{资产名}@{包GUID8}#{版本}
```

例如 `json：demo.rifleman@8f2c1f44#0.0.1`（分隔符为全角冒号）。

## 热加载语义（重要）

- 卡包热加载**只对新建对局生效**：已建对局的资产快照在装配期冻结，永不改变。
- 因此"改了卡想让当前对局看到"必须走**逐卡注册**（`card.load`），而不是热加载卡包。
- 对局活跃期间调用热加载不会报错，但工具返回会提示"仅对新建对局生效"。

## 校验

`asset.validate` 做 dry-run：逐文件解析 + 引用完整性（错误/警告分列，含 `category/file/cardId/prefabId` 锚点）。
`Error > 0` 不放行（不要带着 Error 去 `card.load`）。
