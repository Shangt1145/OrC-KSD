using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Orc.Game.Targeting;

namespace Orc.Game.Tests;

/// <summary>测试数据与工具（夹具）：标准定义集/卡组构造、对局构造。</summary>
internal static class GameTestData
{
    /// <summary>标准卡组规模（20 张；大于起手 4/5，足以支撑多回合推进）。</summary>
    public const int StandardDeckSize = 20;

    /// <summary>
    /// 标准定义集：id "c01".."cNN"，名称 "卡01".."卡NN"（名称与 id 一一对应且全局唯一，便于逐位断言）；
    /// 四数值对每个 i 各异（部署费＝i、行动费＝i+1、攻击＝i+2、防御＝i+3），便于装配断言。
    /// </summary>
    public static IReadOnlyList<CardDefinitionEntry> CreateDefinitions(int count = StandardDeckSize)
        => Enumerable.Range(1, count)
            .Select(i => new CardDefinitionEntry(
                $"c{i:D2}",
                new CardDefinition($"卡{i:D2}", deployCost: i, operateCost: i + 1, attack: i + 2, defense: i + 3)))
            .ToList();

    /// <summary>标准卡组名单：id "c01".."cNN"（与标准定义集对应）。</summary>
    public static CardList CreateDeck(int count = StandardDeckSize)
        => new(Enumerable.Range(1, count).Select(i => $"c{i:D2}"));

    /// <summary>标准对局：双方标准卡组＋标准定义集；默认种子 42、默认先手＝玩家A；可选目标选择桥接（第六员装配输入）。</summary>
    public static Match CreateStandardMatch(
        int? seed = 42,
        int? firstPlayerIndex = null,
        MatchOptions? options = null,
        int deckSize = StandardDeckSize,
        ITargeterBridge? targeterBridge = null)
        => new(CreateDeck(deckSize), CreateDeck(deckSize), CreateDefinitions(deckSize), seed, firstPlayerIndex, options, targeterBridge);
}

/// <summary>更新记录器：经 <see cref="LogicEngine.Subscribe"/> 挂接（测试订阅渠道），记录更新类型与载荷（注册序）。</summary>
internal sealed class UpdateRecorder : IDisposable
{
    private readonly IDisposable _subscription;
    private readonly List<(string Type, IReadOnlyDictionary<string, object?>? Payload)> _updates = new();

    public UpdateRecorder(LogicEngine engine)
    {
        _subscription = engine.Subscribe((type, payload, ct) =>
        {
            _updates.Add((type, payload));
            return Task.CompletedTask;
        });
    }

    /// <summary>已记录的更新（类型＋载荷；记录序）。</summary>
    public IReadOnlyList<(string Type, IReadOnlyDictionary<string, object?>? Payload)> Updates => _updates;

    /// <summary>已记录的更新类型序列（记录序）。</summary>
    public IReadOnlyList<string> Types => _updates.Select(u => u.Type).ToList();

    /// <summary>清空记录（用于分段断言）。</summary>
    public void Clear() => _updates.Clear();

    public void Dispose() => _subscription.Dispose();
}
