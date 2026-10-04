namespace Orc.Game.Targeting;

/// <summary>
/// 选项条目（抉择候选的声明条目）：标识（非空、稳定、可判等——消费方分派与测试断言键）＋
/// 文本（非空——前端呈现用）。
/// "分支"不是条目上的独立字段——"分支"＝消费方"按标识分派"的行为约定（分派键＝标识）；
/// 除两字段外条目不承载其他数据（呈现附加值等属后续扩展点）。
/// 条目不参与候选收集/规范化/筛选链（非引用候选）；随请求描述交付前端（按"选项列表"渲染；顺序＝声明序）。
/// </summary>
public sealed class OptionEntry
{
    /// <summary>创建选项条目。</summary>
    /// <param name="id">标识（非 null/空白；声明集内要求唯一——唯一性在槽位构造期校验）。</param>
    /// <param name="text">文本（非 null/空白；前端呈现用；不要求唯一——区分靠标识）。</param>
    /// <exception cref="ArgumentException">id 或 text 为 null/空白。</exception>
    public OptionEntry(string id, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Id = id;
        Text = text;
    }

    /// <summary>标识（非空、稳定、可判等；消费方按标识分派分支、测试断言键）。</summary>
    public string Id { get; }

    /// <summary>文本（非空；前端呈现用；不进产出——产出仅含选中标识）。</summary>
    public string Text { get; }
}

/// <summary>
/// 选项槽位（抉择）：候选＝声明条目集（标识＋文本；<b>非引用候选</b>——不经收集/规范化/粗筛/细筛链，
/// 由声明携带、随 Begin 请求描述交付前端）。
/// 单选语义（恰选 1 个：min＝max＝1）；终局校验变体＝「选中标识 ∈ 声明集」（加既有数量约束）；
/// 产出＝选中选项标识（按槽位名经非引用读面读取——文本等呈现数据不进产出）；消费方按标识分派分支。
/// 声明合法性（构造期拒绝，fail-fast）：条目集非空（空＝构造期错误）；标识唯一；条目/标识/文本均非空。
/// 声明随构造期固定（只读快照；无中途修改语义）。
/// </summary>
public sealed class OptionSelectSlot : TargetSlot
{
    /// <summary>创建选项槽位。</summary>
    /// <param name="options">选项条目集（声明序＝前端呈现序与产出序来源；标识唯一）。</param>
    /// <param name="name">槽位名（可省略；省略/空白＝归缺省槽位）。</param>
    /// <exception cref="ArgumentNullException">options 为 null。</exception>
    /// <exception cref="ArgumentException">条目集为空（非引用类声明集为空＝构造期错误）；含 null 条目；标识重复。</exception>
    public OptionSelectSlot(IEnumerable<OptionEntry> options, string? name = null)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(options);

        var list = new List<OptionEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in options)
        {
            if (entry is null)
            {
                throw new ArgumentException("选项条目集含 null 条目（构造期拒绝）。", nameof(options));
            }

            if (!ids.Add(entry.Id))
            {
                throw new ArgumentException(
                    $"选项标识 '{entry.Id}' 重复（声明集内标识要求唯一——构造期拒绝；文本不要求唯一）。", nameof(options));
            }

            list.Add(entry);
        }

        if (list.Count == 0)
        {
            throw new ArgumentException(
                "选项条目集为空（非引用类声明集为空＝构造期错误——fail-fast）。", nameof(options));
        }

        Options = list.ToArray();
    }

    /// <summary>选项条目集（声明序只读快照；随请求描述交付前端）。</summary>
    public IReadOnlyList<OptionEntry> Options { get; }

    internal override TargetSlotKind Kind => TargetSlotKind.OptionSelect;

    internal override int MinSelection => 1;

    internal override int MaxSelection => 1;

    internal override bool IsReferenceKind => false;

    internal override TargetSlotPresentation Presentation => TargetSlotPresentation.OptionList;
}
