namespace Orc.Core;

/// <summary>
/// 总线（S3）：按更新字符串分槽的订阅容器；只做广播，不做逻辑编排。
/// 职责：被动触发器的挂载（Mount）／按所有者卸载（UnmountOwner）／更新广播（Emit）／订阅查询读面（GetSubscribers）。
/// 一对一绑定所属引擎（经 <see cref="LogicEngine.Bus"/> 获取、不可独立构造）；多实例互不相通；单线程语义（不引入锁）。
/// 更新字符串为开放集合（不做注册/白名单）；同一性判定＝ordinal 序数、大小写敏感、逐字符原样比较（不做归一化）。
/// 入流与留痕：Mount／UnmountOwner 写 kind=log 条目标识动作；Emit 写 kind=update 条目——
/// 均写入「当前执行者流（无则总流）」；Emit 在调用一刻捕获目标流一次、整轮固定；
/// 订阅者执行流挂载到「发射时的当前执行者流（无则总流）」（沿用 S2 执行帧机制）。
/// </summary>
public sealed class Bus
{
    private readonly LogicEngine _engine;
    private readonly Dictionary<string, List<Subscription>> _subscriptions = new(StringComparer.Ordinal);
    private readonly List<Subscription> _groups = new(); // 全部挂载组（保持挂载序：卸载遍历与留痕顺序确定性来源）
    private long _mountSequence;

    /// <summary>创建总线（仅限所属引擎内部创建：总线不可脱离引擎独立构造）。</summary>
    internal Bus(LogicEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// 挂载被动触发器：读取其 hooks（构造期声明，声明顺序保留）注册到对应更新字符串的订阅表；注册即生效。
    /// 拒绝集（均不产生留痕）：trigger 为 null → <see cref="ArgumentNullException"/>；
    /// Kind=Active（主动触发器不挂载）／hooks 为空／已处于挂载状态（含跨总线重复挂载；卸载后复位可重挂）→ <see cref="InvalidOperationException"/>。
    /// 成功时写 mount 留痕（source="bus"；keywords＝["mount", 触发器标识, 各挂载更新字符串按声明序…]）。
    /// </summary>
    public void Mount<TView>(Trigger<TView> trigger) where TView : class
    {
        ArgumentNullException.ThrowIfNull(trigger);

        if (trigger.Kind != TriggerKind.Passive)
        {
            throw new InvalidOperationException(
                $"主动触发器（Kind=Active）不可挂载；仅被动触发器（Kind=Passive 且 hooks 非空）可挂载。触发器：'{trigger.DisplayName}'。");
        }

        if (trigger.Hooks.Count == 0)
        {
            throw new InvalidOperationException(
                $"触发器 '{trigger.DisplayName}' 的 hooks 为空，不可挂载（无任何订阅目标）。");
        }

        if (trigger.MountedBus is not null)
        {
            throw new InvalidOperationException(
                $"触发器 '{trigger.DisplayName}' 已处于挂载状态（同一实例同一时间只能挂载到一个总线；须先卸载）。");
        }

        var subscription = new Subscription(
            trigger.DisplayName,
            trigger.MountPriority,
            trigger.Owner,
            trigger.Hooks,
            _mountSequence++,
            (payload, ct) => trigger.InvokeAsync(_engine, ClonePayload(payload), ct),
            () => trigger.MountedBus = null);

        foreach (var hook in subscription.Hooks)
        {
            _engine.Hooks.Register(hook); // S-C1/S-C2：hook 首次出现即登记进引擎词汇表（对局内固化）
            if (!_subscriptions.TryGetValue(hook, out var list))
            {
                list = new List<Subscription>();
                _subscriptions[hook] = list;
            }

            list.Add(subscription);
        }

        _groups.Add(subscription);
        trigger.MountedBus = this;

        _engine.Orchestration.Register(trigger, "bus-mount"); // S-C3：内核自动采样（挂载面）

        WriteTrace("mount", $"已挂载触发器 '{subscription.DisplayName}'（更新：{string.Join("、", subscription.Hooks)}）。", subscription);
    }

    /// <summary>
    /// 按所有者卸载：移除该 owner 跨所有更新字符串的全部挂载（引用相等 ReferenceEquals 匹配——owner 语义＝对象身份）。
    /// owner 为 null → <see cref="ArgumentNullException"/>（不把 null 特判为"无主"）。
    /// 幂等清理语义：未命中（该 owner 无任何挂载）＝无操作、不写留痕；命中时每个受影响触发器写一条 unmount 留痕。
    /// 卸载不打断在途 Emit 轮次（快照语义）；卸载后触发器状态复位（可再次挂载、按新挂载时点参与排序）。
    /// </summary>
    public void UnmountOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        List<Subscription>? affected = null;
        foreach (var subscription in _groups)
        {
            if (ReferenceEquals(subscription.Owner, owner))
            {
                (affected ??= new List<Subscription>()).Add(subscription);
            }
        }

        if (affected is null)
        {
            return; // 未命中：幂等无操作、不写留痕
        }

        foreach (var subscription in affected)
        {
            foreach (var hook in subscription.Hooks)
            {
                if (_subscriptions.TryGetValue(hook, out var list))
                {
                    list.Remove(subscription);
                    if (list.Count == 0)
                    {
                        _subscriptions.Remove(hook);
                    }
                }
            }

            _groups.Remove(subscription);
            subscription.Unmark();
            WriteTrace("unmount", $"已卸载触发器 '{subscription.DisplayName}'（更新：{string.Join("、", subscription.Hooks)}）。", subscription);
        }
    }

