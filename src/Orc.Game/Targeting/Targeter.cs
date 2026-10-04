namespace Orc.Game.Targeting;

/// <summary>
/// Targeter（指示器／请求对象）：承载一次目标选择的参数（筛选器、槽位〔可缺省——缺省＝单一选择〕、
/// 请求级槽位数据〔请求构造期绑定〕）；
/// 唯一发起入口＝<see cref="Targeting"/>（异步：内部自动经 <see cref="TargeterManager"/> 排队与桥接等待、产出引用）。
/// 复用：允许串行多次使用（每次独立执行、独立结果；并发/未完成时再发＝排队〔不拒绝〕，结局与产出互不干扰）。
/// 请求对象本身不持执行期可变状态（请求状态随每次执行独立创建），复用无需内部状态重置。
/// 槽位声明随请求对象、构造期固定（无中途修改语义；传参与复用不改变声明）。
/// 请求级槽位数据（context）：请求构造期绑定、请求结束即弃——不挂到槽位声明上（声明可复用，允许集/判定面
/// 随每次请求重新绑定）；构造期校验（fail-fast）：绑定槽位名须为声明槽位、绑定类别须与槽位类别/形态匹配、
/// 手牌槽位强制域判定面与允许集、卡牌选择器〔名单形态〕须非空名单载荷。
/// </summary>
public sealed class Targeter
{
    private readonly TargeterManager _manager;

