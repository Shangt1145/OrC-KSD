# UI 接入指南 — 消费 Orc 引擎内部信号

面向 **Godot C# 前端开发者**。说明 UI 如何监听 Orc 引擎的内部信号，并在自己的节奏里播放视觉表现。

> 状态：**API 名已与实现对齐**（`BeginAction` / `TakeSegments` / `TryTakeSegment` / `OnImmediateUpdate`）。

## 一句话模型

引擎每完成一次**动作**（如结束回合、打出一张牌），就把这次动作产生的**一"段"事件流**放进一个队列；**UI 在自己的帧循环里轮询取段、异步播放**。另外，UI 可注册一个**非阻塞 handler** 来接收**即时更新**，用于需要立刻反应的反馈。

**两条独立通道**：

| 通道 | 谁主动 | 用途 | 是否阻塞引擎 |
|---|---|---|---|
| **段通道** | UI 主动（轮询） | 播放完整视觉表现 | 否 |
| **即时更新通道** | 引擎主动（调用 handler） | 即时反馈 | 否（handler 强制非阻塞） |

---

## 1. 注册（初始化阶段，各只做一次）

```csharp
// ① 即时更新监听（非阻塞 handler）——可选
_immediateSub = engine.OnImmediateUpdate((updateType, payload) =>
{
    // 只做轻量通知：置标志 / 推入你自己的 UI 队列。禁止耗时与阻塞。
    _pendingImmediate.Enqueue(updateType);
});

// ② 段通道无需注册，UI 直接轮询取段（见 §2）。
```

> 注册须在 `Match.Initialize()` **之前**完成，否则会错过初始化产生的事件。

## 2. 取段（UI 帧循环，例如 Godot `_Process`）

```csharp
public override void _Process(double delta)
{
    // 轮询拉取本次动作产生的所有段（取走即移除）
    foreach (var segment in engine.TakeSegments())
    {
        _playbackQueue.Enqueue(segment);   // 入自己的播放队列，立即返回
    }

    // 播放由你掌控（可做队列、时长、动画调度），引擎不会等这里
    AdvancePlayback(delta);
}
```

- **不依赖精确通知**：引擎不会"推"段给你，你按自己的帧率轮询即可。
- **不阻塞引擎**：取段与播放都在 UI 侧，引擎产段后立刻继续。

## 3. 段对象结构（`EventSegment`）

```
EventSegment
├─ Sequence     : long         // 段号（引擎实例内单调递增）——用于排序 / 检测漏段
├─ Timestamp    : DateTimeOffset // UTC
├─ Entries      : LogEntry[]   // 本段全部条目（完整因果，含报错）
└─ Children     : EventStream[]// 本段新增的子树（递归；含嵌套因果）
```

**条目 `LogEntry`**：

```
LogEntry
├─ Kind      : log | attach | update     // 条目类别
├─ Level     : debug | info | warning | error
├─ Source    : string          // 来源（触发器/事件名）
├─ Message   : string
├─ Keywords  : string[]        // 扁平列表，可含 "exception:{类型名}"
├─ Data      : Dictionary<string, object?>  // 结构化载荷
└─ Timestamp : DateTimeOffset
```

- **`update` 条目**＝可播放的事件（如 `card.played`、`unit.deployed`、`card.stat.changed`）；其 `Data` 携带载荷键（如 `Card`、`Player`、`Position`）。
- **因果**：`attach` 条目与 `Children` 子树表达"谁引发了谁"，可用于表现链式反应。

## 4. 消费报错

报错**随段交付**，无需另外订阅。识别方式：`Level == Error`（或 `Keywords` 含 `exception:`）。

```csharp
foreach (var e in segment.Entries)
{
    if (e.Level == LogLevel.Error)
    {
        // e.Message 可读描述；e.Data["exceptionType"] / e.Data["message"] 为结构化信息
        ShowErrorToast(e.Message);
    }
}
```

