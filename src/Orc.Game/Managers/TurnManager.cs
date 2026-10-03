using Orc.Core;
using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 回合管理器（回合领域真源：回合数、当前行动方）：回合推进编排（"信号先行、处理随后"模型）。
/// 回合开始序列＝turn.start.before → turn.start →（开始处理：结算 → 抽牌）→ turn.start.after；
/// 结算＝资源管理器（槽 +1 至上限 → 点数＝槽值；静默）、抽牌＝玩家管理器（先手第 1 回合不抽、其余照抽 1；抽时所发 card.drawn 位于 start 与 after 之间）；
/// 回合结束序列＝turn.end.before → turn.end →（结束处理：结束方点数清零；静默）→ 切换当前方、回合数 +1。
/// 所有 turn 系列更新经总线 Emit（载荷＝{ 玩家, 回合数 }）；全部顺序 await 完结（调用返回即结算与更新完结）。
/// </summary>
public sealed class TurnManager
{
    private readonly LogicEngine _engine;
    private readonly PlayerManager _playerManager;
    private readonly ResourceManager _resourceManager;
    private Player? _currentPlayer;

    internal TurnManager(LogicEngine engine, PlayerManager playerManager, ResourceManager resourceManager)
    {
        _engine = engine;
        _playerManager = playerManager;
        _resourceManager = resourceManager;
    }

    /// <summary>回合数（全局递增；回合 1＝先手首回合）。</summary>
    public int TurnNumber { get; private set; }

    /// <summary>当前行动方（尚未发起首个回合时访问＝抛错）。</summary>
    /// <exception cref="InvalidOperationException">回合尚未开始（当前行动方未定）。</exception>
    public Player CurrentPlayer => _currentPlayer ?? throw new InvalidOperationException("回合尚未开始（当前行动方未定）。");

    /// <summary>
    /// 初始化动作：置回合数＝1、当前行动方＝先手；发起先手回合开始序列（本轮不抽牌——先手第 1 回合＝唯一抽牌例外）。只允许一次。
    /// </summary>
    /// <exception cref="ArgumentNullException">firstPlayer 为 null。</exception>
    /// <exception cref="InvalidOperationException">先手回合已发起（重复发起被拒绝）。</exception>
    internal async Task StartFirstTurn(Player firstPlayer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(firstPlayer);
        if (_currentPlayer is not null)
        {
            throw new InvalidOperationException("先手回合已发起（重复发起被拒绝）。");
        }

        TurnNumber = 1;
        _currentPlayer = firstPlayer;
        await BeginTurnAsync(ct);
    }

    /// <summary>
    /// 结束当前方回合：turn.end.before → turn.end →（结束方点数清零）→ 切换当前方、回合数 +1 → 回合开始序列（双人对局轮流）。
    /// </summary>
    /// <exception cref="InvalidOperationException">回合尚未开始，不能结束回合。</exception>
    public async Task EndTurn(CancellationToken ct = default)
    {
        if (_currentPlayer is null)
        {
            throw new InvalidOperationException("回合尚未开始，不能结束回合。");
        }

        var endingPlayer = _currentPlayer;
        await EmitTurnAsync(GameUpdates.TurnEndBefore, endingPlayer, TurnNumber, ct);
        await EmitTurnAsync(GameUpdates.TurnEnd, endingPlayer, TurnNumber, ct);

        // 回合结束处理（静默）：结束方点数清零（槽保留）
        _resourceManager.ClearPoints(endingPlayer);

        // 切换当前方、回合数 +1（静默）
        _currentPlayer = _playerManager.Players[(endingPlayer.Index + 1) % 2];
        TurnNumber += 1;

        await BeginTurnAsync(ct);
    }

    /// <summary>回合开始序列：before → start →（开始处理：结算 → 抽牌）→ after（全部顺序 await 完结）。</summary>
    private async Task BeginTurnAsync(CancellationToken ct)
    {
        var current = CurrentPlayer;
        await EmitTurnAsync(GameUpdates.TurnStartBefore, current, TurnNumber, ct);
        await EmitTurnAsync(GameUpdates.TurnStart, current, TurnNumber, ct);

        // 回合开始处理（静默）：资源结算（槽 +1 至上限 → 点数＝槽值）→ 抽牌（需抽时发 card.drawn）
        _resourceManager.Settle(current);
        if (ShouldDrawForTurn(TurnNumber))
        {
            await _playerManager.DrawCard(current, ct);
        }

        await EmitTurnAsync(GameUpdates.TurnStartAfter, current, TurnNumber, ct);
    }

    /// <summary>抽牌规则：先手第 1 回合（全局回合 1）不抽；其余回合（含后手首回合与自回合 3 起双方各回合）照抽 1。</summary>
    private static bool ShouldDrawForTurn(int turnNumber) => turnNumber != 1;

    /// <summary>发射 turn 系列更新（载荷＝{ 玩家, 回合数 }）。</summary>
    private Task EmitTurnAsync(string updateType, Player player, int turnNumber, CancellationToken ct)
        => _engine.Emit(
            updateType,
            new Dictionary<string, object?>
            {
                [GameUpdates.PayloadPlayer] = player,
                [GameUpdates.PayloadTurnNumber] = turnNumber,
            },
            ct);
}
