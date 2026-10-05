using Orc.Core;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Players;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// K0·B12 烧牌统一——两路语义并排对照回归（统一改造的专属锚）：
/// 同一满手条件下分别走两路（DrawCard 路径 / PlaceToHand 路径），显式断言：
/// ① 差异面＝仅 card.drawn（DrawCard 路径「算被抽到」恰一次；PlaceToHand 路径「未经手牌」零次）；
/// ② 共同面＝销毁（含 card.destroyed）先于 card.discarded、card.discarded 恰一次、card.hand.add 零次、
///    手牌全程保持 HandLimit（无瞬时超额）、被烧卡终态（已销毁、离手、离场/离引擎登记）。
/// 另：未满边界对照——两路在「上限−1」时均按正常路径处置（无销毁/弃置副作用）。
/// </summary>
public class HandLimitBurnUnificationTests
{
    /// <summary>手牌填充至指定张数（经既有「实例化＋加载＋放入手牌」测试面）。</summary>
    private static async Task FillHandToAsync(Match match, Player player, int count)
    {
        while (player.Hand.Count < count)
        {
            await CommandTestKit.InstantiateLoadedAsync(match, player, CommandTestKit.InfantryId, toHand: true);
        }
    }

    [Fact]
    public async Task Two_Burn_Paths_At_Full_Hand_Parallel_Contrast_Only_Drawn_Differs()
    {
        var match = S9Kit.CreateSceneMatch();
        await match.Initialize();
        var playerA = match.Players[0];
        await FillHandToAsync(match, playerA, Player.HandLimit);
        Assert.Equal(Player.HandLimit, playerA.Hand.Count);

        using var recorder = new UpdateRecorder(match.Engine);
        recorder.Clear();

        // 全程观测：drawn 信号时点读手牌数（证明「无瞬时第 10 张」——手牌全程保持上限）
        var handCountsAtDraw = new List<int>();
        using var probe = match.Engine.Subscribe((type, payload, _) =>
        {
            if (type == GameUpdates.CardDrawn && payload is not null
                && ReferenceEquals(payload[GameUpdates.PayloadPlayer], playerA))
            {
                handCountsAtDraw.Add(playerA.Hand.Count);
            }

            return Task.CompletedTask;
        });

        // ══ 段 ①：DrawCard 路径（「算被抽到」）——drawn → 销毁 → discarded ══
        var drawn = await match.PlayerManager.DrawCard(playerA);

        var drawPathSignals = recorder.Types.ToList();
        Assert.Equal(Player.HandLimit, playerA.Hand.Count);
        Assert.Equal(Player.HandLimit, Assert.Single(handCountsAtDraw)); // drawn 恰一次观察点、值＝上限
        Assert.Equal(1, drawPathSignals.Count(t => t == GameUpdates.CardDrawn));
        Assert.Equal(1, drawPathSignals.Count(t => t == Updates.CardDestroyed));
        Assert.Equal(1, drawPathSignals.Count(t => t == GameUpdates.CardDiscarded));
        Assert.Equal(0, drawPathSignals.Count(t => t == GameUpdates.CardHandAdd));
        var drawPathDiscarded = Assert.Single(recorder.Updates, u => u.Type == GameUpdates.CardDiscarded);
        Assert.Same(drawn, drawPathDiscarded.Payload![GameUpdates.PayloadCard]);
        Assert.Same(playerA, drawPathDiscarded.Payload![GameUpdates.PayloadPlayer]);
        // 被烧卡终态：已销毁、离手、离卡组、离引擎登记
        Assert.False(drawn.Life.IsAlive);
        Assert.DoesNotContain(drawn, playerA.Hand);
        Assert.False(playerA.Deck.ContainsInstance(drawn));
        Assert.DoesNotContain(drawn, match.Engine.Cards);

        // ══ 段 ②：PlaceToHand 路径（「未经手牌」）——销毁 → discarded（不发 drawn） ══
        recorder.Clear();
        var placed = await match.CardService.CreateAndPlaceToHandAsync(S9Kit.LightInfantryId, playerA);

        var placePathSignals = recorder.Types.ToList();
        Assert.Equal(CardPlaceStatus.Burned, placed.Status);
        Assert.True(placed.IsSuccess);
        Assert.Equal(Player.HandLimit, playerA.Hand.Count);
        var burned = Assert.IsType<UnitCard>(placed.Card);
        Assert.Equal(0, placePathSignals.Count(t => t == GameUpdates.CardDrawn));
        Assert.Equal(1, placePathSignals.Count(t => t == Updates.CardDestroyed));
        Assert.Equal(1, placePathSignals.Count(t => t == GameUpdates.CardDiscarded));
        Assert.Equal(0, placePathSignals.Count(t => t == GameUpdates.CardHandAdd));
        var placePathDiscarded = Assert.Single(recorder.Updates, u => u.Type == GameUpdates.CardDiscarded);
        Assert.Same(burned, placePathDiscarded.Payload![GameUpdates.PayloadCard]);
        Assert.Same(playerA, placePathDiscarded.Payload![GameUpdates.PayloadPlayer]);
        Assert.False(burned.Life.IsAlive);
        Assert.DoesNotContain(burned, playerA.Hand);
        Assert.DoesNotContain(burned, match.Engine.Cards);

        // ══ 并排对照：关键信号序列——差异面＝仅 card.drawn；其余（销毁先于 discarded、hand.add 零次）一致 ══
        static List<string> KeyBurnSignals(IReadOnlyList<string> types) => types
            .Where(t => t == GameUpdates.CardDrawn
                || t == Updates.CardDestroyed
                || t == GameUpdates.CardDiscarded
                || t == GameUpdates.CardHandAdd)
            .ToList();

        Assert.Equal(
            new[] { GameUpdates.CardDrawn, Updates.CardDestroyed, GameUpdates.CardDiscarded },
            KeyBurnSignals(drawPathSignals)); // ① 算被抽到：drawn 在销毁前
        Assert.Equal(
            new[] { Updates.CardDestroyed, GameUpdates.CardDiscarded },
            KeyBurnSignals(placePathSignals)); // ② 未经手牌：无 drawn、销毁先于 discarded
    }