## 5. 即时更新（可选通道）

用于"必须立刻反应"的场景（如对手出牌的提示音）。注意：

- handler **不得阻塞**（引擎机制上不等待它）。
- 监听范围＝**全部更新**；请**自行过滤**你关心的 `updateType`。
- 与段通道**互不干扰**：你既可以在 handler 里做即时反馈，也可以在帧循环里用段做完整播放。

## 6. 取消订阅

```csharp
_immediateSub.Dispose();   // 幂等；之后不再收到即时更新
```

段通道无需取消（轮询本身就是 UI 控制的）。

## 7. 约束与注意

- **handler 非阻塞**：即时更新 handler 只应做"置标志 / 入队"这类瞬时操作。
- **段无上限**：待取队列不设上限——**请确保帧循环持续取段**，否则会积压。
- **引用表示**：段内 `Data` 中的实体/位置等引用，当前（同进程）以直接引用形态给出；跨计算机的"统一引用"是**后续任务**。
- **段起点**：一段只含"本动作产生"的内容，不含历史。

---

## 8. 信号来源（UI 能看到什么）

**总原则**：UI 能监听到的一切，都来自引擎写入事件流的**条目**。条目分两类——
**①业务更新**（`Emit` 写 `update` 条目＝"游戏事实"）；**②框架自动留痕**（`log`/`attach` 条目＝"引擎过程"）。

### 8.1 业务更新信号（`update` 条目）

> **权威清单以代码常量为准**（下方为当前快照；代码库演进时以常量文件为准）。

引擎内置（`Orc.Core.Updates`）：
`card.placed`、`card.destroyed`、`card.data`、`effect.removed`。

游戏层契约（`Orc.Game.GameUpdates`）：
- 回合：`turn.start.before`、`turn.start`、`turn.start.after`、`turn.end.before`、`turn.end`
- 通用：`card.played`、`card.drawn`、`card.stat.changed`
- 卡牌：`card.load`、`card.hand.add`、`card.discarded`、`card.died`
- 单位：`unit.joined`、`unit.deployed`、`unit.position.changed`
- 卡组：`deck.shuffled`

载荷键常量：`GameUpdates.Payload*`（`Player` / `TurnNumber` / `Card` / `Unit` / `Position` / `OldPosition` / `NewPosition` / `ChangedFields` / `Deck`）。

### 8.2 框架自动留痕信号（`log` / `attach` 条目）

| 信号 | 形态 | 说明 |
|---|---|---|
| `attach` | `kind=attach` | 子执行流挂载（谁引发谁；`data` 含 `parentId`/`childId`） |
| `mount` / `unmount` | `log`（source=`bus`） | 触发器挂载/卸载；`keywords` 含触发器名与 hooks |
| `exception:{类型名}` | `log` / `level=error` | **异常隔离记录**——报错即由此进入段；`data` 含 `exceptionType`/`message` |
| `validation:rejected` | `log` / `level=warning` | 触发器被验证拒绝 |
| `stop` / `interrupt` | `log` | 执行停止 / 链中断 |
| 契约兜底失败 | `log` / `level=error` | 绑定失败等结构性错误（形态同异常记录） |

### 8.3 两条通道的信号差异

| 信号类别 | 段通道（轮询） | 即时更新口 |
|---|---|---|
| 业务更新（`Emit`） | ✅ 作为 `update` 条目在段内 | ✅ 逐条回调 |
| 框架留痕（`log`/`attach`） | ✅ 在段内 | ❌ 不含（只发业务更新） |

### 8.4 判断"我要的信号有没有"的方法

1. **查常量**：`src/Orc/Core/Updates.cs`、`src/Orc.Game/GameUpdates.cs`。
2. **查发射点**：全局搜 `Emit(` / `EmitXxx`（如 `rg "Emit" src/Orc.Game`）。
3. **实测**：跑一次对应动作 → `engine.TakeSegments()` → 看段内条目（最直观）。

