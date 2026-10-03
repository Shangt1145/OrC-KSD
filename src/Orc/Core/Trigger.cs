namespace Orc.Core;

/// <summary>触发器种类：主动（默认）／被动（S3 起：声明 hooks 后经总线挂载、响应更新激活；主动不可挂载）。</summary>
public enum TriggerKind
{
    /// <summary>主动触发器（默认；由编排/动作直接调用；不参与挂载）。</summary>
    Active,

    /// <summary>被动触发器（需显式声明；S3 起声明 hooks 后经总线挂载，被更新激活）。</summary>
    Passive,
}

/// <summary>
/// 触发器（S3 形态）：一个触发器固定绑定一个视图类型（泛型编译期承载「一个触发器一个 ContextView」约束）。
/// 内部维护 band 方案（构造期声明：缺省＝内置默认枚举；显式＝专门枚举）与事件注册表；
/// 事件排序键＝band 值×1000＋band 内优先级＋注册序兜底（仅排列同一触发器内的事件）。
/// S3 追加：hook 声明（构造期；非空即被动语义）、挂载优先级与所有者（均构造期声明；三者仅内部使用、公共面不暴露）。
/// 注册表与挂载状态（MountedBus）为可变面——除事件注册与总线挂载（Mount/UnmountOwner）外无跨调用可变状态。
/// 开放继承（额外注入 handler 途径的基座）：执行与流产出只经公共执行入口；注册/校验只经公共注册面；
/// 子类不得引入新的跨调用可变状态面。
/// 触发入口三形态（同一 InvokeAsync 重载族）：①统一入口（本类提供）；②具名重载（作者在具体触发器/子类/调用侧书写，
/// 经手写 Translate 汇入①）；③字典透传（调用方就绪字典直接调用①，零加工）。框架不隐式调用 Translate。
/// </summary>
/// <typeparam name="TView">该触发器唯一的视图类型（作者视图类）。</typeparam>
public class Trigger<TView> where TView : class
{
    private const int MaxBandValue = 1_000_000;
    private const int PriorityUpperBound = 1000;

    private readonly List<EventEntry> _events = new();
    private readonly Type? _bandType;
    private readonly string[] _hooks;
    private readonly int _mountPriority;
    private readonly object? _owner;
    private long _seq;

    /// <summary>
    /// 以初始化形态构造触发器（名称〔可选〕、Kind〔默认主动〕、band 方案〔默认缺省〕、初始事件集合〔可选〕、
    /// hooks 声明〔S3；可选〕、挂载优先级〔S3；默认 0＝Normal〕、所有者〔S3；可选〕）。
    /// 初始事件集合与注册 API 为等价通道（逐项走相同装配校验）。
    /// hooks 声明时机仅构造期（无运行期注册 API）：非空即被动语义；每项须非空、非纯空白、不重复（声明顺序保留）。
    /// </summary>
    /// <exception cref="ArgumentNullException">events 集合包含 null 元素。</exception>
    /// <exception cref="ArgumentException">band 方案非法（非枚举）；初始事件与 band 方案不匹配；
    /// hooks 非法（含 null/空白/重复项），或主动触发器（Kind=Active）声明非空 hooks。</exception>
    /// <exception cref="ArgumentOutOfRangeException">初始事件的 band 值/优先级越界。</exception>
    public Trigger(
        string? name = null,
        TriggerKind kind = TriggerKind.Active,
        Type? bandType = null,
        IEnumerable<TriggerEvent<TView>>? events = null,
        IEnumerable<string>? hooks = null,
        int priority = 0,
        object? owner = null)
    {
        Name = name;
        Kind = kind;

        if (bandType is not null && !bandType.IsEnum)
        {
            throw new ArgumentException(
                $"band 方案必须是 C# 枚举类型；'{bandType.FullName}' 不是枚举。", nameof(bandType));
        }

        _bandType = bandType;
        _hooks = ValidateAndCopyHooks(hooks, kind);
        _mountPriority = priority;
        _owner = owner;

        if (events is not null)
        {
            foreach (var item in events)
            {
                if (item is null)
                {
                    throw new ArgumentNullException(nameof(events), "初始事件集合不能包含 null 元素。");
                }

                AddEvent(item.Name, item.Handler, item.Band, item.Priority);
            }
        }
    }

