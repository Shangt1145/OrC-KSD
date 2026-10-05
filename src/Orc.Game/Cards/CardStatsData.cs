namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 本文件「旧 → 新」改造说明（2A 受控变更 + S10 受控变更）：
// ① 2A：原承载「四合一」组件 CardStatsData（部署费 / 行动费 / 攻击力 / 防御力；原注释语义：
//    「『基础数据』为原子组合、不拆分」）。2A 依需求拆分实施：【指挥点花费（部署费）】与
//    【对战（行动费 / 攻击力 / 防御力——初始值）】分立为两个组件；原「不拆分」语义反转——
//    同一数据不得双真源（四合一退役、类型不再存在）。
// ② S10：指挥点花费（部署费）并入【阵营〔国籍〕＋部署费】合并组件
//    （<see cref="FactionCostData"/>——独立文件）；旧指挥点花费组件（原 CommandPointCostData）
//    退役、类型不再存在（不保留兼容读面——单一真源硬约束；一次性重构、无过渡期）。
//    本文件自此仅承载对战数据组件（三初始值）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 对战数据组件（行动花费 / 攻击力 / 防御力——初始值）：**单位卡专属**（E 区：单位＝对战数据组件；指令无其他组件；反制＝激活状态组件〔类就绪〕）。
/// 只读语义基准（初始值记录、运行期不改；语言级只读——get-only）。
/// 单位数据三实时值的初值来源：挂载时一次性复制（<see cref="UnitStateData.CreateInitial"/>）；效果/词条修改实时值不影响本基准。
/// 配置数据：构造期注入；以引擎数据组件形态挂载（<see cref="Orc.Cards.Card.AddData"/> / <c>GetData&lt;T&gt;</c>）。
/// 纯数据、无行为方法；数值域校验后置（负数等，规则批次）。
/// </summary>
public sealed class BattleStatsData
{
    /// <summary>创建组件（三初始值：行动费 / 攻击力 / 防御力）。</summary>
    public BattleStatsData(int operateCost, int attack, int defense)
    {
        OperateCost = operateCost;
        Attack = attack;
        Defense = defense;
    }

    /// <summary>行动花费初始值（移动/攻击消耗的指挥点；E1a 语义）。</summary>
    public int OperateCost { get; }

    /// <summary>攻击力初始值。</summary>
    public int Attack { get; }

    /// <summary>防御力初始值（游戏语义＝HP）。</summary>
    public int Defense { get; }
}