    /// <summary>
    /// 更新广播：把更新写为目标流的一条 update 条目（发射时捕获一次、整轮固定；先写条目、后广播），
    /// 随后按「挂载优先级升序（数值小者先）→注册序升序（先挂先执行）」顺序 await 各订阅者（快照：执行期间的挂载/卸载变化不影响本轮）。
    /// 订阅者以 payload 为执行数据（键值原样；payload=null 视为空载荷）经统一入口激活；
    /// 订阅者执行流挂载到「发射时的当前执行者流（无则总流）」。
    /// 订阅者传播出的取消类异常 → 停止后续订阅者、原样向上穿透；其余异常 → 记录进发射者流（Error 级；source＝订阅者标识）并继续。
    /// </summary>
    /// <exception cref="ArgumentNullException">updateType 为 null。</exception>
    /// <exception cref="ArgumentException">updateType 为空或纯空白。</exception>
    /// <exception cref="OperationCanceledException">ct 已取消（入口即抛，不写条目、不广播）；或订阅者执行传出取消类异常（穿透上抛）。</exception>
    public async Task Emit(
        string updateType, IReadOnlyDictionary<string, object?>? payload = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updateType);
        if (string.IsNullOrWhiteSpace(updateType))
        {
            throw new ArgumentException("更新字符串不能为空或纯空白。", nameof(updateType));
        }

        ct.ThrowIfCancellationRequested(); // 已取消：不写条目、不广播

        var capturedStream = ExecutionFrame.Current?.Stream ?? _engine.RootStream; // 发射时捕获一次、整轮固定
        var snapshot = SnapshotFor(updateType);

        // 更新条目：source="bus"；message＝更新类型字面值；keywords＝恰单元素 [updateType]；data＝载荷键值（null → 空字典）；level=Info
        capturedStream.WriteUpdate("bus", updateType, LogLevel.Info, new[] { updateType }, payload);

        // S5：外部通道通知（桥 → 订阅回调；各通道异常隔离、取消类穿透；先于订阅者广播）
        await _engine.NotifyExternalObservers(updateType, payload, capturedStream, ct);

        foreach (var subscription in snapshot)
        {
            try
            {
                await subscription.Invoke(payload, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消类异常：停止后续订阅者、原样向上穿透
            }
            catch (Exception ex)
            {
                // 传播出的失败（绑定失败/入口阶段异常等）：记录进发射者流（与 update 条目同处）、继续后续订阅者；
                // source＝订阅者标识（纯展示名，无事件上下文）；条目形态沿用 S2 隔离记录同构。
                capturedStream.WriteLog(
                    subscription.DisplayName,
                    ex.Message,
                    LogLevel.Error,
                    new[] { $"exception:{ex.GetType().Name}" },
                    new Dictionary<string, object?>
                    {
                        ["exceptionType"] = ex.GetType().FullName,
                        ["message"] = ex.Message,
                    });
            }
        }
    }

    /// <summary>
    /// 订阅查询读面：返回该更新字符串的订阅者标识有序只读快照（顺序与执行序一致；元素＝触发器展示名——名称非空白用名称，否则视图类型名退化）。
    /// null → <see cref="ArgumentNullException"/>；空/纯空白 → <see cref="ArgumentException"/>；未知更新/空订阅 → 空列表。
    /// 快照＝调用时点的一致副本：其后挂载变化不影响已返回快照。
    /// </summary>
    public IReadOnlyList<string> GetSubscribers(string updateType)
    {
        ArgumentNullException.ThrowIfNull(updateType);
        if (string.IsNullOrWhiteSpace(updateType))
        {
            throw new ArgumentException("更新字符串不能为空或纯空白。", nameof(updateType));
        }

        var snapshot = SnapshotFor(updateType);
        var names = new string[snapshot.Length];
        for (var i = 0; i < snapshot.Length; i++)
        {
            names[i] = snapshot[i].DisplayName;
        }

        return names;
    }