    /// <summary>S3 hooks 校验与拷贝（构造期一致性校验）：每项非空、非纯空白、不重复；主动+非空＝拒绝。</summary>
    private static string[] ValidateAndCopyHooks(IEnumerable<string>? hooks, TriggerKind kind)
    {
        if (hooks is null)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var hook in hooks)
        {
            if (hook is null)
            {
                throw new ArgumentException("hooks 不能包含 null 元素。", nameof(hooks));
            }

            if (string.IsNullOrWhiteSpace(hook))
            {
                throw new ArgumentException("hook 不能为空或纯空白字符串。", nameof(hooks));
            }

            if (list.Contains(hook))
            {
                throw new ArgumentException($"hooks 列表包含重复项 '{hook}'（同一列表中不允许重复）。", nameof(hooks));
            }

            list.Add(hook);
        }

        if (list.Count > 0 && kind == TriggerKind.Active)
        {
            throw new ArgumentException(
                "主动触发器（Kind=Active）不能声明 hooks（矛盾声明：主动触发器不参与挂载）。", nameof(hooks));
        }

        return list.ToArray();
    }

    /// <summary>触发器名称（可选；用于事件流 source 与调试；未命名时 source 以视图类型名退化标识）。</summary>
    public string? Name { get; }

    /// <summary>触发器种类（运行期可读；S3 起与「挂载条件」关联——仅 Passive 且 hooks 非空可挂载）。</summary>
    public TriggerKind Kind { get; }

    /// <summary>hook 声明（构造期；声明顺序保留；S3 挂载机制内部使用、公共面不暴露）。</summary>
    internal IReadOnlyList<string> Hooks => _hooks;

    /// <summary>挂载优先级声明（构造期；任意 int；S3 挂载机制内部使用、公共面不暴露）。</summary>
    internal int MountPriority => _mountPriority;

    /// <summary>所有者声明（构造期；可选、可为 null；S3 卸载机制内部使用、公共面不暴露）。</summary>
    internal object? Owner => _owner;

    /// <summary>当前挂载的总线（null＝未挂载；实例级状态；仅由总线 Mount/UnmountOwner 读写）。</summary>
    internal Bus? MountedBus { get; set; }

    /// <summary>注册一个不带 band 的事件（落默认区段；仅默认 band 方案可用）。</summary>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    /// <exception cref="ArgumentException">事件名为空；默认方案下 band 校验不适用项。</exception>
    /// <exception cref="ArgumentOutOfRangeException">优先级越界（须满足 0 ≤ 优先级 &lt; 1000）。</exception>
    public void Register(string name, Func<TView, Context, CancellationToken, Task> handler, int priority = 0)
        => AddEvent(name, handler, band: null, priority);

    /// <summary>注册一个带 band 的事件（band 成员须与触发器声明的方案一致；专门方案下必须显式携带）。</summary>
    /// <exception cref="ArgumentNullException">handler 或 band 为 null。</exception>
    /// <exception cref="ArgumentException">事件名为空；band 成员与已声明方案不一致（二选一保护）。</exception>
    /// <exception cref="ArgumentOutOfRangeException">band 值或优先级越界（band 须为自然数且 ≤ 1,000,000）。</exception>
    public void Register(string name, Func<TView, Context, CancellationToken, Task> handler, Enum band, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(band);
        AddEvent(name, handler, band, priority);
    }

    /// <summary>
    /// 统一入口（框架提供；规范收敛点）：以就绪数据创建本次执行 ctx（沿用「入料拷贝一次」语义；data 为 null 视作空载体）
    /// → 绑定新建视图会话 → 按排序键顺序执行事件链 → 返回事件流。本身不触发转接（不隐式调用 Translate）。
    /// 嵌套调用自动感知执行栈：子流自动挂载到触发者流（无触发者时挂载到传入引擎的总流）；
    /// 已中断状态下的链上新执行空转（不执行任何事件、不进行视图绑定、正常返回空流）。
    /// 绑定阶段失败（契约/配置错误）原样传播；事件内业务异常被隔离记录并继续；取消类异常穿透上抛（最外层拿不到流）。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    /// <exception cref="KeyNotFoundException">视图必填数据缺失（绑定失败）。</exception>
    /// <exception cref="ArgumentException">视图声明非法（绑定失败）。</exception>
    /// <exception cref="OperationCanceledException">取消类异常穿透上抛（不隔离）。</exception>
    public async Task<EventStream> InvokeAsync(
        LogicEngine engine, IDictionary<string, object?>? data = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var parent = ExecutionFrame.Current;
        var label = DisplayName;
        var stream = new EventStream();
        stream.AttachTo(
            parent?.Stream ?? engine.RootStream,
            parent is null ? label : ExecutionFrame.BuildSource(parent.TriggerLabel, parent.CurrentEventName));

        var frame = new ExecutionFrame(parent, stream, label);
        ExecutionFrame.Current = frame;
        try
        {
            if (ExecutionFrame.IsChainInterrupted(parent))
            {
                return stream; // 空转：不执行任何事件、不进行视图绑定
            }

            var ctx = new Context(data);
            ctx.AttachFrame(frame);
            frame.AttachContext(ctx);

            var view = ContextViewBinder.Create<TView>(ctx);
            try
            {
                await RunEventsAsync(view, ctx, frame, ct);
            }
            finally
            {
                ((IContextView)(object)view).Seal();
            }

            return stream;
        }
        finally
        {
            ExecutionFrame.Current = parent;
        }
    }

    private async Task RunEventsAsync(TView view, Context ctx, ExecutionFrame frame, CancellationToken ct)
    {
        var snapshot = SnapshotSorted(); // 执行时点的稳定视图；执行期间的注册变化不影响本次迭代
        foreach (var item in snapshot)
        {
            if (frame.Stopped || frame.Interrupted)
            {
                break; // 事件边界＝唯一检查点（in-flight 事件必跑完才停）
            }

            frame.CurrentEventName = item.Name;
            try
            {
                await item.Handler(view, ctx, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // 取消类异常不隔离、穿透上抛（途经各层同此）
            }
            catch (Exception ex)
            {
                // 异常隔离：记录并继续后续事件（已发生的 ctx 改写保留；隔离≠回滚）
                frame.Stream.Write(
                    LogEntryKind.Log,
                    LogLevel.Error,
                    ExecutionFrame.BuildSource(frame.TriggerLabel, item.Name),
                    ex.Message,
                    new[] { $"exception:{ex.GetType().Name}" },
                    new Dictionary<string, object?>
                    {
                        ["exceptionType"] = ex.GetType().FullName,
                        ["message"] = ex.Message,
                    });
            }
        }
    }

    private void AddEvent(string name, Func<TView, Context, CancellationToken, Task> handler, Enum? band, int priority)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("事件名不能为空（用途：日志与事件流定位）。", nameof(name));
        }

        if (priority < 0 || priority >= PriorityUpperBound)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priority), priority, $"事件内优先级须满足 0 ≤ 优先级 < {PriorityUpperBound}。");
        }

        var bandValue = ResolveBandValue(band);
        _events.Add(new EventEntry(name, handler, bandValue, priority, _seq++));
    }

    /// <summary>band 成员解析与二选一保护校验（默认方案／专门方案的判定矩阵）。</summary>
    private int ResolveBandValue(Enum? band)
    {
        if (_bandType is null)
        {
            // 默认方案：省略 band → 落 default 区段；仅接受默认枚举成员。
            if (band is null)
            {
                return 0;
            }

            if (band.GetType() != typeof(DefaultBands))
            {
                throw new ArgumentException(
                    $"触发器未声明专门 band 方案（默认方案），不接受枚举 '{band.GetType().Name}' 的成员；如需自定义区段，请经构造参数 bandType 显式声明专门枚举方案。",
                    nameof(band));
            }
        }
        else
        {
            // 专门方案：必须显式携带本枚举成员。
            if (band is null)
            {
                throw new ArgumentException(
                    $"触发器已声明专门 band 方案 '{_bandType.Name}'，注册必须显式携带该枚举成员（省略 band 被拒绝）。",
                    nameof(band));
            }

            if (band.GetType() != _bandType)
            {
                throw new ArgumentException(
                    $"band 成员所属枚举 '{band.GetType().Name}' 与触发器声明的方案 '{_bandType.Name}' 不一致（他类枚举成员被拒绝）。",
                    nameof(band));
            }
        }

        long value;
        try
        {
            value = Convert.ToInt64(band);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(band), band, $"band 值超出支持范围（须为自然数且 ≤ {MaxBandValue:N0}）。");
        }

        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(band), value, "band 值必须为自然数（≥ 0）。");
        }

        if (value > MaxBandValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(band), value, $"band 值超过上限 {MaxBandValue:N0}（防打包溢出）。");
        }

        return (int)value;
    }

    private EventEntry[] SnapshotSorted()
    {
        var snapshot = _events.ToArray();
        Array.Sort(snapshot, static (a, b) =>
        {
            var c = a.SortKey.CompareTo(b.SortKey);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });
        return snapshot;
    }

    /// <summary>source/调试/总线读面用显示名：名称（非空白）或视图类型名（未命名退化）。</summary>
    internal string DisplayName => string.IsNullOrWhiteSpace(Name) ? typeof(TView).Name : Name!;

    private sealed class EventEntry
    {
        internal EventEntry(
            string name,
            Func<TView, Context, CancellationToken, Task> handler,
            int bandValue,
            int priority,
            long seq)
        {
            Name = name;
            Handler = handler;
            BandValue = bandValue;
            Priority = priority;
            Seq = seq;
        }

        internal string Name { get; }

        internal Func<TView, Context, CancellationToken, Task> Handler { get; }

        internal int BandValue { get; }

        internal int Priority { get; }

        internal long Seq { get; }

        /// <summary>排序键＝band 值×1000＋band 内优先级。</summary>
        internal long SortKey => (long)BandValue * PriorityUpperBound + Priority;
    }
}

