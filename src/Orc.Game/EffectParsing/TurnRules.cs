using Orc.Cards;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game.EffectParsing;

/// <summary>
/// 回合归属类条件求值面（S3；csx 渲染的运行时挂钩）：判定「该卡拥有者视角的回合归属」——
/// owner == 当前行动方（<see cref="IsOwnerTurn"/>）／owner ≠ 当前行动方（<see cref="IsOpponentTurn"/>）。
/// <para>求值读既有回合归属判定面：当前行动方经「回合历史」推导——对局历史读取服务
/// （<see cref="MatchHistoryService"/>，S10 交付的既有公共读取面）中最近一次 <c>turn.start</c>／
/// <c>turn.end</c> 的**计数关系**判定：存在未闭合回合（starts == ends + 1——回合交替链的唯二状态）时，
/// 最后一条 <c>turn.start</c> 的载荷玩家即当前行动方；回合未开始／回合间窗口（无正式行动方）＝false
/// （保守——"若是友方回合"不成立）。</para>
/// <para>降级：非卡／无归属／无历史服务（脱局）＝false、不抛错（对齐既有查询面惯例）。</para>
/// </summary>
public static class TurnRules
{
    /// <summary>「若是友方回合」：该卡拥有者＝当前行动方。</summary>
    /// <exception cref="ArgumentNullException">无（null＝false 降级）。</exception>
    public static bool IsOwnerTurn(Card? card)
    {
        if (card is not CardBase { Owner: { } owner })
        {
            return false;
        }

        return ReferenceEquals(CurrentPlayerOf(card), owner);
    }

    /// <summary>「若是敌方回合」：该卡拥有者≠当前行动方（两者均有效时；否则 false 降级）。</summary>
    /// <exception cref="ArgumentNullException">无（null＝false 降级）。</exception>
    public static bool IsOpponentTurn(Card? card)
    {
        if (card is not CardBase { Owner: { } owner })
        {
            return false;
        }

        var currentPlayer = CurrentPlayerOf(card);
        return currentPlayer is not null && !ReferenceEquals(currentPlayer, owner);
    }

    /// <summary>当前行动方（经既有回合事件流推导；不可达／回合未开始／回合间＝null）。</summary>
    private static Player? CurrentPlayerOf(Card card)
    {
        var history = MatchHistoryService.ResolveFor(card);
        if (history is null)
        {
            return null;
        }

        var starts = history.GetEntries(GameUpdates.TurnStart);
        if (starts.Count == 0)
        {
            return null; // 回合尚未开始（当前行动方未定）
        }

        var ends = history.GetEntries(GameUpdates.TurnEnd);
        if (starts.Count != ends.Count + 1)
        {
            return null; // 无未闭合回合（回合间窗口——无正式行动方）
        }

        var lastStart = starts[^1];
        return lastStart.Data.TryGetValue(GameUpdates.PayloadPlayer, out var value) && value is Player player
            ? player
            : null;
    }
}
