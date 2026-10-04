using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 施放动作接线面（S5；主动效果/指令）：非泛型公开接口，供引擎级共享指令流程（<see cref="OrderFlow"/>）
/// 在不依赖具体视图类型的前提下执行指令的施放链。<see cref="ActiveEffect{TView}"/> 实现本接口。
/// 施放信息（来源/目标/数值）由指令流程在执行时经数据通道提供，不经本接口预绑。
/// </summary>
public interface ICastAction
{
    /// <summary>指令名（供反制等检查位按名判定；与 <see cref="Effect.Name"/> 同源）。</summary>
    string Name { get; }

    /// <summary>
    /// 执行施放链（施放 ＝ 主触发器被调用；其施放事件链按注册序执行）。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    Task<EventStream> CastAsync(
        LogicEngine engine, IDictionary<string, object?>? data = null, CancellationToken ct = default);
}
