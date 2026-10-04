using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 请求级槽位数据（<b>请求构造期绑定</b>；每次请求独立提供、请求结束即弃——随请求对象存续，不挂到槽位声明上；
/// 槽位声明只含结构〔名/数量约束〕，状态与归属不入声明——声明可复用，判定面随每次请求重新绑定）。
/// 承载三类数据（均按槽位名绑定）：
/// ①域判定面（引用类槽位通用、可选；<b>手牌选择槽位强制要求</b>）——终局校验时对每个提交项同步调用（P2 语义）；
///   与允许集为叠加关系：允许集＝请求时快照（呈现范围＋静态校验）∧ 域判定（动态成员性，如"仍在手牌"）∧ 既有校验（IsAlive）；
///   交付前端的允许子集照快照交付、不经判定过滤（判定为终局校验职责、动态性由其兜底）；
///   失败语义：判定返回 false（确定不合规，如"已离手"）＝拒绝（同"内容不合规"路径）；判定抛异常＝失败结局；
/// ②新引用类槽位的允许集（手牌选择 / 卡牌选择器〔引用集形态〕）——构造方提供的快照（请求期固定；
///   新增手牌不进入可选范围；框架不内建域读取——由构造方从对局状态构造）；
/// ③卡牌名单载荷（卡牌选择器〔名单形态〕）——定义级标识＋呈现要素（≥可读名称）。
/// 构造期校验（在所属请求对象构造时执行，fail-fast）：绑定槽位名须为声明槽位；绑定类型须与槽位类别/形态匹配；
/// 手牌槽位缺判定面或允许集＝拒绝；卡牌选择器〔名单形态〕缺载荷或载荷为空＝拒绝（构造期错误）。
/// 取值面（框架内部）：快照防御性复制（设置时复制、读取返回只读快照）。
/// </summary>
public sealed class TargetingRequestContext
{
    private readonly Dictionary<string, IReadOnlyList<Ref<Entity>>> _referencesBySlot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Ref<Entity>, bool>> _validatorsBySlot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CardListing>> _listingsBySlot = new(StringComparer.Ordinal);

    /// <summary>创建空请求上下文（按需经 With 系列方法绑定槽位数据）。</summary>
    public TargetingRequestContext()
    {
    }

    /// <summary>
    /// 绑定槽位允许集（引用类候选快照）——适用于手牌选择槽位与卡牌选择器〔引用集形态〕。
    /// 快照语义：设置时复制；请求期固定（此后对源集合的修改不影响本上下文）。
    /// </summary>
    /// <param name="slotName">槽位名（须与声明槽位名一致；归一口径同槽位〔省略/空白＝缺省名〕）。</param>
    /// <param name="references">允许集（引用集合；可为空集——空集＝执行时失败〔不进交互〕；不可含 null 元素）。</param>
    /// <returns>本上下文（链式绑定）。</returns>
    /// <exception cref="ArgumentNullException">references 为 null。</exception>
    /// <exception cref="ArgumentException">slotName 为 null/空白；含 null 元素；该槽位已绑定引用集（重复绑定被拒绝）。</exception>
    public TargetingRequestContext WithSlotReferences(string slotName, IEnumerable<Ref<Entity>> references)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(references);

        var list = new List<Ref<Entity>>();
        foreach (var reference in references)
        {
            if (reference is null)
            {
                throw new ArgumentException(
                    $"槽位 '{slotName}' 的引用集含 null 元素（构造期拒绝）。", nameof(references));
            }

            list.Add(reference);
        }

        if (_referencesBySlot.ContainsKey(slotName))
        {
            throw new ArgumentException($"槽位 '{slotName}' 的引用集已绑定（重复绑定被拒绝）。", nameof(slotName));
        }