---

## 9. 无来源时如何新增信号

UI 想监听某信号、但事件流里没有——**先分类缺口，再对症处理**。

### 9.1 缺口分类

| 类型 | 症状 | 处理 |
|---|---|---|
| (a) 业务没发射 | 流程跑完却无对应 `update` | 在正确时点**新增 `Emit`** |
| (b) 变化不走 `Emit` | 字段被直接改写、无信号 | 接入**通用数据改变管线**或补发射点 |
| (c) 粒度不够 | 有更新但载荷缺 UI 需要的细节 | **扩展载荷**（加键）或新增更细信号 |
| (d) UI 可自行推导 | 能由现有信号推导出 | **不需要新增**，UI 侧推导 |

### 9.2 新增业务信号的规范做法（加性、不动内核）

1. **定义常量**：在 `GameUpdates`（游戏层）新增 `const string` 更新字符串；对外契约，一经定稿冻结（`ordinal` 序数比较）。
2. **落定后发射**：在流程"状态已一致"的时点调用 `engine.Emit(...)`，并配一个 `EmitXxx` 便捷助手（对齐既有风格）。
3. **遵守引擎原则**：
   - P1「改变才传播」——有变化才发，**无变化零发射**；
   - P2「更新不允许异步」——**同步发射**（不要用后台线程）；
   - 顺序＝**先落定状态、后发射**（管线末步）；
   - 动作级信号＝**恰一次**。
4. **载荷契约**：键用常量、值可为对象引用或原始值；在 XML 注释写明语义与载荷。
5. **不要改总线**：更新字符串是**开放集合**——直接发新字符串即可，段通道与即时口会自动捕获。

### 9.3 若变化未走 `Emit`（内部字段改写）

- 优先接入既有**通用数据改变管线**（修饰链 → 更新检测 → 集中触发 `card.stat.changed`）——它已保证"改变才传播"；
- 若属新维度（管线不覆盖），则在改写点之后**补一个明确发射点**。

### 9.4 新增后的自检

- [ ] 跑该动作 → `TakeSegments()` 段内出现新信号；
- [ ] 即时更新口也收到（若经 `Emit`）；
- [ ] 无变化时零发射；
- [ ] 全量测试不红。

### 9.5 反例（不要这样做）

- ❌ 在 UI 侧用"多个信号的时间差"**猜测**引擎状态——应在引擎侧发明确信号；
- ❌ 在即时更新 handler 里做重活（会拖慢引擎）；
- ❌ 新增"只给 UI 看"的假信号——信号必须与引擎事实一致；
- ❌ 绕过 `Emit` 直接往流里写裸 `update` 条目——会破坏既有广播/留痕语义。

---

## 最小完整示例（骨架）

```csharp
public partial class MatchUiBridge : Node
{
    private LogicEngine _engine = null!;
    private IDisposable? _immediateSub;
    private readonly Queue<EventSegment> _playback = new();

    public void Attach(LogicEngine engine)
    {
        _engine = engine;

        // 注册即时更新监听（非阻塞）
        _immediateSub = _engine.OnImmediateUpdate((updateType, _) =>
        {
            if (updateType == GameUpdates.CardPlayed) PlaySfx("card_play");
        });
    }

    public override void _Process(double delta)
    {
        foreach (var seg in _engine.TakeSegments())
            _playback.Enqueue(seg);

        if (_playback.Count > 0)
            PlayOne(_playback.Peek(), delta);   // 自行调度时长/动画
    }

    public override void _ExitTree() => _immediateSub?.Dispose();

    private void PlayOne(EventSegment seg, double delta) { /* 遍历 seg.Entries 播放表现 */ }
    private void PlaySfx(string id) { /* ... */ }
}
```

---

## 相关文档

- 需求：`requirements.md`
- 实现/设计：`implementation.md`
- 术语表：项目根 `CONTEXT.md`
