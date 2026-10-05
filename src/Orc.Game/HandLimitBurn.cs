using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// HandLimit 烧牌共享单元（K0·B12「判定器收编（A+B）」——烧牌统一）：
/// 满手判定（<see cref="IsAtLimit"/>）＋烧牌动作（<see cref="BurnAsync"/>——emitDrawn 参数化语义）＋
/// 「销毁＋card.discarded」单一实现（<see cref="DestroyAndEmitDiscardedAsync"/>）的跨类唯一承载——
/// 消除 DrawCard 满手路径与 PlaceToHandAsync 满手路径的双重实现；弃置路径（DiscardCardAsync）经同一销毁原语复用。
/// 两路语义（显式参数化保留；与统一前逐信号零变化）：
/// ①「算被抽到」（emitDrawn: true——DrawCard 路径）：发 card.drawn 恰一次 → 销毁〔引擎既有机制〕→ 发 card.discarded 恰一次；
/// ②「未经手牌」（emitDrawn: false——PlaceToHand 路径）：销毁 → 发 card.discarded 恰一次（不发 card.drawn / card.hand.add）。
/// 两路均不经手牌（直烧口径——手牌全程保持上限）、card.hand.add 零次、不经弃置动作（共享销毁原语与信号）。
/// 起手装载（LoadOpeningHand）不接入本单元（不受裁决——静默语义不变）；
/// HandLimit＝<see cref="Player.HandLimit"/>〔9〕常量值与判定用法保持现状（不做配置面改造）。
/// </summary>
public static class HandLimitBurn
{
    /// <summary>满手判定（手牌计数 ≥ <see cref="Player.HandLimit"/>〔9〕——烧牌裁决前置；纯读、无副作用）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public static bool IsAtLimit(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Hand.Count >= Player.HandLimit;
    }

    /// <summary>
    /// 销毁＋发射 card.discarded（全库单一实现——弃置动作与烧牌共享的处置链：销毁〔含资源清理〕先、信号后；
    /// 恰一次）。弃置路径（<see cref="Managers.PlayerManager.DiscardCardAsync"/>）与烧牌路径（<see cref="BurnAsync"/>）
    /// 均经本原语；载荷＝{ Card, Player }（被弃卡实例在前、玩家在后）。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine / player / card 为 null。</exception>
    public static async Task DestroyAndEmitDiscardedAsync(
        LogicEngine engine, Player player, CardBase card, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);

        await engine.DestroyCard(card);
        await GameUpdates.EmitCardDiscarded(engine, card, player, ct);
    }

    /// <summary>
    /// 烧牌（满手裁决动作——emitDrawn 参数化语义）：
    /// emitDrawn＝true＝「算被抽到」（DrawCard 路径）：card.drawn 恰一次 → 销毁 → card.discarded 恰一次；
    /// emitDrawn＝false＝「未经手牌」（PlaceToHand 路径）：销毁 → card.discarded 恰一次（drawn 零次）。
    /// 两路均不走手牌（直烧）、不经弃置动作；card.hand.add 零次。
    /// </summary>
    /// <exception cref="ArgumentNullException">engine / player / card 为 null。</exception>
    public static async Task BurnAsync(
        LogicEngine engine, Player player, CardBase card, bool emitDrawn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(card);

        if (emitDrawn)
        {
            await GameUpdates.EmitCardDrawn(engine, player, card, ct);
        }

        await DestroyAndEmitDiscardedAsync(engine, player, card, ct);
    }
}
