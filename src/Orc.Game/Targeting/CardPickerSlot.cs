namespace Orc.Game.Targeting;

/// <summary>
/// 卡牌选择器形态（构造期声明二择一——声明即定型、含产出类型；请求期载荷须匹配声明，不匹配＝请求构造期拒绝）。
/// </summary>
public enum CardPickerForm
{
    /// <summary>名单形态：定义级卡牌标识集合＋呈现要素（至少可读名称级）——非引用类；产出＝选中标识（消费方生成实例）。</summary>
    Listing,

    /// <summary>引用集形态：卡引用（<see cref="Orc.Core.Ref{T}"/>）集合——引用类；产出＝卡引用。</summary>
    ReferenceSet,
}

/// <summary>
/// 卡牌名单条目（定义级标识＋呈现要素；≥可读名称——更多卡面数据属扩展点）。
/// 随请求描述交付前端（屏中卡牌阵列的卡名级要素）；不参与候选收集/规范化/筛选链（非引用候选）。
/// </summary>
public sealed class CardListing
{
    /// <summary>创建卡牌名单条目。</summary>
    /// <param name="id">定义级卡牌标识（非 null/空白；沿用现有卡库 id 体系——足以按标识生成实例）。</param>
    /// <param name="name">可读名称（非 null/空白；呈现要素）。</param>
    /// <exception cref="ArgumentException">id 或 name 为 null/空白。</exception>
    public CardListing(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    /// <summary>定义级卡牌标识（既有卡库 id 体系；消费方按标识生成实例）。</summary>
    public string Id { get; }

    /// <summary>可读名称（呈现要素，至少可读名称级）。</summary>
    public string Name { get; }
}

/// <summary>
/// 卡牌选择器槽位（开发/发现类）：候选＝卡集合，<b>载荷两形态（构造期声明二择一）</b>——
/// ①名单形态（<see cref="CardPickerForm.Listing"/>）：定义级标识＋呈现要素（≥可读名称）；请求构造期携带名单载荷；
///   产出＝选中定义标识（按槽位名、非引用读面；"选中后由消费方生成实例"——框架不生成实例）；
/// ②引用集形态（<see cref="CardPickerForm.ReferenceSet"/>）：卡引用（Ref）集合；请求构造期携带引用集载荷；
///   产出＝卡引用（沿用 Ref 读面）。
/// 数量可配置（min..max；含"恰选 1"）；"多张"＝卡牌阵列呈现（<see cref="TargetSlotPresentation.CardArray"/>）、
/// 非数量下界（候选数量以载荷为准）。
/// 空集语义：名单为空＝构造期错误（fail-fast——对齐"非引用类空集＝构造期错误"）；
/// 引用集为空＝失败（不进交互——对齐"必须非空槽位空集＝失败"）；
/// 载荷与声明形态不匹配（缺失/错配）＝请求构造期拒绝。
/// 引用集不要求额外域校验（∈允许集＋IsAlive 沿用；如需域语义可按通用请求级绑定能力使用）。
/// 呈现＝"屏幕中央呈现多张卡牌 → 选择 → 确认"（供前端桥接；呈现提示、非策略指令）。
/// 与生成职责边界：不含实例生成（生成＝消费方；完整链延后、依赖 G6）。
/// </summary>
public sealed class CardPickerSlot : TargetSlot
{
    /// <summary>创建卡牌选择器槽位。</summary>
    /// <param name="form">形态（构造期声明二择一；声明即定型、含产出类型）。</param>
    /// <param name="min">至少须选到的个数（≥0）。</param>
    /// <param name="max">至多可选的个数（≥1）。</param>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    /// <exception cref="ArgumentOutOfRangeException">form 非已定义值；或 min 为负；或 max 非正整数。</exception>
    /// <exception cref="ArgumentException">min 大于 max（关系在构造期校验）。</exception>
    public CardPickerSlot(CardPickerForm form, int min, int max, string? name = null)
        : base(name)
    {
        if (form is not (CardPickerForm.Listing or CardPickerForm.ReferenceSet))
        {
            throw new ArgumentOutOfRangeException(nameof(form), form, "形态须为已定义值（名单 / 引用集——构造期声明二择一）。");
        }

        if (min < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), min, "min 不能为负（min=0 表示允许空选完成；上限见 max）。");
        }

        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "max 须为正整数（≥1）。");
        }

        if (min > max)
        {
            throw new ArgumentException($"min（{min}）不能大于 max（{max}）（槽位约束关系在构造期校验）。", nameof(min));
        }

        Form = form;
        Min = min;
        Max = max;
    }

    /// <summary>形态（构造期声明二择一；声明即定型）。</summary>
    public CardPickerForm Form { get; }

    /// <summary>至少须选到的个数（≥0）。</summary>
    public int Min { get; }

    /// <summary>至多可选的个数（≥1）。</summary>
    public int Max { get; }

    internal override TargetSlotKind Kind => TargetSlotKind.CardPicker;

    internal override int MinSelection => Min;

    internal override int MaxSelection => Max;

    internal override bool IsReferenceKind => Form == CardPickerForm.ReferenceSet;

    internal override TargetSlotPresentation Presentation => TargetSlotPresentation.CardArray;
}
