using Orc.Game.Board;

namespace Orc.Game.Cards;

/// <summary>
/// 单位数据组件：所在位置（槽位引用；未部署＝null）、是否被摧毁（初始 false）、单位类型枚举列表（0 个/多个均合法）、
/// 三实时值（行动费 / 攻击力 / 防御力——运行值：初始＝对战组件值<see cref="CreateInitial"/>，供后续词条/效果修改）。
/// 「单位化」时装配（＋指挥组件共用单位化触发器）；实际挂载时机属后续批次（本批类定义就绪＋测试内构造挂载可测）。
/// 以引擎数据组件形态挂载：经 <see cref="Orc.Cards.Card.AddData"/> 装配、<c>GetData&lt;T&gt;</c> 读取（引用共享）。
/// 纯数据、无行为方法（伤害处理/死亡判定/单位化归属战斗与部署批次）。
/// </summary>
public sealed class UnitStateData
{
    /// <summary>创建空组件（位置 null、未摧毁、类型列表空、实时值 0——初值请经 <see cref="CreateInitial"/> 或直接赋值装载）。</summary>
    public UnitStateData()
    {
        UnitTypes = new List<UnitType>();
    }

    /// <summary>所在位置（槽位引用；未部署＝null）。</summary>
    public Slot? Position { get; set; }

    /// <summary>是否被摧毁（初始 false；摧毁判定/处理属后续批次）。</summary>
    public bool IsDestroyed { get; set; }

    /// <summary>单位类型枚举列表（允许 0 个/多个、不设非空校验；顺序＝登记序）。</summary>
    public List<UnitType> UnitTypes { get; }

    /// <summary>实时行动费（运行值；初始＝对战组件值）。</summary>
    public int OperateCost { get; set; }

    /// <summary>实时攻击力（运行值；初始＝对战组件值）。</summary>
    public int Attack { get; set; }

    /// <summary>实时防御力（运行值；初始＝对战组件值）。</summary>
    public int Defense { get; set; }

    /// <summary>
    /// 按「初始值复制契约」创建（挂载时用）：三实时值初值一次性复制自对战组件（只读基准）；
    /// 位置 null、未摧毁、类型列表空。后续效果/词条修改实时值不影响基准。
    /// </summary>
    /// <exception cref="ArgumentNullException">stats 为 null。</exception>
    public static UnitStateData CreateInitial(BattleStatsData stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new UnitStateData
        {
            OperateCost = stats.OperateCost,
            Attack = stats.Attack,
            Defense = stats.Defense,
        };
    }
}

/// <summary>
/// 指挥组件：单位的指挥状态——可移动 / 可攻击（两 bool）；初始 false/false。
/// 「两 bool 只在外层更新」（指挥系统批次）；翻转/置位逻辑属后批次（本批仅承载字段与读写）。
/// 以引擎数据组件形态挂载；「单位化」时装配（实际挂载时机属后续批次）。
/// </summary>
public sealed class CommandData
{
    /// <summary>可移动（初始 false）。</summary>
    public bool CanMove { get; set; }

    /// <summary>可攻击（初始 false）。</summary>
    public bool CanAttack { get; set; }
}

/// <summary>
/// 词条组件：词条标识的登记列表（本批载体形态＝字符串——推荐可扩展；只存标识）。
/// 词条数值（如「重甲 2」）由效果模块维护、不落本组件。
/// 基础增删查 API（列表级）：Add/Remove 幂等（重复登记/移除不存在＝无操作、返回 false）；标识非空（空白被拒绝）。
/// 以引擎数据组件形态挂载；加载时的词条登记属后续批次。
/// </summary>
public sealed class KeywordData
{
    private readonly List<string> _keywords = new();

    /// <summary>登记列表（只读枚举面；登记序）。</summary>
    public IReadOnlyList<string> Keywords => _keywords;

    /// <summary>登记一个词条标识（幂等：已登记＝false 无操作；新增＝true）。</summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白（标识非空）。</exception>
    public bool Add(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (_keywords.Contains(keyword))
        {
            return false;
        }

        _keywords.Add(keyword);
        return true;
    }

    /// <summary>注销一个词条标识（幂等：未登记＝false 无操作；移除＝true）。</summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白（标识非空）。</exception>
    public bool Remove(string keyword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        return _keywords.Remove(keyword);
    }

    /// <summary>查询：是否已登记该词条标识（null/空白＝false、不抛错——存在性查询口径）。</summary>
    public bool Contains(string keyword) => !string.IsNullOrWhiteSpace(keyword) && _keywords.Contains(keyword);
}

/// <summary>
/// 反制激活状态组件：反制卡当前是否已激活（初始 false）。
/// 激活流程（先验证〔未激活时检查指挥点〕→ 合法 → 反转状态 → 扣/退点）属打出链批次；
/// 翻转/置位逻辑属后批次（本批仅承载字段与读写）。
/// 以引擎数据组件形态挂载；实际挂载时机属后续批次（本批类定义就绪＋测试内构造挂载可测）。
/// </summary>
public sealed class CounterActivationData
{
    /// <summary>是否已激活（初始 false）。</summary>
    public bool IsActive { get; set; }
}
