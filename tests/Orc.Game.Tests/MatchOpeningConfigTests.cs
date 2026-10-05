using Orc.Game;
using Orc.Game.Managers;
using Xunit;

namespace Orc.Game.Tests;

/// <summary>
/// K4·B13 开局常量配置化（起手张数／首回合抽牌）游戏层测试：
/// ①缺省断言——缺省构造＝4/5/false（属性级）＋行为零变化（流程级：真实起手装载 4/5、第 1 回合不抽、后手首回合照抽）；
/// ②显式配置生效断言（流程级）——真实起手装载张数＝显式配置值；AllowFirstTurnDraw＝true → 全局第 1 回合真实抽牌发生；
/// ③值域边界——0 合法（空起手正常流程）；负数于创建期拒绝（fail-fast）；
/// ④mulligan 交互——显式起手配置不改变 SkipMulligan 序列（换牌相位保持、起手产物供 mulligan 使用）。
/// 构造口径：直接经 <see cref="Match"/> 创建参数（完整 MatchOptions 入口）——不经 GameTestData.CreateStandardMatch
/// （其 options 透传仅限既有字段；不经由此调整）；断言一律经流程消费点（真实起手装载／真实回合抽牌）。
/// </summary>
public class MatchOpeningConfigTests
{
    // ---------- 辅助 ----------

    /// <summary>标准对局构造（完整 MatchOptions 入口；可选配置为 null＝不传 options 的全缺省路径）。</summary>
    private static Match CreateMatch(MatchOptions? options = null)
        => new(
            GameTestData.CreateDeck(),
            GameTestData.CreateDeck(),
            GameTestData.CreateDefinitions(),
            seed: 42,
            firstPlayerIndex: null,
            options: options);

    // ---------- ① 缺省断言（属性级） ----------

    [Fact]
    public void Defaults_Are_4_5_And_Disallow_First_Turn_Draw()
    {
        var options = new MatchOptions();

        Assert.Equal(4, options.OpeningHandSizeFirstPlayer);
        Assert.Equal(5, options.OpeningHandSizeSecondPlayer);
        Assert.False(options.AllowFirstTurnDraw);
        // 缺省值常量（配置面公开读口）：
        Assert.Equal(4, MatchOptions.DefaultOpeningHandSizeFirstPlayer);
        Assert.Equal(5, MatchOptions.DefaultOpeningHandSizeSecondPlayer);
    }

    // ---------- ① 缺省断言（流程级：不传 options → 换牌序列 → 回合 1 不抽；边界：回合 2 照抽） ----------

    [Fact]
    public async Task Defaults_Load_4_5_And_Skip_First_Turn_Draw()
    {
        var match = CreateMatch(); // 不传 options＝全缺省（SkipMulligan=false 的换牌序列）
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();

        // 换牌相位（缺省序列）；起手装载已静默发生：4/5
        Assert.Equal(MatchState.Mulligan, match.State);
        Assert.Equal(4, match.Players[0].Hand.Count);
        Assert.Equal(5, match.Players[1].Hand.Count);

        // 双方确认 → 执行先手第 1 回合：第 1 回合不抽（缺省）——先手仍 4、无 card.drawn
        Assert.Equal(MulliganResultStatus.Success, (await match.MulliganDone(match.Players[0])).Status);
        Assert.Equal(MulliganResultStatus.Success, (await match.MulliganDone(match.Players[1])).Status);
        Assert.Equal(MatchState.InProgress, match.State);
        Assert.Equal(1, match.TurnNumber);
        Assert.Equal(4, match.Players[0].Hand.Count);
        Assert.DoesNotContain(GameUpdates.CardDrawn, recorder.Types);

        // 后手首回合（全局回合 2）照抽 1——既有边界保持
        await match.EndTurn();
        Assert.Equal(6, match.Players[1].Hand.Count);
    }

    // ---------- ② 显式配置生效断言（流程级：真实起手装载张数＝显式配置值） ----------

