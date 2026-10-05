using Orc.Game.Cards;

namespace Orc.Game;

/// <summary>
/// 玩家构筑配置（S10「G12补」；对局创建输入——卡组构筑元数据的载荷类型）：
/// 每组＝主国 1（五主国：德/苏/美/英/日中的一）＋盟国 1（从余下 4 个主国与 5 个盟国〔芬/法/波/澳新/意〕中选——
/// 排除主国自身与 Neutral）；两值为成对配置（提供时齐备；「无盟国」形态不在本批）。
/// 校验时点＝对局创建期（<see cref="Match"/> 构造——提供即校验、全部 fail-fast；
/// 未提供〔null〕＝跳过全部校验、不抛错）；本类型只承载（不校验——单一校验点为对局创建期，
/// 覆盖 4 类非法值＋成对缺失的拒绝形态）。
/// 载入流转：创建输入（唯一数据来源）→ 对局创建期接收校验 → 初始化期注入各 <see cref="Orc.Game.Players.Player"/>
/// → Player 持有为读取面（只读属性；无写路径、值不可变）。
/// </summary>
public sealed class PlayerDeckConfiguration
{
    /// <summary>创建玩家构筑配置（主国＋盟国；值域校验由对局创建期执行——见类型注释）。</summary>
    public PlayerDeckConfiguration(Faction? mainFaction, Faction? allyFaction)
    {
        MainFaction = mainFaction;
        AllyFaction = allyFaction;
    }

    /// <summary>主国（五主国之一；未指定＝null——该形态由对局创建期按成对口径拒绝）。</summary>
    public Faction? MainFaction { get; }

    /// <summary>盟国（余下 4 个主国＋5 个盟国；未指定＝null——该形态由对局创建期按成对口径拒绝）。</summary>
    public Faction? AllyFaction { get; }
}
