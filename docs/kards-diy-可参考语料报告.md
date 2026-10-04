# kards-diy 可参考语料报告

> 用途：把 `D:\kards-diy`（KARDS DIY，Electron/JS）里"可被 OrC 参考/移植"的机制与效果语料盘清，作为后续逐步移植的对照依据与验收用例来源。
> 来源：`D:\kards-diy\resources\app\game\`（`js/` 引擎源码 + `data/` 数据与规范）。频次取自 `data/effect-schema.json`（generatedAt 2026-10-02）。
> 对照基线：`docs/初始设计文档.md`、`.ams-docs/b18ac18d-.../表达力缺口补全/*`（OrC 已定 7 项缺口设计）。
> 日期：2026-10-04。

---

## 〇、使用约定（先读）

1. **本报告是"语料/语义参考"，不是"代码移植清单"**。kards-diy 是数据驱动效果虚拟机（JS），OrC 是强类型触发器引擎（C#），执行模型不可平移；可平移的是**机制语义 + 数据模型 + 用例**。
2. **落地形态一律以 OrC 已定设计为准**（G3 卡侧修饰器链、G11 独立 HQ 实体、G12 TagData、G9 订阅自管、G14 自写监听 handler）。遇到与 kards-diy 实现不符处，参考其语义、不照搬其结构。
3. 频次列＝该 op/cond 在实际卡池数据中出现的次数（`seenInData`），是**优先级依据**：高频＝先用、先验。

---

## 一、语料地图（kards-diy 文件 → 内容 → 用途）

| 文件 | 内容 | 迁移用途 |
|---|---|---|
| `game/js/engine.js`（~2850 行） | 规则核心：事件分发、战斗、回合、战场、HQ | 规则语义对照（**触发/时序/HQ/战场**） |
| `game/js/effects.js`（~3390 行） | `OPS`（动作）/`CONDS`（条件）注册表 + `num()` | 原语语义总表（**主要语料**） |
| `game/js/effect-primitives.js` / `primitives.js` / `primitives-extra.js` | 选靶/阵营/区域/兵种词表 + 句式解析 | 中文句式→原语的映射语料 |
| `game/js/compiler.js` / `parser-v2.js` | 卡面文本 → DSL JSON 编译器 | 卡面语料 → 效果的自动化管线 |
| `game/js/effect-contract.js` | 从 OPS/CONDS 反推能力表并校验 | "能力契约自检"思路可借鉴 |
| `game/js/cards.js` / `effects-data.js` | 卡定义/效果数据装载 | 卡牌数据模型对照 |
| `game/js/ai.js` | 启发式 AI | 不建议迁移（超出 OrC 非目标） |
| `game/js/netsync.js` / `net.js` | 锁步同步/WebSocket | 不建议迁移（明确非目标） |
| `game/data/EFFECT_DSL.md` | DSL 规范 v1（触发器语义/op 清单/选择器） | **机制语义速查** |
| `game/data/effect-schema.json` | 127 ops / 46 conds 全量 + 参数结构 + 频次 | **原语总表（本报告数据源）** |
| `game/data/effect-labels.json` | op/cond/field/value 中文对照 | **命名映射（汉→英）** |
| `game/data/invariants.json` | 27 项监听型触发清单 + 编译不变式 | **触发面清单** |
| `game/data/primitives.json` | 外置句式规则 + op 白名单 | 句式→原语映射语料 |
| `game/data/nations/*.json` | 各国卡池（卡定义 + effects） | **效果用例语料（315 卡）** |
| `game/data/_effects_report.md` | 编码覆盖报告（315 卡 / 231 已编码） | 覆盖率与缺口语料 |
| `game/data/SCHEMA.md` / `RULES_REF.md` | 数据 schema / 规则参考 | 数据模型对照 |

---

## 二、机制语料

### 2.1 触发时机（trigger）

**主动型（单位自身/出牌）**

| trigger | 语义 | OrC 对应 |
|---|---|---|
| `order` | 指令牌打出时（效果主体） | 已有：OrderFlow |
| `deploy` | 单位进入战场（"部署："） | 已有：`unit.deployed` |
| `mobilize` / `unitMobilized` | 本单位 / 友方单位移至前线 | 缺（移动触发面） |
| `death` | 单位被消灭（"亡计："） | 已有：`card.died` |
| `turnStart` / `turnEnd` | 拥有者回合开始/结束 | 已有：`turn.start.*` / `turn.end` |
| `attack` / `attacked` / `afterAttack` | 本单位攻击时 / 攻击后 | 已有流程位（AttackFlow） |
| `damaged` | 本单位受到伤害后 | 缺（伤害事件面） |
| `counter` / `counterSet` | 反制卡触发 / 埋设 | 部分：CounterCard 状态翻转 |
| `passive` | 常驻（光环/静态修正） | 缺（G4 光环） |

**监听型（`invariants.json:96-124`，共 27 项）**

`unitDeployed`、`unitMobilized`、`friendlyDeath`、`friendlyAttacked`、`friendlyDamaged`、`friendlyPinned`、`friendlyRetreated`、`friendlySilenced`、`friendlySurvived`、`attack`、`attacked`、`orderPlayed`、`hqDamaged`、`enemyKilled`、`afterKill`、`afterAttack`、`afterAttackHQ`、`impactUsed`、`unpinned`、`targetedByOrder`、`chargeNow`、`kreditsGained`、`turnStart`、`turnEnd`、`friendlySuppressed`、`shuffle`、`unitLeft`

> **迁移要点**：OrC 现状是"伤害走流程（DamageFlow band）而非事件名"。若要对齐 kards-diy 的"监听友好"触发面，需在 G14（监听源）/X1（触发者卡牌）中补齐"伤害/移动/击杀/洗切"等信号。

### 2.2 效果原语 OPS（127 项，按域分组）

> 标注 频次＝卡池数据出现次数；`别名`＝aliasOf；0 表示已登记但卡池未观测（实现级补全）。

#### A. 伤害 / 治疗 / 消灭 / 属性

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `damage` | 造成伤害 | 32 | 已有 DamageFlow |
| `damageAll`（别名 damage） | 对全体伤害 | 0 | 已有（选择器覆盖） |
| `damageHQ` | 对总部伤害 | 14 | 缺（G11 HQ） |
| `damageSplit` | 均分伤害 | 1 | 缺（分配算法） |
| `conditionalRandomSplit` | 随机分配（兼容别名） | 0 | 缺 |
| `heal` / `healAll` | 治疗 / 全体治疗 | 0 / 2 | 缺（G17） |
| `healHQ` | 治疗总部 | 0 | 缺（G11+G17） |
| `destroy` / `destroyAll`（别名） | 消灭 / 全体消灭 | 22 / 0 | 已有（死亡流） |
| `setStats` / `setDefense`（别名） | 设置身材 | 1 / 0 | 缺（G3 设值型修饰） |
| `debuff` | 属性削减 | 0 | 缺（G3） |
| `buff` / `buffAll`（别名） | 属性增减（含 keyword/opCostMod/duration） | 33 / 0 | 缺（**G3 主用例**） |
| `armorBonus` | 护甲加成 | 0 | 缺（护甲机制） |
| `loseDefense` | 失去防御力（byCost） | 1 | 缺（G3） |
| `orderDamageMod` | 指令伤害修正 | 0 | 缺 |
| `buffCardsInPiles` / `buffDeckAndHand`（别名） | 强化牌堆中卡 | 2 / 0 | 缺（G6 区域卡牌操作） |
| `auraBuff` | 光环增益 | 0 | 缺（G4） |

#### B. 卡牌与资源

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `draw` | 抽牌 | 38 | 已有 |
| `drawUntil` | 抽到…为止 | 2 | 缺 |
| `discard` | 弃牌（mode: random/named/choose） | 4 | 缺（弃牌堆） |
| `discardAll` | 全部弃掉 | 1 | 缺 |
| `discardFromDeck` / `discardChosenHand` | 从卡组/所选手牌弃 | 0 / 0 | 缺 |
| `mill` | 磨牌 | 1 | 缺 |
| `shuffleIn` | 洗入卡组（cardId/name/random/to:top） | 10 | 缺 |
| `shuffleInUntil` | 洗入直到卡组为 N | 0 | 缺 |
| `shuffleHandIntoDeck` / `shuffleRandomSet` | 手牌洗回 / 洗入随机系列 | 0 / 0 | 缺 |
| `removeDeckTop` | 移除卡组顶（to: discard/hand/removed） | 1 | 缺 |
| `removeFromEnemyDeck` | 从敌方卡组移除 | 0 | 缺 |
| `deckToHand` / `deckToTop` | 卡组入手牌 / 放回顶 | 0 / 0 | 缺 |
| `deckToField` | 卡组上场（to: support/frontline + buff/keyword） | 2 | 缺 |
| `enemyDeckToHand` | 敌方卡组入手牌 | 1 | 缺 |
| `addCardToHand` | 加入手牌（cardId/name/self/random） | 52 | 缺（G6 生成） |
| `copyToDeck` / `copyHandCard` / `copyLastDiscarded` | 复制进卡组/手牌/刚弃的牌 | 0 / 1 / 0 | 缺（G6 复制） |
| `transformHandCard` / `transformUnit` | 转换手牌 / 变形单位 | 1 / 0 | 缺（G13 转换/升级） |
| `develop` / `discover`（别名） | 开发（from: deck/pool，thenSummon） | 20 / 2 | 缺（G6 衍生物） |
| `salvage` | 打捞（弃牌堆取回） | 0 | 缺 |
| `intel` | 情报（看对手手牌） | 0 | 缺（G18 明牌） |
| `reveal` | 明牌 | 0 | 缺（G18） |
| `chooseFromHand` / `chooseHandCard` | 从手牌选择 | 0 / 1 | 缺 |
| `handToField` / `summonFromHand`（别名） / `handUnitToFrontline` | 手牌上场 / 上前线 | 1 / 0 / 0 | 缺 |
| `takeControl` | 夺取控制权 | 1 | 缺（G19 收缴） |
| `removeUnit` | 移除单位 | 0 | 缺 |
| `grantEffect` / `grantEffectToHand` | 授予效果（挂单位/手牌） | 0 / 0 | 部分（Effect 装载面） |

#### C. 费用与指挥点

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `gainKredits` / `gainKreditSlot` | 获得指挥点 / 点槽 | 17 / 9 | 部分（ResourceManager） |
| `loseKredits` / `loseKreditSlots` | 失去指挥点 / 点槽 | 0 / 1 | 部分 |
| `setKreditSlots` | 设置点槽数 | 1 | 缺 |
| `nextTurnKredits` / `nextTurnSlots` / `nextTurnDrawPending` | 下回合点数/槽/多抽 | 1 / 0 / 0 | 缺（**G9 延迟资源**） |
| `nextTurnStart` | 下回合开始时执行动作组 | 1 | 缺（**G9 核心**） |
| `noDrawNextTurn` / `noKreditSlotNextTurn` / `noKreditSlotForTurns` | 下回合不抽 / 无点槽 | 0 | 缺（G9） |
| `delayNextTurn` | 跳过下回合 | 0 | 缺（G9） |
| `costReduce` | 本回合手牌费用修正 | 0 | 缺（G5） |
| `opCostMod` / `opCostModAll` | 行动花费修正（单位/全体） | 5 / 4 | 缺（**G5 主用例**） |
| `setOpCost` | 设置行动花费 | 2 | 缺（G5 设值型） |
| `globalOpCostMod` / `handOpCostMod` / `deckCostModAll` | 全局/手牌/卡组费用修正 | 0 | 缺（G5） |
| `setHandCost` | 设置手牌费用（mode: reduce） | 4 | 缺（G5） |
| `setLastAddedCost` | 设置新加入卡的费用 | 0 | 缺（G5） |
| `modMovedCost` | 移动费用修正 | 0 | 缺（G5） |
| `condOpCost` | 条件行动花费 | 1 | 缺（G5） |

#### D. 场面 / 词条 / 战斗

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `summon` | 召唤（to: support/sameZone/hqAdjacent） | 12 | 缺（G6 衍生物） |
| `returnToHand` / `retreat`（别名） | 返回手牌 / 撤退 | 0 / 1 | 缺 |
| `move` | 强制移动（to: frontline/support） | 2 | 部分（CommandManager 移动） |
| `pin` / `unpin` | 压制 / 解除压制 | 10 / 0 | 缺（G1/G2 状态） |
| `grant` / `grantAll`（别名） | 获得词条（value） | 19 / 3 | 部分（词条系统） |
| `grantRandomCombatKw` / `grantRandomKwToPiles` | 随机获得对战词条 / 牌堆随机词条 | 0 | 缺 |
| `removeKeyword` | 失去词条 | 0 | 缺（G1/G2） |
| `silence` | 沉默/抑制 | 1 | 缺（G1/G2） |
| `refreshAction` | 刷新行动机会 | 0 | 缺 |
| `canMoveAndAttack` | 可移动并攻击（能力，非身份） | 1 | 缺 |
| `grantMod` | 获得常驻修正（mod 白名单，38 次，最高频） | 38 | 缺（**G3/G4 修正白名单**） |
| `upgradeSelf` | 升为老兵 | 2 | 缺（**已搁置**） |
| `triggerMountain` / `forecast` / `fury` | 山地/预报/狂怒 | 0 | 缺（专属机制） |
| `fight` / `fightRepeatedly` | 互相战斗 / 反复战斗 | 1 / 0 | 缺（**G16 效果级战斗**） |
| `nthTime` / `asVar` / `rememberTarget` | 第 N 次 / 存变量 / 记目标 | 0 | 缺（G14） |
| `countered` / `activate` | 被反制 / 激活 | 0 | 部分（反制卡） |
| `setWeather` | 设置天气（mist/gale/clear/snow） | 24 | 缺（专属全局状态） |

#### E. 总部 / 附魔

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `hqEnchant` | 总部附魔（持续效果组，untilTurnEnd） | 5 | 缺（**G11 核心**） |
| `hqKeyword` | 总部词条 | 0 | 缺（G11） |
| `hqArmor` | 总部护甲 | 0 | 缺（G11） |
| `hqMaxUp` | 总部上限提升 | 8 | 缺（**G11 主用例**） |
| `suppressHQ` | 压制总部 | 0 | 缺（G11） |

#### F. 流程控制 / 其它

| op | 中文 | 频次 | OrC 映射 |
|---|---|---|---|
| `chooseOne` | 抉择 | 19 | 缺（G10 交互形态） |
| `conditional` | 如果…则（condition/then/else） | 14 | 部分（条件流） |
| `forEach` / `repeat` | 逐个执行 / 重复 | 1 / 1 | 缺（G…… 循环原语） |
| `randomPick` | 随机挑选 | 0 | 缺（G8 随机） |
| `log` | 日志占位 | 2 | — |
| `endTurnNow` | 立即结束回合 | 1 | 缺（G9） |
| `orderDoubleTurn` | 指令双倍回合 | 1 | 缺（整回合钩子） |
| `script` | 内联 JS 脚本（逃生舱） | 0 | 缺（**Orc.Lua 对位**） |
| `_returnToHandOld` | 旧版兼容 | 0 | — |

### 2.3 条件原语 CONDS（46 项）

| 组 | 条件 | 频次 | OrC 映射 |
|---|---|---|---|
| 组合 | `and` / `or` / `not` | 0 | 缺（条件组合） |
| 通用比较 | `compare`（left/cmp/value，推荐唯一比大小口） | 2 | 缺（G…… 数值查询） |
| 场面 | `controlFrontline` / `frontlineControl` / `frontlineEmpty` / `frontlineEnemyOrFull` / `hasRoom` | 0/0/1/1/0 | 部分（前线判定） |
| 资源 | `kreditsAtLeast` / `kreditsAtMost` | 0 | 缺 |
| 手/牌库 | `handSize` / `deckSize` / `cardInDeck` / `handHasCard` | 0/0/0/2 | 部分 |
| HQ | `hqAbove` / `hqBelow` | 0 | 缺（G11） |
| 费用 | `opCost` | 1 | 缺（G5） |
| 单位 | `unitCount` / `unitCountLess` / `controlsType` / `isUnitType` / `unitInZone` | 4/1/2/0/0 | 部分（筛选） |
| 词条/状态 | `hasKeyword` / `damaged` / `undamaged` / `targetPinned` / `cardVarHasKeyword` | 1/0/0/1/0 | 部分 |
| 目标态 | `targetAlive` / `targetDead` / `targetDestroyed` / `targetIsHQ` / `targetIsUnit` | 0 | 缺（G10） |
| 事件 | `eventUnitIs` / `eventCardIs` / `deadUnitCostAtLeast` / `defenderIsType` / `damageFromType` | 0/1/0/1/0 | 缺（监听事件上下文） |
| 回合节奏 | `playedUnitThisTurn` / `firstUnitThisTurn` / `turnAtLeast` / `lastTurnKilledCompare` | 0 | 缺（**G14**） |
| 随机/变量 | `random` / `var` | 0 | 缺（G8 / 变量） |
| 其它 | `revealedInEnemyHand` / `discoveredCardNotInDeck` | 1/1 | 缺（G18 / G6） |

### 2.4 选择器 / 过滤器 / 数值取值器

**选择器（动作 target）**：`"t1"`（引用声明目标）、`"self"`、`{"sel":"all|random|choose|self|ref|refAll|hqOnly", "side":"friendly|enemy|both|any", "filter":{...}, "count":n, "zone":"frontline|support", "excludeSelf":true, "ref":"eventUnit"}`。

**过滤器 filter 键**（`engine.js:1602-1683`）：`unitType`（可数组）`notUnitType` `anyUnitType` `keyword` `notKeyword` `maxAttack` `minAttack` `maxCost` `minCost` `maxDefense` `minDefense` `zone` `damaged` `name` `nameIncludes` `set` `setIn` `system` `cardId` `cardIds` `rarity` `excludeSelf` `adjacentTo:'self'` `damagedBySelf` `hasMod` `notMod`。

**数值取值器 `num()`**（`effects.js:21-161`）：常数、`ctx.vars` 变量、`cardVar`、`{stat: attack|defense|maxDefense|missing|armor|opCost|lastTurnKilled|lastCombatDamage, of: self|target}`、`{count:{sel,side,filter}, times}`、`maxStat`、`playedCount`、`rand`、`{hand:side}`、`revealedCount`、`{deck:side}`、`supportFree`、`frontlineFree`、`lastAdded`、`{hq:side, mode:missing}`、`{turn:true}`、`{kredits:side}`、`{sum|mul|min|max:{items:[...]}}`、`{kreditsAtLeast...}`（兼容）。

### 2.5 效果字段 / 卡牌字段

**效果（effect）字段**（`effect-schema.json:3812-3831` 频次）：
`trigger`(594) `actions`(283) `targets`(45) `oncePerTurn`(6) `on`(10) `owner`(12) `filter`(4) `interrupt`(6) `once`(4) `aura`(20) `condition`(12) `grants`(2) `onAttack`(2) `cardFields`(16) `unimplemented`(4) `vars`(6) `noAttackIfNameOnBoard`(2) `passiveRules`(4) `notes`(10)。

**卡牌（card）字段**（`nations/USG.json`）：`id` `src` `name` `set` `sub` `cardType` `unitType` `cost` `attack` `defense` `opCost` `keywords` `kwMap` `kwValues` `rarity` `token` `text` `effects`。

**卡面级字段 cardFields**：`upgradeOn` `costModPerKeyword` `costModPerOrder` `costModByStat`。

**filter 中出现过的维度键**（`effect-schema.json:3910-3927`）：`set`(3) `unitType`(11) `maxCost`(5) `nameIncludes`(3) `name`(14) `cardType`(17) `cardIds`(3) `rarity`(1) `maxAttack`(1) `setIn`(2) `minCost`(1) `system`(1)。

---

## 三、卡牌与效果语料规模

| 指标 | 数值（来源 `_effects_report.md`） |
|---|---|
| 卡池总数 | 315 |
| 已编码效果 | 231（真源＝`nations/*.json`，overlayFields 重建） |
| 覆盖层条目 / 别名 | 262 / 62 |
| 参考卡 | 7（cru/gue/lcru/mra/sc/sms/tsekep） |
| 老兵升级链 | 3 条（星盟/u/-23→-24、av76/units/-5→-6、deran/units/_9a→_9b） |
| 有文本但效果为空 | ~80（含全部 `zpaiq/天气` 系列、token/attack/mode1-11） |

**效果模式分布**（`effectFieldKinds`）：有 `actions` 的 283 条；`aura` 20；`passiveRules` 4；`cardFields` 8；`unimplemented` 2。

**兵种词表**（`primitives.js:39-61` 唯一权威）：含 KARDS 原生（infantry/tank/artillery/fighter/bomber）＋自定义（space/spacefighter/nonspace/order 等）。

---

## 四、迁移映射表（语料 → OrC 落点）

> 落点编号取自 OrC《表达力缺口补全》需求文档（本批 R2/R5/R7/R8；后续候选 R1-R13；子缺口 G1-G19）。

| kards-diy 语料 | OrC 落点 | 批次 | 迁移方式 |
|---|---|---|---|
| `buff`/`debuff`/`setStats`/`loseDefense`/`grantMod`/`buffCardsInPiles` | **G3 属性修饰** | 本批 | 语义参考（修饰类型：加法/设值/引用/上限） |
| `auraBuff`/`aura`/`passiveRules`/`grantMod.mod` 白名单 | **G4 光环/持续** | 本批 | aura 结构 + mod 白名单可作词表参考 |
| `opCostMod`/`setOpCost`/`setHandCost`/`costReduce`/`condOpCost` | **G5 有效费用** | 本批 | 费用作为链输出；`min/max` 钳制型 |
| `hqEnchant`/`hqMaxUp`/`hqKeyword`/`hqArmor`/`damageHQ`/`healHQ`/`hqDamaged` | **G11 HQ 实体化** | 本批 | kards-diy 的 HQ 语义（附魔=持续效果组）参考 |
| `set`/`cardType`/`unitType`/`rarity`/`name`/`cardId` 等筛选维度 | **G12 TagData** | 本批 | 维度清单参考（老兵已搁置） |
| `nextTurnStart`/`nextTurnKredits`/`nextTurnSlots`/`noDrawNextTurn`/`delayNextTurn`/`endTurnNow` | **G9 延迟调度** | 本批 | 锚点语义参考（turn.start.after / turn.end） |
| `nthTime`/`playedUnitThisTurn`/`firstUnitThisTurn`/`lastTurnKilledCompare`/事件 ordinal | **G14 历史计数** | 本批 | 计数器清单参考（自写监听 handler） |
| `chooseOne`/`randomPick`/`targets` 声明/`ask` 交互 | **G10 目标交互形态** | 后续 R6 | 声明式选靶与 OrC 运行时队列有张力，需裁决 |
| `sel:"random"`/`random` 条件/`randomPick` | **G8 随机** | 后续 R4 | 较易补（OrC 随机仅用于洗牌） |
| `pin`/`unpin`/`silence`/`removeKeyword`/`grant`/`grantAll` | **G1/G2 状态与词条** | 后续 R1 | 运行时授予/移除语义参考 |
| `addCardToHand`/`develop`/`discover`/`summon`/`deckToField`/`shuffleIn`/`transformHandCard`/`copy*` | **G6/G7/G13 区域与卡牌操作** | 后续 R3 | 取卡顺序算法（`EFFECT_FX.opCardId`）值得借鉴 |
| `counter`/`countered`/`interrupt: deathrattle|fatalDamage`/`takeControl` | **G15 处置语义**（反制/替换/重定向/免疫） | 后续 R9 | ⚠ 打断时序需专门裁决 |
| `fight`/`fightRepeatedly` | **G16 效果级战斗调用** | 后续 R10 | 复用 AttackFlow/DamageFlow |
| `heal`/`healAll`/`healHQ` | **G17 治疗/完全修复** | 后续 R11 | 治疗=恢复到上限（衔接 G3 上限语义） |
| `intel`/`reveal`/`revealedInEnemyHand` | **G18 明牌/揭示/隐蔽** | 后续 R12 | 新增 |
| `takeControl`/收缴态 | **G19 收缴** | 后续 R13 | 新增 |
| `script`（内联 JS 逃生舱） | **Orc.Lua** | 视需求 | Orc.Lua 沙箱已就绪但未接入引擎，作逃生舱对位 |
| `armorBonus`/护甲分层（重甲/轻甲/护盾） | 未编号（伤害域） | 视需求 | 需新设计 |
| `setWeather`/`forecast`/`fury`/`chargeNow`/`triggerMountain` | 专属机制（内容层） | 不迁移 | 属卡池内容，非通用机制 |
| `ai.js` / `netsync.js` / `net.js` | — | 不迁移 | 超出 OrC 非目标 |

---

## 五、移植优先级与批次

**批次 1（本批已定设计，语料直接用于验收）**：G3 / G4 / G5 / G9 / G11 / G12 / G14
- 用法：把对应 op 的**代表效果**翻译为 OrC 四场景验收用例（各缺口已定"机制→代表效果"口径）。
- 代表效果取自：`buff`(33) `grantMod`(38) `opCostMod`(5) `hqMaxUp`(8) `hqEnchant`(5) `nextTurnStart`(1) `nthTime`(0)。

**批次 2（需新设计的机制）**：R4 随机(G8)、R9 处置(G15)、R11 治疗(G17)、R10 效果级战斗(G16)、R12 明牌(G18)、R13 收缴(G19)、R3 区域卡牌操作(G6/G7/G13)。
- 前置：批次 1 落地（尤其 G3 上限语义 → G17）。

**批次 3（内容/专属）**：护甲分层、天气、充能、老兵升级链、脚本地图脚本。

**前置裁决项（移植前必须先定）**：
1. 反制打断的时序语义（kards-diy 同步事件前 vs OrC 事件边界检查）——归 R9/G15。
2. HQ 承载路径（kards-diy 纯数据 vs OrC 独立 HQ 实体）——归 G11。
3. 效果表达载体（JS DSL vs C# 强类型视图）——确定"只取语义不取执行模型"。

---

## 六、对拍与验收建议

1. **语料驱动验收**：从 `nations/*.json` 抽 20-50 张已编码卡，逐条翻译为 OrC 测试场景（机制级），验证表达力。
2. **规则差异先行对齐**：kards-diy 战场容量（支援 6 / 前线 5）与 OrC（4/5/4）不同、指挥点曲线与 X3 修正不同——对拍前需先统一规则参数，否则结果不可比。
3. **能力契约自检**：借鉴 `effect-contract.js`（从 OPS/CONDS 反推能力表校验数据）的思路，为 OrC 建"注册表↔数据"一致性检查。
4. **不要对拍 UI/AI/网络**：这些是 kards-diy 的端到端能力，与 OrC 非目标无关。

---

## 七、速查：高频语料 TOP（优先移植）

| 排名 | op | 频次 |
|---|---|---|
| 1 | `addCardToHand` | 52 |
| 2 | `grantMod` | 38 |
| 3 | `draw` | 38 |
| 4 | `buff` | 33 |
| 5 | `damage` | 32 |
| 6 | `setWeather` | 24 |
| 7 | `destroy` | 22 |
| 8 | `develop` | 20 |
| 9 | `chooseOne` | 19 |
| 10 | `grant` | 19 |
| 11 | `gainKredits` | 17 |
| 12 | `damageHQ` | 14 |
| 13 | `conditional` | 14 |
| 14 | `summon` | 12 |
| 15 | `shuffleIn` | 10 |
| 16 | `pin` | 10 |
| 17 | `gainKreditSlot` | 9 |
| 18 | `hqMaxUp` | 8 |