/// <summary>
/// 执行帧（内部）：一次执行（顶层或嵌套）的运行期会话。
/// 承担：①当前执行者感知（AsyncLocal 栈；执行会话入/出栈——子流挂载、Interrupt 传播、空转判定共用）；
/// ②检查点状态（Stopped/Interrupted）与 ctx 状态查询面共用的唯一状态源；
/// ③source 构成素材（触发器显示名＋当前事件名）。
/// </summary>
internal sealed class ExecutionFrame
{
    private static readonly AsyncLocal<ExecutionFrame?> CurrentFrame = new();

    /// <summary>当前执行的栈顶帧（AsyncLocal；会话进入时入栈、退出时还原）。</summary>
    internal static ExecutionFrame? Current
    {
        get => CurrentFrame.Value;
        set => CurrentFrame.Value = value;
    }

    private Context? _context;

    internal ExecutionFrame(ExecutionFrame? parent, EventStream stream, string triggerLabel)
    {
        Parent = parent;
        Stream = stream;
        TriggerLabel = triggerLabel;
    }

    /// <summary>父帧（触发者执行会话；无父时为 null——顶层）。</summary>
    internal ExecutionFrame? Parent { get; }

    /// <summary>本次执行的流。</summary>
    internal EventStream Stream { get; }

