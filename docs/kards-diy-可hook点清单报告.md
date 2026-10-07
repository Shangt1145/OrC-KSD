# kards-diy 可 hook 点清单报告

> 用途：把 `D:\kards-diy` 游戏层中**所有可作为 hook 的地方**（可被外部介入 / 观察 / 改写 / 扩展的点）盘清，作为 OrC 引擎游戏层设计 hook 面与扩展点的对照依据。
> 来源：`D:\kards-diy\resources\app\game\`（`js/` 引擎源码 + `data/` 数据与规范）+ `resources\app\`（Electron 宿主）。
> 关联：`docs/kards-diy-游戏流程分析报告.md`（流程）、`docs/orc-用户输入点分析报告.md`（OrC 输入点）、`docs/kards-diy-可参考语料报告.md`（效果原语映射）。
> 日期：2026-10-05。行号以分析时版本为准，**以符号名为准**。

---

## 0. 顶层命名空间（外部介入的入口对象）

这些全局对象是外部（其他脚本 / 开发者 / 联机对端）介入引擎的抓手：

`KG`（引擎）、`KG.effects`（FX，效果层）、`KG.ai`、`KG.inspector`、`KG_EFFECT_EDITOR`、`KG_EFFECT_CONTRACT`、`KG_PRIMITIVES`(P)、`KG_PRIMITIVES_EXTRA`、`KG_PARSER_V2`、`KG_EFFECT_OVERLAY` / `KG_ALIASES`、`KG_CARDS` / `KG_CARD_INDEX`、`KG_ANIM` + `KGSfx`、`KGNetSync`、`KGNet`、`KG_AUTO_SAVE`、`KG_PLAYER_STORAGE`、`KG_CARD_GUIDE`、`KG_COMPILER`。

---

## A. 触发器 / 事件系统（最核心的 hook 面）

### A1. 派发与分派核心

| 函数 | 文件:行 | 语义 |
|---|---|---|
| `runTrigger(state, ev)`（导出 `KG.runTrigger`） | `engine.js:2662`（2796） | **唯一事件派发入口**；按 `ev.trigger` 匹配并交叉 `owner`。内部：`match`2673 / `selfOnly`2677 / `fitsEvent`2684 / `skipSource`2695 / 对手侧 `watchOpponent`2721 / `upgradeCheck`2736 / 总部附魔 `p.hqEnchants`2739 / 反制卡 `p.counters`2769 |
| `execEffects(state,ctx,effects,chooser,trigger)`（`KG.execEffects`） | `engine.js:2799` | 转调 `KG.effects.exec` |
| `FX.exec` | `effects.js:3029` | 效果块执行器：按 `e.trigger`/`e.triggers[]` 过滤 3031、条件门 3066、`oncePerTurn`3071、`once`3078、充能门 3049、脚本逃生舱 3104、`OPS[a.op]` 派发 3113 |
| `emitUnitActed(state,u)` | `engine.js:2324` | 派发 `unitActed` |

### A2. 内置 trigger 全集（事件名字符串 = hook 名）

| 事件名 | 触发处（文件:行） |
|---|---|
| `turnStart` / `turnEnd` | engine.js:882 / 899 |
| `unitDeployed` | engine.js:1828、effects.js:1301/1790/2199 |
| `deploy` | compiler.js:117/119；playCard 直接执行 engine.js:1817 |
| `mobilize` / `movedToFrontline` / `unitMobilized` | engine.js:2511 / 2512 / 2514 |
| `death`（亡计）/ `friendlyDeath` | engine.js:1289-1302 |
| `afterKill` / `enemyKilled` | engine.js:1277 / 1283 |
| `attack` / `attacked` / `unitAttacked` | engine.js:2203/2219、2223、2204/2220 |
| `afterAttack` / `afterAttackHQ` | engine.js:2429 / 2212 |
| `friendlyAttacked` / `friendlySurvived` | engine.js:2224 / 2305-2306 |
| `damaged` / `friendlyDamaged` | engine.js:1206 / 1205 |
| `hqDamaged` | engine.js:1426 |
| `unpinned` / `revealed` | engine.js:905 / 1225 |
| `targeted` / `targetedByOrder` | effects.js:367 / compiler.js:248-251 |
| `orderPlayed` / `order` | engine.js:1875 / 1876 |
| `counterSet` / `counter` | engine.js:1760 / 2791 |
| `kreditsGained` | effects.js:1005 |
| `extraDraw` | engine.js:617 |
| `impactUsed` | engine.js:2419 |
| `friendlyPinned` / `friendlySuppressed` / `friendlySilenced` / `friendlyRetreated` | effects.js:1034 / 1174 / 编译期 / 1217 |
| `unitLeft`（非消灭离场） | effects.js:1188、1215 |
| `shuffle` | effects.js:1693 |
| `drawn` | engine.js:367、compiler.js:152/180 |
| `chargeNow` / `chargeArm`（不走 runTrigger） | engine.js:851 / 2343-2344 |
| `play`（手牌附魔"使用时"） | effects.js:2090、engine.js:1821/1852 |
| `passive`（常驻/光环） | effect-contract.js:199、effects.js:3149 |
| `choose`（抉择）/ `drawn` | compiler.js:122 / 152 |

> **权威副本**：`data/invariants.json` 的 `listeningTriggers`（96 项监听白名单）。

### A3. 注册 / 注销 / 监听挂载点

引擎**没有独立 pub/sub `on/off` API**——「监听 = 效果块挂在对象上」：

| 挂载面 | 位置 | 说明 |
|---|---|---|
| 单位效果 | `u.def.effects`（卡面）+ `u.extraEffects`（运行时追加） | engine.js:2702 / 2726 |
| 总部附魔 | `player.hqEnchants[]` | `{effects, once, source, forOpponent, watchOpponent, anyOwner}`；engine.js:2741；写入 `OPS.hqEnchant` effects.js:1885 |
| 反制埋伏 | `player.counters[]` | engine.js:2769；`OPS.grantEffect`→effectTrigger effects.js:2312 |
| 事件主角引用（"其"指谁） | `compiler.js` `EVENT_REF` | 266 |
| 反制卡触发子句表 | `compiler.js` `COUNTER_CLAUSES` | 518（`on`/`owner`/`interrupt`） |
| 「监听句→counter 埋伏」白名单 | `compiler.js` `LISTEN_TRIGGERS` | 545 |
| **打断型（同步消费，绕过异步 exec）** | `consumeInterrupt(state,owner,kind,info)` | engine.js:2644 |

方向控制字段：`watchOpponent`（engine.js:2674）、`forOpponent`/`anyOwner`（2750-2752）、`selfOnly`（2678）。

---

## B. 效果系统扩展点（OPS / CONDS / 契约 / 逃生舱）

### B1. 两张原语注册表（直接 `表[id]=fn` 即可注册新原语）

| 注册表 | 文件:行 | 规模 | 例 |
|---|---|---|---|
| `CONDS` = `FX.CONDS` | `effects.js:406` | ~60+ | `and/or/not/compare/kreditsAtLeast/handSize/deckSize/turnAtLeast/var/hasKeyword/isUnitType/damaged/undamaged/eventUnitIs/defenderIsType…` |
| `OPS` = `FX.OPS` | `effects.js:655` | ~100+ | `damage/heal/destroy/buff/debuff/draw/discard/gainKredits/pin/grant/removeKeyword/silence/returnToHand/summon/addCardToHand/intel/reveal/move/takeControl/chooseOne/conditional/forEach/repeat/randomPick/develop/discover/salvage/shuffleIn/hqEnchant/grantEffect/setHandCost/opCostMod…` |

- 条件求值器：`evalCond(state,ctx,c)` — `effects.js:642`（未知 op 记 error 并按成立放行）。
- 动作执行入口：`engine.js:489` 读 `KG.effects.OPS[a.op]`。
- op 白名单副本：`data/primitives.json:20`。

### B2. 基本组合原语层（第二注册面）

| 项 | 文件:行 | 说明 |
|---|---|---|
| `atoms`（`let/store/sequence/branch/loop/iterate/choice/dealDamage/destroyUnit/changeAttribute/setKeyword/bindEvent/setModifier/…`） | `effect-primitives.js:154` | 在 335 `Object.assign(F.OPS, atoms)` 合并；334 断言不得与旧 OPS 重名 |
| `catalog`（编辑器字段元数据） | `effect-primitives.js:251` | |
| `seed/expand/unresolved`（动作重写降级） | `effect-primitives.js:280/283` | |
| `F.composition`（value/test/query/read/path/catalog/seed/expand/run/continuousOps/continuous/expire/clearAura） | `effect-primitives.js:333` | |
| 持续型动作表 `FX.PASSIVE_CONT_OPS` | `effects.js:3149` | 执行器 `applyContinuousAction` 3194 |
| 修正重放 `applyDynMod(u,g)`（**mod 类型注册点**） | `effects.js:3155` | `vsType/takeDoubleFrom/condAtk/vsHq/extraTypes/opCostAdd/primitiveField/…`；字段全集 `FX.freshMods()` 3126 |

### B3. 契约钩子（检查 / 生成能力表）

- `effect-contract.js`：`types`(5)、`parameters(fn,name)`(13)、`build(observed,engine,docs)`(22)、`validate(dsl,schema,opts)`(49) → `KG_EFFECT_CONTRACT = {build,validate,parameters,types}`(210)；上下文引用白名单 `contextRefs`(47)。

### B4. JS 逃生舱（任意注入 JS）

| 项 | 文件:行 |
|---|---|
| `runEffectScript(...)`（`new Function('state','ctx','a','KG','targets','log','num','ask',…)`，脚本缓存 668） | `effects.js:669` |
| `OPS.script`（`{"op":"script","code":"…"}`） | `effects.js:688` |
| `{"trigger":…, "script":"…"}` 路径 | `FX.exec` effects.js:3104 |

### B5. 编辑器 / 检查器可注入接口

- `effect-inspector.js`：`mount(host,api)`(261)、`KG.inspector={Core,mount,validate,toModel,fromModel,clone,label,BUILD}`(329)；读卡源 ＝ base + `KG_EFFECT_OVERLAY[id]` + `edits`(274)；`schema:()=>KG_EFFECT_SCHEMA`(303)。
- `effect-editor.js`：`KG_EFFECT_EDITOR={mount}`(281)；导航钩子 `KG_BEFORE_NAVIGATE` / `KG_BEFORE_CLOSE`(276)。
- 元数据：`data/op-docs.json`（op 文档+参数）、`data/effect-schema.json`（能力 schema）、`data/effect-labels.json`。

---

## C. 编译 / 解析扩展点（卡面文本 → DSL）

| 项 | 文件:行 | 说明 |
|---|---|---|
| `C.compile(text,card)` | `compiler.js:554`（`KG_COMPILER`） | 主编译入口 |
| 触发前缀表（正则可热改）`TRIGGERS` | `compiler.js:116` | 含 `chargeArm/chargeNow` 特判 63/101 |
| 原语层入口 `parsePrimitives` → `KG_PRIMITIVES.parse` | `compiler.js:488` → `primitives.js:2652`（包装 3725） | |
| 词条白名单 `KW_WORDS` / `isKeywordOnlyLine` | `compiler.js:42/45` | |
| `REFERENCE_CARDS`（跳过释义卡） | `compiler.js:598` | |
| **兵种词表** `P.UNIT_TYPES` | `primitives.js:39` | 全项目唯一权威兵种词表 |
| 词条中文映射 `P.KW_CN` / `P.KW_FILTER` | `primitives.js:1414` / 474 | |
| 条件规则表 `P.COND_RULES` / `buildCondition` | `primitives.js:1710` / 1900 | 「新增条件写法 = 加一行」 |
| 长尾动作规则表 `P.LONGTAIL` / `LONGTAIL3` / `LONGTAIL4` | `primitives.js:524 / 698 / 889` | |
| 卡名解析/别名 `P.resolveCardRef` | `primitives.js:1468`（读 `KG_ALIASES` 1481） | |
| 未识别产出 `P.unparsed` / 变量回填 `takeVarRefs` / `expandAction` | `primitives.js:2619 / 2627 / 1963` | |

### C1. 外置声明式规则表（**无需改 JS 即可加句式**）

`data/primitives.json`（`rules[]` + op 白名单）→（`node game/tools/build-primitives.js` 生成）→ `js/primitives-extra.js`（`KG_PRIMITIVES_EXTRA.rules`）→ `primitives.js:604-606` 注入 `LONGTAIL`。

> 注：本构建内 `tools/build-primitives.js` 与 `tools/build-card-guide.js` **未随包**（仅注释引用）；`primitives-extra.js` 已是预生成内联产物。

### C2. 别名机制（两条链）

- 静态别名表 `data/aliases.json`。
- 真源别名 = `data/nations/*.json` 的 `aliases[]` + `_meta.json:orphanAliases`，由 `build-effects.js` 生成 `effects-data.js` 的 `KG_ALIASES`（消费 `effects.js:2851/2860`）。

### C3. 效果覆盖层（**可外部改写任意卡效果**）

- 生成物 `js/effects-data.js` → `KG_EFFECT_OVERLAY` + `KG_ALIASES`（源 `nations/*.json` 的 `overlayFields`）。
- 池合并优先级链（注入点）：`ui.js` `rebuildPool()` — 235；优先级 **base/custom < 本地 overlay < 主机 hostFx < `S.edits`（本地手工）< `peerOverrides`（联机整卡）**（注释 248/253/258）。
- 卡面级字段提升：`engine.js` `promoteCardFields` — 153。

---

## D. 数值 / 状态变更钩子

| 项 | 文件:行 | 说明 |
|---|---|---|
| 光环重算总入口 `recomputeAuras(state)`（`KG.recomputeAuras`） | `engine.js:2805` → `effects.js:3252` | 附带烟幕禁则清扫 `enforceSmokeBans` engine.js:1061 |
| 光环记账字段 `auraAtk/auraDef/auraKws/auraRmKws/auraOp` | `makeUnit` engine.js:263；重建 effects.js:3257-3267 | |
| 单位构造 `makeUnit` / `newMods` | `engine.js:245` / 205 | 初始属性/词条落地 |
| 修正重放 `applyDynMod` | `effects.js:3155` | mod 类型注册点 |
| 临时/持续修正容器 | `u.dynMods` / `u.turnMods` / `u.primitiveExpiry` / `u.primitiveAura` | effect-primitives.js:146-147 |
| 伤害/死亡钩子 `damageUnit`（派发 damaged/friendlyDamaged 1205-1206）、`hqDamaged`(1426)、`killUnit`/亡计(1289-1302)、`afterKill`/`enemyKilled`(1277/1283) | `engine.js` | |
| 行动花费重算 `effOpCost` + opCost 累计 | `engine.js:1887` 附近 | |
| 老兵升级钩子 `upgradeCheck` / `applyDeployTraits` / 充能 | `engine.js:1363 / 1695 / 2343,2376` | |
| 胜负判定钩子 `checkWin` | `engine.js:1437` | |
| 日志/演出脏标记 `KG.log` / `fxPush` | `engine.js:125 / 136`（进 `state.fxPops`） | |
| 快照/脏检查/差异（对拍与联机校验）`stateDigest`296 / `fullDigest`335 / `describeDiff`348 / `fingerprint`58 / `poolDigest`74 / `applyPacket`259 | `netsync.js` | |

---

## E. 外层钩子（AI / 网络 / 音效 / 动画 / 存档 / UI）

### E1. AI 决策钩子

| 项 | 文件:行 | 说明 |
|---|---|---|
| `AI.takeTurn(state,pi,hooks)` | `ai.js:84` | `hooks.afterStep`98 / `hooks.delay`104 / `hooks.beforePlay`154 |
| `AI.chooser(state,pi,rng)` | `ai.js:30` | 可按 `req.kind` 扩展：`target/chooseOne/discover/mulligan` |
| `KG.autoDeck(pool,opts)` | `ai.js:252` | 可注入随机源 `opts.rng`（264） |
| 通用询问通道 `KG.ask(chooser,req)` | `engine.js:1447` | 所有玩家选择的统一出口；UI 实现 `uiChooser` ui.js:4946 |

### E2. 网络同步钩子

| 项 | 文件:行 |
|---|---|
| `Net.hooks`（`getPoolCards/applyCards/hashPool/getArt/setArt/artIds/getDecks/onStatus/onProgress/onPeerMessage/onMove/onLog`） | `net.js:68` |
| UI 回填实现 | `ui.js:3308-3337`（追加 `getFxOverlay/applyFxOverlay/getArtRaw/refreshCards/localChooser/onMatchStart/getState`） |
| 消息分派 `handle` / `onPeerPayload` / `fx-sync` | `net.js:373 / 581 / 600-615` |
| 确定性重放 `recorder` / `player` / `applyPacket` | `netsync.js:214 / 233 / 259` |

### E3. 音效 / 动画触发器

| 项 | 文件:行 |
|---|---|
| 音效 `TABLE`(事件→音频) / `EVENT_GAIN` / `play(evt,opts)` / 语义化 API `S.attack/hit/move/die/deploy/veteran/counterSet/counterFire/shuffle/intel/burn/order/ui` | `sfx.js:46 / 136 / 297 / 327-345` |
| 动画消费引擎事件队列 `playFxPops(st,animate)`（消费 `state.fxPops`） / `burnPops` | `ui.js:1645 / 1614` |
| 动画库 `A.playOrderCard`939 / `A.orderFx`1240 / `A.playOpponentOrder`1041 / `setSpeed`32-53 | `animate.js` |
| 反制演出钩子 | `ui.js:1590-1602` |

### E4. 自动存档 / 导航钩子

| 项 | 文件:行 |
|---|---|
| `KG_AUTO_SAVE = {init,flush,imagesChanged,error,restored}`；失败事件 `CustomEvent('kg-save-error')`；导航拦截 `KG_BEFORE_NAVIGATE`；`KG_BEFORE_CLOSE`；localStorage 写拦截 | `auto-save.js:133 / 71 / 107 / 276(editor) / 126-128` |
| 原生桥 `KG_PLAYER_STORAGE`（`read/write` 快照 `kards-player-data` v1）；Capacitor 插件 `PlayerStorage` | `player-storage.js:9` |

### E5. UI 事件回调（可外部调用）

- `ui.js` 导出 `S`（=`KG.ui`）并挂：`S.applyDrop`1388 / `S.dropInfoAt`1389；`Net.hooks.refreshCards`3328 等。

---

## F. Mod / 插件 / 自定义入口

| 入口 | 位置 | 说明 |
|---|---|---|
| **玩家存档** | `player-storage.js` | `read/write/validate`，原子写 + `.previous` 备份 |
| **卡池数据管线（唯一存储 = nations）** | `tools/merge.js` | 读 `data/nations/*.json` → 生成 `js/cards.js` + 报告；`KG_DATA_ROOT` 可重定向根（16） |
| | `tools/build-effects.js` | 生成 `js/effects-data.js`、`_effects_report.md`、`_missing_cards.md`；别名聚合 62-72 |
| | `tools/player-cards.js` | 卡池迁移（`prepare/catalog/differences/legacyRoots`） |
| | `tools/delete-cards.js` / `tools/alt-art-lib.js` | 删除内置卡 / 异画扫描 |
| **开发/联机服务器（外部写入端点）** | `tools/serve.js`(+`tools/ws.js`) | `POST /__save-cards`258 / `/__delete-card(s)`268 / `GET /__alt-art`283 / `POST /__alt-art-ui`296 / `GET /kg-net`300 / `/kg-ws`369 / `/`→index.html 308；自定义网卡规则文件 429-431 |
| **牌组导入 / 导出 / 卡组库** | `ui.js` | `S.deckLib`、`persistDeckLib/loadDeckLibrary`5349/5525；「复制卡组代码」2426；`Net.hooks.getDecks`3329；localStorage 键 `kg.deckLib/kg.deck/kg.custom(/.bak)/kg.edits/kg.removed/kg.altArt` |
| **运行时效果/卡注入（覆盖层）** | 见 §C3 | `KG_EFFECT_OVERLAY` / `S.edits` / `S.hostFx` / `S.custom` / peer overrides |
| **规则/校验数据（改文件即扩展）** | `data/` | `invariants.json`（编译不变式+`listeningTriggers`）、`effect-schema.json`、`op-docs.json`、`effect-labels.json`、`primitives.json`、`aliases.json`、`nations/_meta.json` |

---

## G. 「唯一外部介入点」速查（移植 C# 时优先映射）

| # | 介入点 | 位置 | 性质 |
|---|---|---|---|
| 1 | 事件总线 `KG.runTrigger(state, ev)` | engine.js:2662 | **所有钩子的收敛点** |
| 2 | 效果分派 `KG.effects.exec` | effects.js:3029 | |
| 3 | 动作注册 `KG.effects.OPS[id]=fn` | effects.js:655 / effect-primitives.js:335 | 注册表 |
| 4 | 条件注册 `KG.effects.CONDS[id]=fn` | effects.js:406 | 注册表 |
| 5 | 文本编入 `KG_COMPILER.compile` → `KG_PRIMITIVES.parse` | compiler.js:554 / primitives.js:2652 | |
| 6 | 遍历式规则注入 `data/primitives.json` → `primitives-extra.js` → LONGTAIL | — | 声明式 |
| 7 | 效果覆盖 `KG_EFFECT_OVERLAY` | effects-data.js | 覆盖层 |
| 8 | 状态重算 `KG.recomputeAuras` | engine.js:2805 | |
| 9 | 选择回调 `chooser(req)` / `KG.ask` | engine.js:1447 | 交互 |
| 10 | 外部同步/演出 `Net.hooks` / `state.fxPops`+`KGAnim`+`KGSfx` | net.js:68 | 表现层 |

---

## H. 与 OrC 现有 hook 面的对照（要点）

| kards-diy hook 类别 | OrC 对位 | 差距 |
|---|---|---|
| 事件总线 `runTrigger` | `Bus.Emit` + `Trigger.InvokeAsync` + `GameUpdates`（17 条信号） | OrC 为**强类型载荷**、kards 为裸对象；信号覆盖面需按 §A2 补齐 |
| OPS/CONDS 注册表 | 效果工厂 `CardEffectRegistry.Register` / 条件由判定器 `JudicatorRegistry` 承载 | OrC 无「开放式字符串注册表」，是**注册式**（重复拒绝） |
| JS 逃生舱 `OPS.script` | `Orc.Lua`（沙箱已就绪、未接入） | 逃生舱对位 |
| 效果覆盖层 `KG_EFFECT_OVERLAY` | 无直接对位 | 数据层动态覆盖需新设计 |
| 文本编译管线 | 无（OrC 卡牌为代码定义，无 JSON 载入） | 需新增（见报告 3 缺口） |
| `chooser` / `KG.ask` | `TargeterManager.RunAsync` + `ITargeterBridge`（会话 / 选择器 / 语义事件） | 语义接近，结构不同 |
| `Net.hooks` / `fxPops` | `Orc/Output/*`（段轮询 `TakeSegments`） | 观察模型不同（推 vs 拉） |
