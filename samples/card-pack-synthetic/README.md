# 合成卡包样本（card-pack-synthetic）

前端资产迁移（S9·P9d）的**包格式**样本：演示"引擎新资产模型"下的卡包目录约定——
与同目录的 `samples/card-pack-demo/`（"分发数据"管道样本，无 manifest）互补。

```
card-pack-synthetic/
├── manifest.json                    包清单（kind=cardpack / id / guid / version / assets 留空）
├── cards/                           卡目录（约定：`cards` 或 `cards-*`）
│   ├── sample.rifle.card.json           基础单位卡（无效果）
│   └── sample.quartermaster.card.json   带卡面文本 → 经引擎文本→效果管道编译
├── texts.json                       卡面文本 sidecar
└── keywords.json                    词条声明（由 `orc.builtin.ui` 插件在包装载/重载时加载）
```

约定与要点：

- **卡 id 前缀**：引擎装载后卡 id 为 `pack/sample.synthetic/<原卡id>`（该前缀是前端冻结契约，用于图鉴分包页）。
- **`assets[]` 留空**：卡片/效果/词条全走**目录约定**（`cards/`、`effects/`、`texts.json`、`keywords.json`）。
- **预检双零门**：装载前跑 `CardDistributionValidator`，Errors 与 Warnings 必须同时为 0，否则**整包隔离**。
- **`keywords.json` 的绑定**：`prefabId` 必须能在本包 `effects/` 或宿主注册面找到（否则对局装配期核验 fail-fast）。
  本样本仅演示声明形态；要真正跑通词条效果，请把对应 `*.prefab.json` 放进 `effects/`。
- 该包的 `guid` 为固定字面（样本专用）；自建包请自行分配稳定 guid。

对照测试：`tests/Orc.Packages.Tests/CardPackUiEndToEndTests.cs`（程序化生成同构夹具，端到端验证
卡包装载 → 词条注册 → 快照 → 建局 → 投影 → 运行期卡注册）。
