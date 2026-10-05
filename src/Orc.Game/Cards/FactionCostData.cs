namespace Orc.Game.Cards;

/// <summary>
/// 阵营〔国籍〕＋部署费 合并数据组件（S10「G12补+G14补」组件重构；主题口径 D17 修正——
/// 「阵营」＝所属国籍〔KARDS 无轴心/同盟划分〕，中文文档中「阵营〔国籍〕」与此同义）。
/// 合并范围：卡牌「阵营〔国籍〕＋花费（部署费）」合并入同一组件——行动费/攻/防属战斗数值域、保持既有位置
/// （<see cref="BattleStatsData"/> / <see cref="UnitStateData"/>；不在此次合并范围）。
/// 字段构成：【国籍（<see cref="Faction"/>）＋部署费（基准值）】。
/// 可修改形态（受控写面）：国籍与部署费均具备受控写面（运行时经受控入口修改；不裸露自由 setter）——
/// 受控入口＝卡侧门户（<see cref="CardBase.SetFaction"/> / <see cref="CardBase.SetDeployCostBaseAsync"/>；
/// 本类型的 Core 方法为内部执行面、非公开入口）；国籍修改＝静默（不发射/不通知——延续「元数据静默变更」先例）；
/// 部署费修改＝修改基准值（本体）——经既有数据改变管线传播（链重跑＋集中触发；复用既有
/// <see cref="GameUpdates.CardStatChanged"/>、不新增信号）；非法修改 fail-fast、不产生半改（原子——
/// 值域校验先于落值）。
/// 值域：国籍＝已定义枚举值（11 值）；部署费＝非负整数（0 合法）。
/// 单一真源（硬约束）：本组件之外不保留第二份数据/第二读面——旧独立花费组件（CommandPointCostData）退役、
/// 旧 TagData 国籍槽位移出（一次性重构、无过渡期、不保留兼容读面）。
/// 装配：构造期按定义注入（全类别：单位 / 指令 / 反制——对齐既有「花费＝全类别构造期装配」先例）；
/// 以引擎数据组件形态挂载（<see cref="Orc.Cards.Card.AddData"/> / <c>GetData&lt;T&gt;</c> 读取、引用共享）。
/// 就绪语义：全类别构造期常驻——部署费域更新检测（<see cref="DeployCostUpdateDetector"/>）恒就绪。
/// </summary>
public sealed class FactionCostData
{
    /// <summary>
    /// 创建组件（国籍＋部署费初始值——从定义注入）；初始值经受控值域同一校验
    /// （装配期非法输入 fail-fast 拒绝——与运行期受控写面同一口径）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">faction 为未定义枚举值；或 deployCost 为负。</exception>
    public FactionCostData(Faction faction, int deployCost)
    {
        SetFactionCore(faction);
        SetDeployCostCore(deployCost);
    }

    /// <summary>国籍（读面；运行期修改经受控写面——<see cref="SetFactionCore"/>，静默）。</summary>
    public Faction Faction { get; private set; }

    /// <summary>
    /// 部署费基准值（定义静态值——修饰链起点；读面。运行期修改经受控写面——<see cref="SetDeployCostCore"/>，
    /// 且须经卡侧门户衔接管线传播）；「有效部署费」＝修饰机制链输出（由链读取口统一提供），消费点统一读有效值。
    /// </summary>
    public int DeployCost { get; private set; }

    /// <summary>
    /// 受控写：设置国籍（内部执行面——由卡侧门户 <see cref="CardBase.SetFaction"/> 调用；本类型内初始装配共用）。
    /// 值域＝已定义枚举值（11 值）；非法值＝明确拒绝（fail-fast、值不变）；修改为静默数据变更（不发射/不通知）。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">faction 为未定义枚举值。</exception>
    internal void SetFactionCore(Faction faction)
    {
        if (!Enum.IsDefined(faction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(faction), faction, "国籍为未定义枚举值（受控写面拒绝——fail-fast、不产生半改）。");
        }

        Faction = faction;
    }

    /// <summary>
    /// 受控写：设置部署费基准值（内部执行面——由卡侧门户 <see cref="CardBase.SetDeployCostBaseAsync"/> 调用；
    /// 本类型内初始装配共用）。值域＝非负整数（0 合法）；非法值＝明确拒绝（fail-fast、值不变）；
    /// 本方法只落值——「经管线传播」（链重跑＋集中触发）由卡侧门户衔接。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">deployCost 为负。</exception>
    internal void SetDeployCostCore(int deployCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deployCost);
        DeployCost = deployCost;
    }
}
