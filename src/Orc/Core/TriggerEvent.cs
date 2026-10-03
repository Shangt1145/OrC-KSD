namespace Orc.Core;

/// <summary>
/// 触发器内单个事件的装配描述（注册项）：事件名（日志与事件流定位用；允许重复，不承担唯一键职责）、
/// handler、band 成员（依触发器的 band 方案可省）、band 内优先级。本类型不持任何运行期状态。
/// 构造通道与注册 API 的校验一致（由触发器在装配/注册期执行，见 <see cref="Trigger{TView}"/>）。
/// </summary>
/// <typeparam name="TView">事件所属触发器的视图类型。</typeparam>
public sealed class TriggerEvent<TView> where TView : class
{
    /// <summary>无 band 形态：事件落默认区段（仅默认方案可用；专门方案下注册会被拒绝）。</summary>
    /// <exception cref="ArgumentNullException">name 或 handler 为 null。</exception>
    public TriggerEvent(string name, Func<TView, Context, CancellationToken, Task> handler, int priority = 0)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Band = null;
        Priority = priority;
    }

    /// <summary>带 band 形态：band 成员须与触发器声明的方案一致（注册期校验）。</summary>
    /// <exception cref="ArgumentNullException">name、handler 或 band 为 null。</exception>
    public TriggerEvent(string name, Func<TView, Context, CancellationToken, Task> handler, Enum band, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(band);

        Name = name ?? throw new ArgumentNullException(nameof(name));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Band = band;
        Priority = priority;
    }

    /// <summary>事件名（日志/事件流定位用）。</summary>
    public string Name { get; }

    /// <summary>事件处理器：Task(TView view, Context ctx, CancellationToken ct)。</summary>
    public Func<TView, Context, CancellationToken, Task> Handler { get; }

    /// <summary>band 成员（未携带时为 null，落默认区段）。</summary>
    public Enum? Band { get; }

    /// <summary>band 内优先级（默认 0；须满足 0 ≤ 优先级 &lt; 1000）。</summary>
    public int Priority { get; }
}
