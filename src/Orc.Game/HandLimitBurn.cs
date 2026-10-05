using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Players;

namespace Orc.Game;

/// <summary>
/// HandLimit 爆牌共享单元（K0·B12「判定器收编（A+B）」——爆牌统一；Kb 爆牌独立化与术语更名）：
/// 满手判定（<see cref="IsAtLimit"/>）＋爆牌动作（<see cref="BurnAsync"/>——emitDrawn 参数化语义）的跨类唯一承载——
/// 消除 DrawCard 满手路径与 PlaceToHandAsync 满手路径的双重实现；爆牌输出＝独立信号 <c>card.burned</c>
/// （不复用 card.discarded——爆牌不走弃牌路线）。弃置路径专用原语 <see cref="DestroyAndEmitDiscardedAsync"/>
/// （销毁＋card.discarded）亦在本单元承载（弃置路径 DiscardCardAsync 经此）。
/// 两路语义（显式参数化保留；Kb 起爆牌输出＝独立信号 card.burned〔信号替换为有意变更〕；其余逐信号不变）：
/// ①「算被抽到」（emitDrawn: true——DrawCard 路径）：发 card.drawn 恰一次 → 销毁〔引擎既有机制〕→ 发 card.burned 恰一次；
/// ②「未经手牌」（emitDrawn: false——PlaceToHand 路径）：销毁 → 发 card.burned 恰一次（不发 card.drawn / card.hand.add）。
/// 两路均不经手牌（直爆口径——手牌全程保持上限）、card.hand.add 零次、不经弃置动作。
/// 共享关系（Kb 更新——替代 K0「共享处置链/共享信号」表述）：弃置＝销毁＋card.discarded；爆牌＝销毁＋card.burned；
/// 两者仅共享「销毁」步骤（引擎既有销毁、card.destroyed 保留）；信号发射各自独立（不复用、不互发）。
/// 起手装载（LoadOpeningHand）不接入本单元（不受裁决——静默语义不变）；
/// HandLimit＝<see cref="Player.HandLimit"/>〔9〕常量值与判定用法保持现状（不做配置面改造）。
/// </summary>
public static class HandLimitBurn
{
    /// <summary>满手判定（手牌计数 ≥ <see cref="Player.HandLimit"/>〔9〕——爆牌裁决前置；纯读、无副作用）。</summary>
    /// <exception cref="ArgumentNullException">player 为 null。</exception>
    public static bool IsAtLimit(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Hand.Count >= Player.HandLimit;
    }

    /// <summary>
    /// 销毁＋发射 card.discarded（弃置路径专用〈Kb 解耦后〉——处置链：销毁〔含资源清理〕先、信号后；恰一次）。
    /// 弃置路径（<see cref="Managers.PlayerManager.DiscardCardAsync"/>）经本原语；爆牌路径已解耦
    /// （改经 <see cref="BurnAsync"/> 的独立 card.burned 输出——不复用本信号）。
    /// 载荷＝{ Card, Player }（被弃卡实例在前、玩家在后）。
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
    /// 爆牌（满手裁决动作——emitDrawn 参数化语义；输出＝独立信号 card.burned——不复用 card.discarded）：
    /// emitDrawn＝true＝「算被抽到」（DrawCard 路径）：card.drawn 恰一次 → 销毁 → card.burned 恰一次；
    /// emitDrawn＝false＝「未经手牌」（PlaceToHand 路径）：销毁 → card.burned 恰一次（drawn 零次）。
    /// 两路均不走手牌（直爆）、不经弃置动作；card.hand.add 零次。
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

        await engine.DestroyCard(card); // 与弃置路径仅共享「销毁」步骤（引擎既有销毁；card.destroyed 保留）
        await GameUpdates.EmitCardBurned(engine, card, player, ct); // 独立信号——不复用 card.discarded
    }
}
