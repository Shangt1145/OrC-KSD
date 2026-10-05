namespace Orc.Game.Cards;

/// <summary>
/// 国籍规则助手（S10「G12补」——主国/盟国构筑配置的规则知识收口）：
/// 五大主国（德国 / 苏联 / 美国 / 英国 / 日本）判定＋盟国值域判定。
/// 主国值域＝五主国（<see cref="MajorFactions"/>）；盟国值域＝其余 4 个主国＋5 个盟国（芬/法/波/澳新/意）＝9 值——
/// 等价规则：已定义枚举值 ∧ 非 Neutral ∧ 非主国自身（<see cref="IsValidAlly"/>）。
/// 消费＝对局创建期的玩家构筑配置校验（<see cref="Orc.Game.PlayerDeckConfiguration"/> 的 fail-fast 校验）。
/// </summary>
public static class FactionRules
{
    /// <summary>五大主国（声明序＝枚举序：德 / 苏 / 美 / 英 / 日；「主国」值域）。</summary>
    public static IReadOnlyList<Faction> MajorFactions { get; } = new[]
    {
        Faction.Germany,
        Faction.Soviet,
        Faction.USA,
        Faction.Britain,
        Faction.Japan,
    };

    /// <summary>是否五大主国（主国值域判定——对局创建期校验消费）。</summary>
    public static bool IsMajorFaction(Faction faction) => MajorFactions.Contains(faction);

    /// <summary>
    /// 盟国值域判定：已定义枚举值 ∧ 非 Neutral（Neutral 不得作为盟国）∧ 非主国自身
    /// （盟国≠主国——「盟国 ∈ 余下 4 个主国＋5 个盟国〔芬/法/波/澳新/意〕＝9 值」的等价规则实现）。
    /// </summary>
    public static bool IsValidAlly(Faction mainFaction, Faction allyFaction)
        => Enum.IsDefined(allyFaction)
            && allyFaction != Faction.Neutral
            && allyFaction != mainFaction;
}