    [Fact]
    public async Task Under_Limit_Both_Paths_Place_Normally_Without_Burn_Side_Effects()
    {
        var match = S9Kit.CreateSceneMatch();
        await match.Initialize();
        var playerA = match.Players[0];
        var playerB = match.Players[1];
        await FillHandToAsync(match, playerA, Player.HandLimit - 1);
        await FillHandToAsync(match, playerB, Player.HandLimit - 1);

        using var recorder = new UpdateRecorder(match.Engine);

        // 段 ①：DrawCard@「上限−1」＝正常入手（drawn → hand.add；零销毁/弃置）
        recorder.Clear();
        var drawn = await match.PlayerManager.DrawCard(playerA);
        Assert.Equal(Player.HandLimit, playerA.Hand.Count);
        Assert.Contains(drawn, playerA.Hand);
        Assert.True(drawn.Life.IsAlive);
        Assert.Equal(new[] { GameUpdates.CardDrawn, GameUpdates.CardHandAdd }, recorder.Types);

        // 段 ②：PlaceToHand@「上限−1」＝正常放置（hand.add 恰一次；drawn/销毁/弃置零次）
        recorder.Clear();
        var placed = await match.CardService.CreateAndPlaceToHandAsync(S9Kit.LightInfantryId, playerB);
        Assert.Equal(CardPlaceStatus.Placed, placed.Status);
        Assert.Equal(Player.HandLimit, playerB.Hand.Count);
        var placedCard = Assert.IsType<UnitCard>(placed.Card);
        Assert.Contains(placedCard, playerB.Hand);
        Assert.True(placedCard.Life.IsAlive);
        Assert.Equal(0, recorder.CountOf(GameUpdates.CardDrawn));
        Assert.Equal(1, recorder.CountOf(GameUpdates.CardHandAdd));
        Assert.Equal(0, recorder.CountOf(Updates.CardDestroyed));
        Assert.Equal(0, recorder.CountOf(GameUpdates.CardDiscarded));
        var handAdd = recorder.PayloadOf(GameUpdates.CardHandAdd)!;
        Assert.Same(placedCard, handAdd[GameUpdates.PayloadCard]);
        Assert.Same(playerB, handAdd[GameUpdates.PayloadPlayer]);
    }
}
