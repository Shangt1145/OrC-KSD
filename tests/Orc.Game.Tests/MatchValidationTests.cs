using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Collections;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// 创建参数校验、状态门禁与初始化失败模式：
/// 空名单/含空白 id/先手非法/上限非法 → 创建期拒绝；准备态推进与准备态访问管理器 → 抛错；
/// 重复 Initialize → 抛错；初始化异常传播且对局保持"准备"态；种子可读与先手可配置。
/// </summary>
public class MatchValidationTests
{
    [Fact]
    public void Empty_Deck_Is_Rejected_At_Creation()
    {
        var deck = GameTestData.CreateDeck();
        Assert.Throws<ArgumentException>(
            () => new Match(new CardList(), deck, GameTestData.CreateDefinitions()));
        Assert.Throws<ArgumentException>(
            () => new Match(deck, new CardList(), GameTestData.CreateDefinitions()));
    }

    [Fact]
    public void Null_Deck_Is_Rejected_At_Creation()
    {
        var deck = GameTestData.CreateDeck();
        var definitions = GameTestData.CreateDefinitions();
        Assert.Throws<ArgumentNullException>(() => new Match(null!, deck, definitions));
        Assert.Throws<ArgumentNullException>(() => new Match(deck, null!, definitions));
        Assert.Throws<ArgumentNullException>(() => new Match(deck, deck, null!));
    }

    [Fact]
    public void Blank_Id_In_Deck_Is_Rejected_At_Creation()
    {
        var badDeck = new CardList(new[] { "c01", "", "c02" }); // 空白 id（当前允许装入集合、创建期拒绝）
        var deck = GameTestData.CreateDeck();
        var definitions = GameTestData.CreateDefinitions();
        Assert.Throws<ArgumentException>(() => new Match(badDeck, deck, definitions));
        Assert.Throws<ArgumentException>(() => new Match(deck, badDeck, definitions));
    }

    [Fact]
    public void Invalid_First_Player_Index_Is_Rejected_At_Creation()
    {
        var deck = GameTestData.CreateDeck();
        var definitions = GameTestData.CreateDefinitions();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Match(deck, deck, definitions, firstPlayerIndex: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Match(deck, deck, definitions, firstPlayerIndex: 2));
    }

    [Fact]
    public void Invalid_Max_Point_Slots_Is_Rejected_At_Creation()
    {
        var deck = GameTestData.CreateDeck();
        var definitions = GameTestData.CreateDefinitions();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Match(deck, deck, definitions, options: new MatchOptions { MaxPointSlots = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Match(deck, deck, definitions, options: new MatchOptions { MaxPointSlots = -3 }));
    }

    [Fact]
    public async Task EndTurn_Before_Initialize_Throws()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await Assert.ThrowsAsync<InvalidOperationException>(() => match.EndTurn());
    }

    [Fact]
    public void Accessing_Managers_And_Forwarded_Properties_Before_Initialize_Throws()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);

        Assert.Throws<InvalidOperationException>(() => { _ = match.TurnManager; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.PlayerManager; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.BattlefieldManager; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.ResourceManager; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.CardLibrary; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.Players; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.CurrentPlayer; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.TurnNumber; });
        Assert.Throws<InvalidOperationException>(() => { _ = match.Battlefield; });
    }

    [Fact]
    public async Task Duplicate_Initialize_Throws()
    {
        var match = GameTestData.CreateStandardMatch(seed: 42);
        await match.Initialize();

        await Assert.ThrowsAsync<InvalidOperationException>(() => match.Initialize());
    }

    [Fact]
    public async Task Initialization_Failure_By_Insufficient_Opening_Cards_Keeps_Preparing_State()
    {
        // 不足起手（卡组 3 张 < 先手起手 4）：允许创建；初始化抽到空集抛错；对局保持"准备"态（不承诺回滚）
        var match = GameTestData.CreateStandardMatch(seed: 42, deckSize: 3);
        Assert.Equal(MatchState.Preparing, match.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => match.Initialize());

        Assert.Equal(MatchState.Preparing, match.State);
    }

    [Fact]
    public async Task Initialization_Failure_By_Unregistered_Id_Keeps_Preparing_State()
    {
        // 名单含未注册 id：不在创建期校验（初始化实例化时抛错——既有契约）
        var deckWithUnknown = new CardList(new[] { "c01", "c02", "c03", "zz9" }); // 4 张全数装载（先手 4）
        var deck = GameTestData.CreateDeck();
        var match = new Match(deckWithUnknown, deck, GameTestData.CreateDefinitions(), seed: 5);
        Assert.Equal(MatchState.Preparing, match.State);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => match.Initialize());

        Assert.Equal(MatchState.Preparing, match.State);
    }

    [Fact]
    public async Task Seed_Is_Readable_And_First_Player_Is_Configurable()
    {
        var match = GameTestData.CreateStandardMatch(seed: 555, firstPlayerIndex: 1);
        Assert.Equal(555, match.Seed); // 传入种子可读＝传入值

        await match.Initialize();

        // 先手＝指定玩家（玩家B）：先手 4、后手 5；先手先结算
        Assert.Same(match.Players[1], match.CurrentPlayer);
        Assert.Equal(4, match.Players[1].Hand.Count);
        Assert.Equal(5, match.Players[0].Hand.Count);
        Assert.Equal(1, match.Players[1].Points);
        Assert.Equal(0, match.Players[0].Points);
    }

    [Fact]
    public void Auto_Generated_Seed_Is_Readable()
    {
        var match = GameTestData.CreateStandardMatch(seed: null);
        Assert.True(match.Seed >= 0); // 未传种子：自动生成且事后可读（支撑复现）
    }

    [Fact]
    public void Empty_Definition_Set_Is_Allowed()
    {
        // 定义集为空 → 允许（空库；创建期不拦截）
        var deck = GameTestData.CreateDeck();
        var match = new Match(deck, GameTestData.CreateDeck(), Array.Empty<CardDefinitionEntry>(), seed: 1);
        Assert.Equal(MatchState.Preparing, match.State);
    }
}