    [Fact]
    public async Task Explicit_Opening_Hand_Sizes_Drive_Real_Load()
    {
        var match = CreateMatch(new MatchOptions
        {
            SkipMulligan = true,
            OpeningHandSizeFirstPlayer = 3,
            OpeningHandSizeSecondPlayer = 6,
        });
        await match.Initialize();

        // 真实起手装载张数＝显式配置值（流程消费点证据；卡组余量同步）
        Assert.Equal(3, match.Players[0].Hand.Count);
        Assert.Equal(6, match.Players[1].Hand.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 3, match.Players[0].Deck.Count);
        Assert.Equal(GameTestData.StandardDeckSize - 6, match.Players[1].Deck.Count);
    }

    // ---------- ② 显式配置生效断言（流程级：AllowFirstTurnDraw=true → 全局第 1 回合真实抽牌发生） ----------

    [Fact]
    public async Task Allow_First_Turn_Draw_Makes_Global_Turn_One_Draw_Happen()
    {
        var match = CreateMatch(new MatchOptions
        {
            SkipMulligan = true,
            AllowFirstTurnDraw = true,
        });
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();

        // 显式「允许」→ 全局第 1 回合真实抽牌发生：先手 4＋1、card.drawn 恰一次（载荷＝先手）
        Assert.Equal(5, match.Players[0].Hand.Count);
        Assert.Equal(1, recorder.Types.Count(t => t == GameUpdates.CardDrawn));
        var drawn = recorder.Updates.Single(u => u.Type == GameUpdates.CardDrawn);
        Assert.Same(match.Players[0], drawn.Payload![GameUpdates.PayloadPlayer]);

        // 后手首回合（全局回合 2）照抽的既有边界不受开关影响
        await match.EndTurn();
        Assert.Equal(6, match.Players[1].Hand.Count);
    }

    // ---------- ③ 值域边界：负数创建期拒绝（fail-fast） ----------

    [Fact]
    public void Negative_Opening_Hand_Sizes_Are_Rejected_At_Creation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMatch(new MatchOptions { OpeningHandSizeFirstPlayer = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateMatch(new MatchOptions { OpeningHandSizeSecondPlayer = -2 }));
    }

    // ---------- ③ 值域边界：0 合法（空起手正常流程） ----------

    [Fact]
    public async Task Zero_Opening_Hand_Is_Valid_Empty_Hand_Normal_Flow()
    {
        var match = CreateMatch(new MatchOptions
        {
            SkipMulligan = true,
            OpeningHandSizeFirstPlayer = 0,
            OpeningHandSizeSecondPlayer = 0,
        });
        using var recorder = new UpdateRecorder(match.Engine);
        await match.Initialize();

        // 空起手（0 合法）：双方手牌 0；回合 1 正常执行（先手、不抽）
        Assert.Equal(0, match.Players[0].Hand.Count);
        Assert.Equal(0, match.Players[1].Hand.Count);
        Assert.Equal(1, match.TurnNumber);
        Assert.Same(match.Players[0], match.CurrentPlayer);
        Assert.DoesNotContain(GameUpdates.CardDrawn, recorder.Types);

        // 正常流程继续：回合 2 后手照抽 1（0＋1＝1）
        await match.EndTurn();
        Assert.Equal(1, match.Players[1].Hand.Count);
    }

    // ---------- ④ mulligan 交互：显式起手配置不改变 SkipMulligan 序列 ----------

    [Fact]
    public async Task Explicit_Opening_Config_Keeps_Mulligan_Sequence()
    {
        var match = CreateMatch(new MatchOptions
        {
            OpeningHandSizeFirstPlayer = 3,
            OpeningHandSizeSecondPlayer = 6,
        });
        await match.Initialize();

        // SkipMulligan 缺省 false 的换牌序列不受新配置影响；起手产物（配置值）供 mulligan 使用
        Assert.Equal(MatchState.Mulligan, match.State);
        Assert.Equal(MatchPhase.Mulligan, match.Phase);
        Assert.False(match.MulliganManager.AllConfirmed);
        Assert.Equal(3, match.Players[0].Hand.Count);
        Assert.Equal(6, match.Players[1].Hand.Count);
    }
}
