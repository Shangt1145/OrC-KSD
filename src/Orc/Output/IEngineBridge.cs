namespace Orc.Output;

/// <summary>
/// 引擎桥（S5 骨架）：引擎→外部的主动调用契约。宿主实现本接口（编译期强类型）并经 <see cref="Orc.Core.LogicEngine.Bridge"/> 装配；
/// 引擎在「更新广播（Emit）」关键时点调用；未装配（null）时引擎照常运转、不调用。
/// 与订阅回调（<see cref="Orc.Core.LogicEngine.Subscribe"/>）为同一更新路径的两种独立通道：
/// 桥＝编译期强类型单点契约；回调＝运行期多注册弱形态。
/// 骨架边界：仅「更新通知」一个点位；桥方法异常被隔离（记录、继续后续通道与广播），不破坏更新广播主流程。
/// </summary>
public interface IEngineBridge
{
    /// <summary>更新通知（Emit 时点；更新条目写入后、订阅者广播前调用）。</summary>
    Task OnUpdate(string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct);
}