    /// <summary>
    /// 全量 hook 枚举（S-C2 加性只读面）：返回当前已挂载订阅涉及的 hook（按首次出现序＝挂载序快照），
    /// 每项含 hook 名、其 <see cref="HookId"/> 与订阅者展示名（执行序）。
    /// 快照语义＝调用时点一致副本；无订阅＝空列表。
    /// </summary>
    public IReadOnlyList<HookSubscriptions> EnumerateHooks()
    {
        var result = new List<HookSubscriptions>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subscription in _groups)
        {
            foreach (var hook in subscription.Hooks)
            {
                if (!seen.Add(hook))
                {
                    continue; // 已收录：跳过（保持首次出现序）
                }

                var snapshot = SnapshotFor(hook);
                var names = new string[snapshot.Length];
                for (var i = 0; i < snapshot.Length; i++)
                {
                    names[i] = snapshot[i].DisplayName;
                }

                result.Add(new HookSubscriptions(hook, HookId.FromName(hook), names));
            }
        }

        return result;
    }

    /// <summary>写一条动作留痕（Mount/Unmount；source="bus"、Info 级；目标流＝当前执行者流（无则总流））。</summary>
    private void WriteTrace(string actionKeyword, string message, Subscription subscription)
    {
        var keywords = new List<string>(subscription.Hooks.Count + 2) { actionKeyword, subscription.DisplayName };
        keywords.AddRange(subscription.Hooks);

        var stream = ExecutionFrame.Current?.Stream ?? _engine.RootStream;
        stream.WriteLog("bus", message, LogLevel.Info, keywords);
    }

    /// <summary>取该更新的订阅者快照（复制的稳定数组；排序＝挂载优先级升序→注册序升序）。</summary>
    private Subscription[] SnapshotFor(string updateType)
    {
        if (!_subscriptions.TryGetValue(updateType, out var list) || list.Count == 0)
        {
            return Array.Empty<Subscription>();
        }

        var snapshot = list.ToArray();
        Array.Sort(snapshot, static (a, b) =>
        {
            var byPriority = a.Priority.CompareTo(b.Priority); // 数值小者先（升序）
            return byPriority != 0 ? byPriority : a.Seq.CompareTo(b.Seq); // 同优先级：先挂先执行（升序）
        });
        return snapshot;
    }

    /// <summary>把只读载荷转为执行入口可接收的字典（浅拷贝；null 保持 null＝空载体）。</summary>
    private static IDictionary<string, object?>? ClonePayload(IReadOnlyDictionary<string, object?>? payload)
        => payload is null ? null : new Dictionary<string, object?>(payload);

    /// <summary>一次挂载的订阅记录（内部）：触发器实例级挂载组——跨其全部 hooks 共享注册序与生命周期。</summary>
    private sealed class Subscription
    {
        internal Subscription(
            string displayName,
            int priority,
            object? owner,
            IReadOnlyList<string> hooks,
            long seq,
            Func<IReadOnlyDictionary<string, object?>?, CancellationToken, Task> invoke,
            Action unmark)
        {
            DisplayName = displayName;
            Priority = priority;
            Owner = owner;
            Hooks = hooks;
            Seq = seq;
            Invoke = invoke;
            Unmark = unmark;
        }

        /// <summary>触发器标识（展示名；读面/留痕/错误记录共用语义）。</summary>
        internal string DisplayName { get; }

        /// <summary>挂载优先级（构造期声明；升序执行——数值小者先）。</summary>
        internal int Priority { get; }

        /// <summary>所有者（ReferenceEquals 匹配）。</summary>
        internal object? Owner { get; }

        /// <summary>hooks 声明顺序（留痕 keywords 与其他涉及范围共用）。</summary>
        internal IReadOnlyList<string> Hooks { get; }

        /// <summary>注册序（Mount 时点序号；跨全部 hooks 一致；重挂分配新序号）。</summary>
        internal long Seq { get; }

        /// <summary>订阅者激活入口（已绑定所属引擎；payload 键值原样传入本次执行 ctx）。</summary>
        internal Func<IReadOnlyDictionary<string, object?>?, CancellationToken, Task> Invoke { get; }

        /// <summary>挂载状态复位（卸载时由总线调用）。</summary>
        internal Action Unmark { get; }
    }
}

/// <summary>
/// hook 订阅读面项（S-C2；<see cref="Bus.EnumerateHooks"/> 产物）：hook 名、其定义级标识与订阅者展示名。
/// </summary>
/// <param name="Hook">hook 名（更新字符串）。</param>
/// <param name="Id">hook 的定义级稳定标识（<see cref="HookId.FromName"/>）。</param>
/// <param name="Subscribers">订阅者展示名（执行序：挂载优先级升序→注册序升序）。</param>
public sealed record HookSubscriptions(string Hook, HookId Id, IReadOnlyList<string> Subscribers);
