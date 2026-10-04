namespace Orc.Game.Cards;

/// <summary>
/// 国籍 / 阵营枚举（KARDS 已知国家全量 11 值；W1-1 G12 引入）。
/// 成员清单与来源数据（docs/kards官方卡牌.json）faction 字段一致——Germany / Soviet / USA / Britain / Japan /
/// France / Italy / Poland / Finland / Anzac / Neutral；五大主国（德国/苏联/美国/英国/日本）在内。
/// 用途：卡牌定义的必填槽位值域（<see cref="CardDefinition.Faction"/>）；「日本精英空军」式组合筛选的维度之一。
/// </summary>
public enum Faction
{
    /// <summary>德国。</summary>
    Germany = 0,

    /// <summary>苏联。</summary>
    Soviet = 1,

    /// <summary>美国。</summary>
    USA = 2,

    /// <summary>英国。</summary>
    Britain = 3,

    /// <summary>日本。</summary>
    Japan = 4,

    /// <summary>法国。</summary>
    France = 5,

    /// <summary>意大利。</summary>
    Italy = 6,

    /// <summary>波兰。</summary>
    Poland = 7,

    /// <summary>芬兰。</summary>
    Finland = 8,

    /// <summary>澳新联军（Anzac；来源数据的合法阵营归属）。</summary>
    Anzac = 9,

    /// <summary>中立（来源数据的合法阵营归属）。</summary>
    Neutral = 10,
}

/// <summary>
/// 稀有度枚举（KARDS；W1-1 G12 引入）。
/// 中文语义对照：基础＝Standard、限定＝Limited、特殊＝Special、精英＝Elite；
/// 与来源数据 rarity 字段四值一一对应。用途：卡牌定义的必填槽位值域（<see cref="CardDefinition.Rarity"/>）。
/// </summary>
public enum Rarity
{
    /// <summary>基础（Standard）。</summary>
    Standard = 0,

    /// <summary>限定（Limited）。</summary>
    Limited = 1,

    /// <summary>特殊（Special）。</summary>
    Special = 2,

    /// <summary>精英（Elite）。</summary>
    Elite = 3,
}

/// <summary>
/// 强类型槽位（W1-1 G12；泛型约束形态＝<c>where TTag : struct, Enum</c>）：以泛型参数承载槽位值域，
/// 不同槽位（国籍 / 稀有度）为不同类型、彼此不可混用（强类型约束）。
/// 值恒有且只读：构造即固定（<see cref="Value"/> 无写面）——实例侧只读（本批不支持运行时修改）。
/// fail-fast：未定义枚举值在构造期被拒绝（沿用既有 Enum 校验风格）。
/// </summary>
/// <typeparam name="TTag">槽位值域枚举（值类型枚举）。</typeparam>
public sealed class TagSlot<TTag>
    where TTag : struct, Enum
{
    /// <summary>创建槽位（值域校验：未定义枚举值＝明确错误、不吞）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">value 为未定义枚举值（配置错误在定义期被拒绝）。</exception>
    public TagSlot(TTag value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "槽位值为未定义枚举值（配置错误在定义期被拒绝）。");
        }

        Value = value;
    }

    /// <summary>槽位值（恒有、只读——实例侧不可修改）。</summary>
    public TTag Value { get; }
}

/// <summary>
/// 卡牌标签数据组件（W1-1 G12；以引擎数据组件形态挂载——加载时装配、<c>GetData&lt;T&gt;</c> 读取）：
/// ① 必填强类型槽位：国籍（<see cref="TagSlot{T}"/> of <see cref="Faction"/>）＋ 稀有度（<see cref="TagSlot{T}"/> of <see cref="Rarity"/>）——
///    恒有值且实例侧只读（随定义声明固定；本批不支持运行时修改）；
/// ② 开放 tag 集合（子类别：海军 / T-34 / 谢尔曼等）：平等的一组开放 tag 值（单一「子类别」性质集合、不另立命名维度）；
///    完全开放值域（任意非空白字符串可增删、无受控清单）；支持运行时增删（作用于目标卡牌实例、不影响定义与同定义的其它实例）；
///    增删为静默数据变更（不产生任何更新 / 通知——无消费方，读取与筛选随动即观测面）。
/// 与词条边界：开放 tag 不承载机制行为；机制词条不走本组件（独立机制体系、勿混用）。
/// 未加载实例＝组件缺失、明确错误（沿用「未装配实例取件＝明确错误」先例）。
/// </summary>
public sealed class TagData
{
    private readonly List<string> _tags = new();

    /// <summary>创建标签数据（槽位值经 <see cref="TagSlot{T}"/> 装配与校验；开放 tag 初始为空——后续经 <see cref="AddTag"/> 登记）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">faction / rarity 为未定义枚举值。</exception>
    public TagData(Faction faction, Rarity rarity)
    {
        FactionSlot = new TagSlot<Faction>(faction);
        RaritySlot = new TagSlot<Rarity>(rarity);
    }

    /// <summary>国籍槽位（必填、只读；槽位值经 <see cref="TagSlot{T}.Value"/> 读取）。</summary>
    public TagSlot<Faction> FactionSlot { get; }

    /// <summary>稀有度槽位（必填、只读；槽位值经 <see cref="TagSlot{T}.Value"/> 读取）。</summary>
    public TagSlot<Rarity> RaritySlot { get; }

    /// <summary>国籍便捷读面（＝<see cref="FactionSlot"/>.Value）。</summary>
    public Faction Faction => FactionSlot.Value;

    /// <summary>稀有度便捷读面（＝<see cref="RaritySlot"/>.Value）。</summary>
    public Rarity Rarity => RaritySlot.Value;

    /// <summary>开放 tag 枚举（只读面；登记序；空列表＝无开放 tag）。</summary>
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>
    /// 登记一个开放 tag（运行时增删口；幂等：已登记＝false 无操作、新增＝true——与词条登记先例一致）。
    /// </summary>
    /// <exception cref="ArgumentException">tag 为 null/空白（值非空——fail-fast）。</exception>
    public bool AddTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (_tags.Contains(tag))
        {
            return false;
        }

        _tags.Add(tag);
        return true;
    }

    /// <summary>
    /// 注销一个开放 tag（运行时增删口；幂等：未登记＝false 无操作、移除＝true——与词条登记先例一致）。
    /// </summary>
    /// <exception cref="ArgumentException">tag 为 null/空白（值非空——fail-fast）。</exception>
    public bool RemoveTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _tags.Remove(tag);
    }

    /// <summary>查询：是否含该开放 tag（null/空白＝false、不抛错——存在性查询口径，与词条查询一致）。</summary>
    public bool ContainsTag(string tag) => !string.IsNullOrWhiteSpace(tag) && _tags.Contains(tag);
}