        _referencesBySlot.Add(slotName, list.ToArray());
        return this;
    }

    /// <summary>
    /// 绑定槽位域判定面（引用类槽位通用、可选；手牌选择槽位强制要求）——终局校验时对每个提交项同步调用。
    /// 判定为纯查询（不得有持久副作用）；返回 false＝确定不合规（拒绝路径）；抛异常＝失败结局。
    /// </summary>
    /// <param name="slotName">槽位名（须与声明槽位名一致）。</param>
    /// <param name="validator">判定面（参数＝提交的引用；返回是否仍合规，如"仍在手牌"）。</param>
    /// <returns>本上下文（链式绑定）。</returns>
    /// <exception cref="ArgumentNullException">validator 为 null。</exception>
    /// <exception cref="ArgumentException">slotName 为 null/空白；该槽位已绑定判定面（重复绑定被拒绝）。</exception>
    public TargetingRequestContext WithSlotDomainValidator(string slotName, Func<Ref<Entity>, bool> validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(validator);

        if (_validatorsBySlot.ContainsKey(slotName))
        {
            throw new ArgumentException($"槽位 '{slotName}' 的域判定面已绑定（重复绑定被拒绝）。", nameof(slotName));
        }

        _validatorsBySlot.Add(slotName, validator);
        return this;
    }

    /// <summary>
    /// 绑定槽位卡牌名单载荷（卡牌选择器〔名单形态〕）；随请求描述交付前端（屏中卡牌阵列要素）。
    /// 名单为空＝构造期错误（在所属请求对象构造时拒绝——fail-fast）。
    /// </summary>
    /// <param name="slotName">槽位名（须与声明槽位名一致）。</param>
    /// <param name="listings">名单条目（定义级标识＋可读名称；不可含 null 元素）。</param>
    /// <returns>本上下文（链式绑定）。</returns>
    /// <exception cref="ArgumentNullException">listings 为 null。</exception>
    /// <exception cref="ArgumentException">slotName 为 null/空白；含 null 条目；该槽位已绑定名单（重复绑定被拒绝）。</exception>
    public TargetingRequestContext WithSlotListings(string slotName, IEnumerable<CardListing> listings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        ArgumentNullException.ThrowIfNull(listings);

        var list = new List<CardListing>();
        foreach (var listing in listings)
        {
            if (listing is null)
            {
                throw new ArgumentException(
                    $"槽位 '{slotName}' 的卡牌名单含 null 条目（构造期拒绝）。", nameof(listings));
            }

            list.Add(listing);
        }

        if (_listingsBySlot.ContainsKey(slotName))
        {
            throw new ArgumentException($"槽位 '{slotName}' 的卡牌名单已绑定（重复绑定被拒绝）。", nameof(slotName));
        }

        _listingsBySlot.Add(slotName, list.ToArray());
        return this;
    }

    // ---------- 框架内部读面 ----------

    /// <summary>已绑定的槽位名（三类绑定的并集；构造期校验用）。</summary>
    internal IEnumerable<string> BoundSlotNames
        => _referencesBySlot.Keys.Concat(_validatorsBySlot.Keys).Concat(_listingsBySlot.Keys).Distinct();

    /// <summary>是否绑定了槽位引用集。</summary>
    internal bool HasReferences(string slotName) => _referencesBySlot.ContainsKey(slotName);

    /// <summary>是否绑定了槽位域判定面。</summary>
    internal bool HasDomainValidator(string slotName) => _validatorsBySlot.ContainsKey(slotName);

    /// <summary>是否绑定了槽位卡牌名单。</summary>
    internal bool HasListings(string slotName) => _listingsBySlot.ContainsKey(slotName);

    /// <summary>尝试读取槽位引用集（只读快照；未绑定＝false 且返回空集）。</summary>
    internal bool TryGetReferences(string slotName, out IReadOnlyList<Ref<Entity>> references)
    {
        if (_referencesBySlot.TryGetValue(slotName, out var value))
        {
            references = value;
            return true;
        }

        references = Array.Empty<Ref<Entity>>();
        return false;
    }

    /// <summary>尝试读取槽位域判定面（未绑定＝false）。</summary>
    internal bool TryGetDomainValidator(string slotName, out Func<Ref<Entity>, bool> validator)
    {
        if (_validatorsBySlot.TryGetValue(slotName, out var value))
        {
            validator = value;
            return true;
        }

        validator = static _ => true;
        return false;
    }

    /// <summary>尝试读取槽位卡牌名单（只读快照；未绑定＝false 且返回空集）。</summary>
    internal bool TryGetListings(string slotName, out IReadOnlyList<CardListing> listings)
    {
        if (_listingsBySlot.TryGetValue(slotName, out var value))
        {
            listings = value;
            return true;
        }

        listings = Array.Empty<CardListing>();
        return false;
    }
}
