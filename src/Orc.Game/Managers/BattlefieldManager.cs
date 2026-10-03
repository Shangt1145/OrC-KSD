using Orc.Game.Board;

namespace Orc.Game.Managers;

/// <summary>
/// 战场管理器（战场领域真源）：创建并持有战场（三条空战线）＋提供访问；
/// 容量配置 4/5/4 随战场对象可读（位置＝<see cref="Board.Battlefield"/> 各线容量属性）。
/// </summary>
public sealed class BattlefieldManager
{
    /// <summary>创建战场管理器（初始化动作：创建三条空战线）。</summary>
    public BattlefieldManager()
    {
        Battlefield = new Battlefield();
    }

    /// <summary>战场（三线：玩家A 支援线 / 前线 / 玩家B 支援线）。</summary>
    public Battlefield Battlefield { get; }
}
