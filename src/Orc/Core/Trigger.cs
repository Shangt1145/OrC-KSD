namespace Orc.Core;

/// <summary>
/// 最小触发器壳（S1）：一个触发器固定绑定一个视图类型（编译期泛型参数固定，类型系统承载「一个触发器只有一个 ContextView」约束），
/// 每次执行把一个 Context 的数据绑定到新建的视图会话，并交给执行委托。ctx 由同一触发器跨事件/跨执行共享。
/// 不实现事件列表 / 优先级 / 传参（S2 扩展）；触发器自身无可变状态（除不可变的执行委托引用外无任何跨调用状态）。
/// </summary>
/// <typeparam name="TView">该触发器唯一的视图类型（作者视图类）。</typeparam>
public sealed class Trigger<TView> where TView : class
{
    private readonly Func<TView, Task> _handler;

    /// <summary>以执行委托构造触发器。委托只接收视图（唯一数据通道）、返回 Task、不接收取消令牌。</summary>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    public Trigger(Func<TView, Task> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// 执行一次：显式传入 Context（一律显式，不提供缺省创建）。
    /// 绑定（含必填校验）失败时不产出视图、不执行执行委托；委托结束（含异常路径）后自动 Seal；
    /// 期间异常（绑定 / 委托）一律原样传播，不捕获、不包装、不吞掉。
    /// </summary>
    /// <exception cref="ArgumentNullException">ctx 为 null。</exception>
    public async Task RunAsync(Context ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var view = ContextViewBinder.Create<TView>(ctx);
        try
        {
            await _handler(view);
        }
        finally
        {
            ((IContextView)(object)view).Seal();
        }
    }
}
