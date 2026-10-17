namespace Orc.Mcp.Match;

/// <summary>
/// 座位装配输入：一个座位对应一份**显式卡组**（由前端生成玩家/AI 角色时给出；不做自动构筑）。
/// </summary>
/// <param name="Seat">座位号（0＝玩家A、1＝玩家B）。</param>
/// <param name="Deck">卡组名单（卡 id 序列；可含包内原 id 或已前缀化 id）。</param>
/// <param name="Name">展示名（缺省＝按座位号生成）。</param>
public sealed record MatchSeatConfig(int Seat, IReadOnlyList<string> Deck, string? Name = null);

/// <summary>
/// 真实对局创建输入（进程内；非沙盒）：卡包目录 ＋ 两个座位装配输入。
/// </summary>
public sealed class MatchLaunchOptions
{
    /// <summary>卡包目录列表（按顺序装载；含 <c>manifest.json</c> 走包链，否则走目录链）。</summary>
    public required IReadOnlyList<string> PackDirectories { get; init; }

    /// <summary>0 号座位。</summary>
    public required MatchSeatConfig SeatA { get; init; }

    /// <summary>1 号座位。</summary>
    public required MatchSeatConfig SeatB { get; init; }

    /// <summary>随机种子（缺省＝引擎自动生成；指定以复现）。</summary>
    public int? Seed { get; init; }

    /// <summary>跳过换牌相位（缺省 false＝真实对局须经换牌）。</summary>
    public bool SkipMulligan { get; init; }

    /// <summary>先手起手张数（默认 4）。</summary>
    public int OpeningHandSizeFirstPlayer { get; init; } = Orc.Game.MatchOptions.DefaultOpeningHandSizeFirstPlayer;

    /// <summary>后手起手张数（默认 5）。</summary>
    public int OpeningHandSizeSecondPlayer { get; init; } = Orc.Game.MatchOptions.DefaultOpeningHandSizeSecondPlayer;

    /// <summary>
    /// 卡组张数要求（默认 39——KARDS 默认构筑口径：39 张卡组，总部随玩家自动生成）。
    /// 不满足＝创建失败；同名张数不限。
    /// </summary>
    public int RequiredDeckSize { get; init; } = 39;
}