    /// <summary>触发器显示名（名称或类型名）。</summary>
    internal string TriggerLabel { get; }

    /// <summary>当前正在执行的事件名（source 构成用；未进入事件时为 null）。</summary>
    internal string? CurrentEventName { get; set; }

    /// <summary>本级停止（Stop）标记。</summary>
    internal bool Stopped { get; set; }

    /// <summary>链中断（Interrupt）标记。</summary>
    internal bool Interrupted { get; set; }

    internal void AttachContext(Context context) => _context = context;

    /// <summary>Stop（仅本级）：标记本层剩余事件停止；不影响父层与子执行；写入事件流。</summary>
    internal void RequestStop()
    {
        if (_context is null)
        {
            return; // 未关联执行会话（执行路径之外）防御：无操作
        }

        Stopped = true;
        Stream.Write(
            LogEntryKind.Log,
            LogLevel.Info,
            BuildSource(TriggerLabel, CurrentEventName),
            "执行被停止（Stop）：本级剩余事件不再执行。",
            new[] { "stop" },
            data: null);
    }

    /// <summary>
    /// Interrupt（链级）：标记当前活跃执行栈全部帧（Interrupt 所在层、全部祖先层，以及正在运行的更深子层）；
    /// 各层在各自下一事件边界检查后停止；已中断状态下链上新发起的执行空转。写入事件流。
    /// </summary>
    internal void RequestInterrupt()
    {
        if (_context is null)
        {
            return;
        }

        Stream.Write(
            LogEntryKind.Log,
            LogLevel.Warning,
            BuildSource(TriggerLabel, CurrentEventName),
            "执行被中断（Interrupt）：动作链取消，链上剩余事件不再执行。",
            new[] { "interrupt" },
            data: null);

        var top = Current;
        if (top is not null)
        {
            for (var frame = top; frame is not null; frame = frame.Parent)
            {
                frame.Interrupted = true;
            }
        }
        else
        {
            for (var frame = this; frame is not null; frame = frame.Parent)
            {
                frame.Interrupted = true;
            }
        }
    }

    /// <summary>沿帧链（含起点帧）检查是否存在中断标记（用于已中断状态下的空转判定）。</summary>
    internal static bool IsChainInterrupted(ExecutionFrame? frame)
    {
        for (var f = frame; f is not null; f = f.Parent)
        {
            if (f.Interrupted)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>source 构成：触发器显示名（未命名时已退化为类型名）＋事件名（无事件上下文时省略）。</summary>
    internal static string BuildSource(string triggerLabel, string? eventName)
        => string.IsNullOrEmpty(eventName) ? triggerLabel : $"{triggerLabel}/{eventName}";
}
