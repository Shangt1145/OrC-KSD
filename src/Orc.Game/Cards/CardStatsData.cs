namespace Orc.Game.Cards;

// ─────────────────────────────────────────────────────────────────────────────
// 本文件「旧 → 新」改造说明（2A 受控变更）：
// 原承载「四合一」组件 CardStatsData（部署费 / 行动费 / 攻击力 / 防御力；原注释语义：
// 「『基础数据』为原子组合、不拆分」）。2A 依需求拆分实施：【指挥点花费（部署费）】与
// 【对战（行动费 / 攻击力 / 防御力——初始值）】分立为两个组件；原「不拆分」语义反转——
// 同一数据不得双真源（四合一退役、类型不再存在，实例化路径改挂拆分后的组件：
// 全类别＝指挥点花费；单位＝另加对战〔需求原文 E 区：单位＝对战数据组件、指令无其他组件、反制＝激活状态组件〕）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 指挥点花费数据组件（单列）：部署费——打出时的指挥点消耗（术语确认：指挥点花费＝部署费；与 KARDS 基线一致）。
/// 装配范围＝全类别（单位 / 指令 / 反制）；配置数据、只读语义基准的同类：构造期注入、运行期不改（语言级只读——get-only）。
/// W3-2 G5：「基准/有效」区分落地——本面承载「基准」（定义静态值，链起点）；「有效部署费」＝修饰机制链输出
/// （修饰/设值/钳制等贡献叠加后的读取口径），由链读取口统一提供；打出校验/扣费/评估/翻转等结算读取点统一读有效值。
/// 原 get-only 基准语义与构造调用保持兼容（不新增便捷读取面——有效值经修饰机制读取口查询）。
/// 以引擎数据组件形态挂载：经 <see cref="Orc.Cards.Card.AddData"/> 装配、<c>GetData&lt;T&gt;</c> 读取（引用共享）。
/// 纯数据、无行为方法；数值域校验后置（负数等，规则批次）。
/// </summary>
public sealed class CommandPointCostData
{
    /// <summary>创建组件（部署费初始值注入）。</summary>
    public CommandPointCostData(int deployCost) => DeployCost = deployCost;

    /// <summary>部署费基准值（定义静态值——链起点；运行期本体不改；「有效部署费」经链读取口查询）。</summary>
    public int DeployCost { get; }
}

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
