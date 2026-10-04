using Orc.Cards;
using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Cards;

/// <summary>
/// 卡牌库：id → 卡牌定义的注册表（代码注册 API；本批无 JSON 载入）；
/// 负责名单实例化时创建卡牌实例——按定义类别产出 <see cref="UnitCard"/> / <see cref="CommandCard"/> / <see cref="CounterCard"/> 之一
/// （<see cref="CardBase"/> 子类；名称取自定义；实例化路径装配按类别差异化：全类别＝指挥点花费、单位＝另加对战、反制＝另加激活状态）。
/// 读面：存在性 <see cref="Contains"/>（不抛错、回答有无）、取得 <see cref="Get"/>（未注册抛错）、枚举 <see cref="Definitions"/>。
/// 重复注册被拒绝（配置错误不吞；如需变更定义，重建库/对局）。
/// 2B 加性面：可选回合上下文提供器（<paramref name="turnPlayerProvider"/>）——实例化时注入每张卡
/// （反制「仅己方回合」验证所需；延迟读取、随对局回合推进取当前值）；独立构造（不提供）＝卡上为 null。
/// 2C 加性面：可选词条装载上下文提供器（<paramref name="keywordLoadContextProvider"/>）——实例化时注入每张卡
/// （加载时装载主动词条逻辑所需；延迟读取——对局装配顺序下提供器目标在实例化后才就绪）；独立构造（不提供）＝卡上为 null。
/// </summary>
public sealed class CardLibrary
{
    private readonly LogicEngine _engine;
    private readonly Dictionary<string, CardDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Func<Player?>? _turnPlayerProvider;
    private readonly Func<KeywordLoadContext?>? _keywordLoadContextProvider;

    /// <summary>创建卡牌库（实例化所需引擎引用由构造注入；可选回合上下文提供器——2B 加性；可选词条装载上下文提供器——2C 加性）。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public CardLibrary(
        LogicEngine engine,
        Func<Player?>? turnPlayerProvider = null,
        Func<KeywordLoadContext?>? keywordLoadContextProvider = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _turnPlayerProvider = turnPlayerProvider;
        _keywordLoadContextProvider = keywordLoadContextProvider;
    }

    /// <summary>注册定义（id 为注册键）。</summary>
    /// <exception cref="ArgumentException">id 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">definition 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一 id 重复注册（被拒绝）。</exception>
    public void Register(string id, CardDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(definition);

        if (!_definitions.TryAdd(id, definition))
        {
            throw new InvalidOperationException($"卡牌 id '{id}' 已注册（重复注册被拒绝）。");
        }
    }

    /// <summary>存在性查询：已注册＝true；未注册/ null/空白＝false（不抛错）。</summary>
    public bool Contains(string id) => !string.IsNullOrWhiteSpace(id) && _definitions.ContainsKey(id);

    /// <summary>取得定义。</summary>
    /// <exception cref="ArgumentException">id 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册的 id（明确错误、不吞）。</exception>
    public CardDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!_definitions.TryGetValue(id, out var definition))
        {
            throw new KeyNotFoundException($"卡牌 id '{id}' 未注册。");
        }

        return definition;
    }

    /// <summary>全部已注册定义（枚举读面；id → 定义）。</summary>
    public IReadOnlyDictionary<string, CardDefinition> Definitions => _definitions;

    /// <summary>
    /// 实例化：id → 卡牌实例（按定义类别产出三大类卡基类实例之一；名称取自定义、实例化路径装配按类别差异化；
    /// 实例化时注入回合上下文提供器〔2B〕与词条装载上下文提供器〔2C〕）。
    /// </summary>
    /// <exception cref="ArgumentException">id 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册的 id（明确错误、不吞）。</exception>
    public CardBase Instantiate(string id)
    {
        var definition = Get(id);
        CardBase card = definition.Category switch
        {
            CardCategory.Unit => new UnitCard(_engine, definition),
            CardCategory.Command => new CommandCard(_engine, definition),
            CardCategory.Counter => new CounterCard(_engine, definition),
            _ => throw new InvalidOperationException($"卡牌 '{id}' 的类别 '{definition.Category}' 未支持（实例化被拒绝）。"),
        };

        card.TurnPlayerProvider = _turnPlayerProvider;
        card.KeywordLoadContextProvider = _keywordLoadContextProvider;
        return card;
    }
}