    /// <summary>创建请求对象（请求对象本身不规定获取路径——管理器读取/闭包捕获等均可；范式用例演示可行路径）。</summary>
    /// <param name="manager">所属目标选择管理器（其队列与桥接承载本次 targeting 的执行）。</param>
    /// <param name="filter">筛选器（两级均可缺省；可只给其一；null＝两级全通过）。筛选链仅作用于既有引用类槽位（收集产物）。</param>
    /// <param name="slots">槽位声明（可缺省——缺省＝单一选择语义；声明构造期固定）。</param>
    /// <param name="context">请求级槽位数据（可选；请求构造期绑定——新引用类允许集/域判定面/卡牌名单载荷；缺省＝无绑定）。</param>
    /// <exception cref="ArgumentNullException">manager 为 null。</exception>
    /// <exception cref="ArgumentException">槽位声明含 null 元素；槽位名重复（同名归并＝非法声明，构造期拒绝）；
    /// 请求级数据绑定不合法（未声明槽位名/类别错配/手牌槽位缺判定面或允许集/名单载荷缺失或为空——请求构造期拒绝）。</exception>
    public Targeter(TargeterManager manager, TargetFilter? filter = null, IEnumerable<TargetSlot>? slots = null, TargetingRequestContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        Filter = filter;

        var list = new List<TargetSlot>();
        if (slots is not null)
        {
            foreach (var slot in slots)
            {
                if (slot is null)
                {
                    throw new ArgumentException("槽位声明含 null 元素。", nameof(slots));
                }

                list.Add(slot);
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in list)
        {
            if (!names.Add(slot.Name))
            {
                throw new ArgumentException(
                    $"槽位名 '{slot.Name}' 重复（名字互不重复为声明合法性要求；省略/空白名归缺省槽位）。", nameof(slots));
            }
        }

        Slots = list.ToArray();
        ValidateRequestContext(Slots, context);
        Context = context;
    }

    /// <summary>筛选器（两级均可缺省；构造期固定、无中途修改语义）。</summary>
    public TargetFilter? Filter { get; }

    /// <summary>槽位声明（声明序只读快照；缺省＝空＝单一选择语义）。</summary>
    public IReadOnlyList<TargetSlot> Slots { get; }

    /// <summary>请求级槽位数据（请求构造期绑定；随请求对象存续、请求结束即弃；缺省＝无绑定）。</summary>
    internal TargetingRequestContext? Context { get; }

    /// <summary>
    /// 执行（异步）：经所属管理器排队 → 出队执行（候选收集 → 规范化 → 粗筛 → 细筛 → 交互等待）→ 产出统一结果对象。
    /// 结局三态（成功/取消/失败）均经结果对象表达、不抛异常；失败/取消不使队列崩溃（队列继续出队下一条）。
    /// </summary>
    public Task<TargetingResult> Targeting() => _manager.Enqueue(this);

    /// <summary>所属管理器（框架内部读面）。</summary>
    internal TargeterManager Manager => _manager;

    /// <summary>
    /// 请求级数据校验（请求构造期；fail-fast——对齐既有"拒绝非法配置"风格）：
    /// ①绑定槽位名须为声明槽位名（未声明＝拒绝）；
    /// ②绑定类别须与槽位类别/形态匹配（引用集〔手牌/卡牌选择器·引用集〕；名单〔卡牌选择器·名单〕；域判定〔引用类通用〕）；
    /// ③手牌槽位强制要求允许集＋域判定面（缺失＝拒绝）；
    /// ④卡牌选择器〔名单形态〕要求非空名单载荷（缺失＝载荷与声明形态不匹配＝拒绝；为空＝构造期错误＝拒绝）。
    /// </summary>
    private static void ValidateRequestContext(IReadOnlyList<TargetSlot> slots, TargetingRequestContext? context)
    {
        var byName = new Dictionary<string, TargetSlot>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            byName[slot.Name] = slot;
        }

        if (context is not null)
        {
            foreach (var name in context.BoundSlotNames)
            {
                if (!byName.TryGetValue(name, out var slot))
                {
                    throw new ArgumentException(
                        $"请求级数据绑定含未声明的槽位名 '{name}'（请求构造期拒绝）。", nameof(context));
                }

                if (context.HasReferences(name) && !AcceptsReferences(slot))
                {
                    throw new ArgumentException(
                        $"槽位 '{name}' 不接受引用集绑定（引用集仅适用于手牌选择槽位与卡牌选择器〔引用集形态〕；请求构造期拒绝）。",
                        nameof(context));
                }

                if (context.HasListings(name) && !AcceptsListings(slot))
                {
                    throw new ArgumentException(
                        $"槽位 '{name}' 不接受卡牌名单绑定（名单仅适用于卡牌选择器〔名单形态〕；请求构造期拒绝）。",
                        nameof(context));
                }

                if (context.HasDomainValidator(name) && !slot.IsReferenceKind)
                {
                    throw new ArgumentException(
                        $"槽位 '{name}' 为非引用类，不接受域判定面绑定（域判定为引用类槽位通用能力；请求构造期拒绝）。",
                        nameof(context));
                }
            }
        }

        foreach (var slot in slots)
        {
            if (slot is HandSelectSlot)
            {
                if (context is null || !context.HasDomainValidator(slot.Name))
                {
                    throw new ArgumentException(
                        $"槽位 '{slot.Name}' 为手牌选择槽位，请求未绑定域判定面（手牌槽位强制要求；请求构造期拒绝）。",
                        nameof(context));
                }

                if (!context.HasReferences(slot.Name))
                {
                    throw new ArgumentException(
                        $"槽位 '{slot.Name}' 为手牌选择槽位，请求未提供候选允许集（请求构造期拒绝）。",
                        nameof(context));
                }
            }
            else if (slot is CardPickerSlot picker)
            {
                if (picker.Form == CardPickerForm.Listing)
                {
                    if (context is null || !context.TryGetListings(picker.Name, out var listings))
                    {
                        throw new ArgumentException(
                            $"槽位 '{picker.Name}' 为卡牌选择器〔名单形态〕，请求未提供卡牌名单载荷（载荷与声明形态不匹配；请求构造期拒绝）。",
                            nameof(context));
                    }

                    if (listings.Count == 0)
                    {
                        throw new ArgumentException(
                            $"槽位 '{picker.Name}' 的卡牌名单为空（非引用类为空＝构造期错误；fail-fast）。",
                            nameof(context));
                    }
                }
                else
                {
                    if (context is null || !context.HasReferences(picker.Name))
                    {
                        throw new ArgumentException(
                            $"槽位 '{picker.Name}' 为卡牌选择器〔引用集形态〕，请求未提供卡牌引用集（载荷与声明形态不匹配；请求构造期拒绝）。",
                            nameof(context));
                    }
                }
            }
        }
    }

    private static bool AcceptsReferences(TargetSlot slot)
        => slot is HandSelectSlot || slot is CardPickerSlot { Form: CardPickerForm.ReferenceSet };

    private static bool AcceptsListings(TargetSlot slot)
        => slot is CardPickerSlot { Form: CardPickerForm.Listing };
}
