namespace Orc.Game.Targeting;

/// <summary>槽位种类（描述与产出形态用：扁平产出＝无槽位/单槽位；非扁平产出＝多槽位）。</summary>
public enum TargetSlotKind
{
    /// <summary>单选（"选 1 个"：1..1）。</summary>
    SingleSelect,

    /// <summary>多选（min..max 范围约束）。</summary>
    MultiSelect,
}

/// <summary>
/// 选择槽位（抽象基类）：名字（省略/空白＝归"缺省槽位"）＋选择数量约束。
/// 声明随 <see cref="Targeter"/> 构造期固定（无中途修改语义；传参与复用不改变声明）。
/// 名字归一后非空；互不重复为声明合法性要求（重复在 Targeter 构造期拒绝）。
/// 槽位类型扩展由库内提供（构造仅限库内派生）——扩展须保持既有调用不破坏。
/// </summary>
public abstract class TargetSlot
{
    /// <summary>缺省槽位名（未声明槽位/匿名槽位的稳定承载键；对业务方可见、mock 与前端可依据）。</summary>
    public const string DefaultName = "default";

    private protected TargetSlot(string? name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? DefaultName : name;
    }

    /// <summary>槽位名（归一后非空；省略/空白名归缺省名 <see cref="DefaultName"/>）。</summary>
    public string Name { get; }

    /// <summary>槽位种类。</summary>
    internal abstract TargetSlotKind Kind { get; }

    /// <summary>完成/确认时至少须选到的个数（SingleSelect＝1；MultiSelect＝构造 min）。</summary>
    internal abstract int MinSelection { get; }

    /// <summary>完成/确认时至多可选的个数（SingleSelect＝1；MultiSelect＝构造 max）。</summary>
    internal abstract int MaxSelection { get; }
}

/// <summary>单选槽位（"选 1 个"的语义特化：min＝max＝1，必选恰 1 个）。</summary>
public sealed class SingleSelectSlot : TargetSlot
{
    /// <summary>创建单选槽位。</summary>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    public SingleSelectSlot(string? name = null)
        : base(name)
    {
    }

    internal override TargetSlotKind Kind => TargetSlotKind.SingleSelect;

    internal override int MinSelection => 1;

    internal override int MaxSelection => 1;
}

/// <summary>
/// 多选槽位（min..max 范围约束）：min 语义＝完成/确认时必须至少选到 min 个；min＝0 合法（允许空选完成＝成功、空产出）。
/// 构造期校验（拒绝非法配置，不假设业务方正确）：min ≥ 0、max ≥ 1、min ≤ max。
/// </summary>
public sealed class MultiSelectSlot : TargetSlot
{
    /// <summary>创建多选槽位。</summary>
    /// <param name="min">至少须选到的个数（≥0；0＝允许空选完成）。</param>
    /// <param name="max">至多可选的个数（≥1）。</param>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    /// <exception cref="ArgumentOutOfRangeException">min 为负；或 max 非正整数。</exception>
    /// <exception cref="ArgumentException">min 大于 max（关系在构造期校验）。</exception>
    public MultiSelectSlot(int min, int max, string? name = null)
        : base(name)
    {
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

        Min = min;
        Max = max;
    }

    /// <summary>至少须选到的个数（≥0）。</summary>
    public int Min { get; }

    /// <summary>至多可选的个数（≥1）。</summary>
    public int Max { get; }

    internal override TargetSlotKind Kind => TargetSlotKind.MultiSelect;

    internal override int MinSelection => Min;

    internal override int MaxSelection => Max;
}
