using Orc.Core;

namespace Orc.Game.Board;

/// <summary>
/// 战线槽位（2A 槽位模型；2B 槽位引用形态）：
/// 位置（线内索引）＋占用者（单位/总部/空——对象类型可判；空＝null）。
/// 总部占用者＝总部实体 <see cref="Players.Hq"/> 引用（W3-3 HQ 实体化：由「Player 引用占位」改；
/// 单位占用者＝单位卡牌实例）。
/// 结构不变量：每槽至多一个占用者（放置仅空槽；清空幂等）。
/// 写面受控：放置/清空为本模型的基础操作（HQ 占位 / 部署 / 移动的底层路径；部署校验属后续批次）。
/// 实例由所属线创建（线内索引固定），不提供外部构造。
/// 〔2B 受控变更——槽位引用形态〕本类现继承 <see cref="Entity"/>：槽位获得引擎引用能力（<see cref="Entity.Ref"/>，类型为
/// <c>Ref&lt;Entity&gt;</c>），作为「槽位引用」进入 targeter 候选/产出面（部署预打出「选中空槽位」的承载；沿用既有 Ref&lt;Entity&gt; 处理路径，
/// 不破坏其他引用场景）；名称由所属线在创建时指定（如「玩家A支援线[1]」）；索引/占用者/放置/清空等既有面不变。
/// 〔W3-3 说明：HQ 目标承载已改以 HQ 实体引用为准——槽位引用不再作为 HQ 目标产出；槽位关系仅用于布局语义。〕
/// </summary>
public sealed class Slot : Entity
{
    internal Slot(int index, string lineName)
        : base($"{lineName}[{index}]")
    {
        Index = index;
    }

    /// <summary>线内索引（0..容量-1；由所属线在创建时指定）。</summary>
    public int Index { get; }

    /// <summary>占用者（null＝空；Player＝总部；单位卡牌实例＝单位）。</summary>
    public object? Occupant { get; private set; }

    /// <summary>是否为空（无占用者）。</summary>
    public bool IsEmpty => Occupant is null;

    /// <summary>放置占用者（空槽 → 放置；已占＝明确拒绝——「放置仅空槽」的结构不变量）。</summary>
    /// <exception cref="ArgumentNullException">occupant 为 null。</exception>
    /// <exception cref="InvalidOperationException">槽位已被占用（如需变更先清空）。</exception>
    public void Place(object occupant)
    {
        ArgumentNullException.ThrowIfNull(occupant);
        if (!IsEmpty)
        {
            throw new InvalidOperationException($"槽位 {Index} 已被占用（放置仅空槽；如需变更先清空）。");
        }

        Occupant = occupant;
    }

    /// <summary>清空槽位（幂等：已空＝无操作、不抛错）。</summary>
    public void Clear() => Occupant = null;
}
