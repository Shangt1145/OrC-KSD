using Orc.Core;

namespace Orc.Game.Cards.Data;

/// <summary>
/// 组件加载上下文（P5＝a）：装载链调用组件 loader 时传入的**受控服务集合**——
/// 显式列出所需的对局级服务（非"整个引擎当上下文"，对齐既有 <c>KeywordLoadContext</c> 的受控集合性质）。
/// 不携带本卡数据体与 owner（<c>card.Definition</c> 已是单一真源，避免第二入口）。
/// 服务均为**延迟访问**（对局装配顺序下目标在卡实例化后才就绪；缺省＝null＝独立构造／部分装配——消费侧防御跳过）。
/// </summary>
public sealed class CardComponentLoadContext
{
    private readonly Func<KeywordLoadContext?>? _keywords;
    private readonly Func<CardEffectLoadContext?>? _effects;

    /// <summary>创建加载上下文（可选服务面均可缺省——缺省＝null＝功能不可用、不抛错）。</summary>
    /// <param name="engine">引擎引用（发射/装载所需）。</param>
    /// <param name="keywords">词条装载上下文提供器（延迟读取；缺省＝null）。</param>
    /// <param name="effects">效果装载上下文提供器（延迟读取；缺省＝null）。</param>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public CardComponentLoadContext(
        LogicEngine engine,
        Func<KeywordLoadContext?>? keywords = null,
        Func<CardEffectLoadContext?>? effects = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        _keywords = keywords;
        _effects = effects;
    }

    /// <summary>引擎引用。</summary>
    public LogicEngine Engine { get; }

    /// <summary>词条装载上下文（延迟读取；独立构造／未装配＝null）。</summary>
    public KeywordLoadContext? KeywordLoadContext => _keywords?.Invoke();

    /// <summary>效果装载上下文（延迟读取；独立构造／未装配＝null）。</summary>
    public CardEffectLoadContext? EffectLoadContext => _effects?.Invoke();
}
