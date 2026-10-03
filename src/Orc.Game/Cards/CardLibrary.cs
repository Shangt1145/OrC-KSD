using Orc.Cards;
using Orc.Core;

namespace Orc.Game.Cards;

/// <summary>
/// 卡牌库：id → 卡牌定义的注册表（代码注册 API；本批无 JSON 载入）；
/// 负责名单实例化时创建卡牌实例（引擎 <see cref="Card"/> ＋ <see cref="CardStatsData"/> 基础数据组件，名称取自定义、四项数值取初始值）。
/// 读面：存在性 <see cref="Contains"/>（不抛错、回答有无）、取得 <see cref="Get"/>（未注册抛错）、枚举 <see cref="Definitions"/>。
/// 重复注册被拒绝（配置错误不吞；如需变更定义，重建库/对局）。
/// </summary>
public sealed class CardLibrary
{
    private readonly LogicEngine _engine;
    private readonly Dictionary<string, CardDefinition> _definitions = new(StringComparer.Ordinal);

    /// <summary>创建卡牌库（实例化所需引擎引用由构造注入）。</summary>
    /// <exception cref="ArgumentNullException">engine 为 null。</exception>
    public CardLibrary(LogicEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
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
    /// 实例化：id → 卡牌实例（引擎 Card ＋ 基础数据组件装配；名称取自定义、四项数值取定义初始值）。
    /// </summary>
    /// <exception cref="ArgumentException">id 为 null/空白。</exception>
    /// <exception cref="KeyNotFoundException">未注册的 id（明确错误、不吞）。</exception>
    public Card Instantiate(string id)
    {
        var definition = Get(id);
        var card = new Card(_engine, definition.Name);
        card.AddData(new CardStatsData
        {
            DeployCost = definition.DeployCost,
            OperateCost = definition.OperateCost,
            Attack = definition.Attack,
            Defense = definition.Defense,
        });

        return card;
    }
}
