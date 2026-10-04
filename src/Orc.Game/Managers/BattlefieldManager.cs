using Orc.Game.Board;
using Orc.Game.Players;

namespace Orc.Game.Managers;

/// <summary>
/// 战场管理器（战场领域真源）：创建并持有战场（三条战线；2A 起槽位化、构造期含 HQ 占位〔W3-3：占位者＝总部实体〕）＋提供访问；
/// 容量配置 4/5/4 随战场对象可读（位置＝<see cref="Board.Battlefield"/> 各线容量属性）。
/// </summary>
public sealed class BattlefieldManager
{
    /// <summary>创建战场管理器（初始化动作：创建三条战线＋HQ 初始占位——各支援线槽 0＝对应玩家的总部实体）。</summary>
    /// <exception cref="ArgumentNullException">playerA / playerB 为 null。</exception>
    public BattlefieldManager(Player playerA, Player playerB)
    {
        Battlefield = new Battlefield(playerA, playerB);
    }

    /// <summary>战场（三线：玩家A 支援线 / 前线 / 玩家B 支援线）。</summary>
    public Battlefield Battlefield { get; }
}
