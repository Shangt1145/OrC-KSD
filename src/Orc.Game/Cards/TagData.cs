namespace Orc.Game.Cards;

/// <summary>
/// 国籍 / 阵营标识（**字符串常量类**；资产化 S4：由 <c>enum</c> 改为 string 常量——真源＝资产服务，
/// 本类＝内置项可读符号；标识字面＝对外数据契约基线）。
/// 成员清单与来源数据（docs/kards官方卡牌.json）faction 字段一致（规范 id 为小写）——germany / soviet / usa / britain / japan /
/// france / italy / poland / finland / anzac / neutral；五大主国（德国/苏联/美国/英国/日本）在内。
/// 用途：卡牌定义的必填槽位值域（<see cref="CardDefinition.Faction"/>）；「主国牌」「日本精英空军」式组合筛选的维度之一。
/// S10 起：作为卡牌实例「国籍」的读取面承载于【阵营〔国籍〕＋部署费】合并组件（<see cref="FactionCostData"/>）——
/// 不再经 <see cref="TagData"/> 承载（国籍槽位自 TagData 移出；单一真源）。
/// </summary>
public static class Faction
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,bool> Custom=new(StringComparer.Ordinal);
    /// <summary>Registers a namespaced content faction without changing the built-in constants.</summary>
    public static void RegisterCustom(string id,bool major=true)
    {
        if(string.IsNullOrWhiteSpace(id)||id!=id.ToLowerInvariant()||!id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-')||All.Contains(id))
            throw new ArgumentException("Custom faction requires a distinct canonical id.",nameof(id));
        Custom[id]=major;
    }
    public static bool IsCustomMajor(string id)=>Custom.TryGetValue(id,out var major)&&major;
    /// <summary>德国。</summary>
    public const string Germany = "germany";

    /// <summary>苏联。</summary>
    public const string Soviet = "soviet";

    /// <summary>美国。</summary>
    public const string USA = "usa";

    /// <summary>英国。</summary>
    public const string Britain = "britain";

    /// <summary>日本。</summary>
    public const string Japan = "japan";

    /// <summary>法国。</summary>
    public const string France = "france";

    /// <summary>意大利。</summary>
    public const string Italy = "italy";

    /// <summary>波兰。</summary>
    public const string Poland = "poland";

    /// <summary>芬兰。</summary>
    public const string Finland = "finland";

    /// <summary>澳新联军（Anzac；来源数据的合法阵营归属）。</summary>
    public const string Anzac = "anzac";

    /// <summary>中立（来源数据的合法阵营归属）。</summary>
    public const string Neutral = "neutral";

    /// <summary>内置阵营标识清单（登记序＝来源数据序）。</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Germany, Soviet, USA, Britain, Japan, France, Italy, Poland, Finland, Anzac, Neutral,
    };

    /// <summary>是否为已定义阵营标识（null/空白＝false、不抛错）。</summary>
    public static bool IsDefined(string? value)
        => value is not null && (All.Contains(value, StringComparer.Ordinal)||Custom.ContainsKey(value));

    /// <summary>
    /// 归一到规范阵营标识（大小写不敏感——官方语料用 PascalCase 如 <c>Germany</c>，规范 id 为小写
    /// <c>germany</c>；**映射表＝大小写归一 ＋ 定义校验**，无特殊映射项）；未定义＝false。
    /// </summary>
    public static bool TryNormalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.Trim().ToLowerInvariant();
        if (!IsDefined(lowered))
        {
            return false;
        }

        canonical = lowered;
        return true;
    }
}

/// <summary>
/// 稀有度标识（**字符串常量类**；资产化 批次 4：由 <c>enum</c> 改为 string 常量；真源＝资产服务，本类＝内置符号）。
/// 中文语义对照：基础＝Standard、限定＝Limited、特殊＝Special、精英＝Elite；
/// 与来源数据 rarity 字段四值一一对应（规范 id 为小写）。用途：卡牌定义的必填槽位值域（<see cref="CardDefinition.Rarity"/>）。
/// </summary>
public static class Rarity
{
    /// <summary>基础（Standard）。</summary>
    public const string Standard = "standard";

    /// <summary>限定（Limited）。</summary>
    public const string Limited = "limited";

    /// <summary>特殊（Special）。</summary>
    public const string Special = "special";

    /// <summary>精英（Elite）。</summary>
    public const string Elite = "elite";

    /// <summary>内置稀有度标识清单（登记序）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Standard, Limited, Special, Elite };

    /// <summary>是否为已定义稀有度标识（null/空白＝false、不抛错）。</summary>
    public static bool IsDefined(string? value)
        => value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>归一到规范标识（大小写不敏感——官方语料用 PascalCase，规范 id 为小写）；未定义＝false。</summary>
    public static bool TryNormalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lowered = value.Trim().ToLowerInvariant();
        if (!All.Contains(lowered, StringComparer.Ordinal))
        {
            return false;
        }

        canonical = lowered;
        return true;
    }
}

/// <summary>
/// 强类型槽位（W1-1 G12；泛型约束形态＝<c>where TTag : struct, Enum</c>）：以泛型参数承载槽位值域，
/// 不同槽位（如国籍 / 稀有度）为不同类型、彼此不可混用（强类型约束）。
/// 值恒有且只读：构造即固定（<see cref="Value"/> 无写面）——实例侧只读（本批不支持运行时修改）。
/// fail-fast：未定义枚举值在构造期被拒绝（沿用既有 Enum 校验风格）。
/// S10 起：本类型由 <see cref="TagData"/> 的稀有度槽位使用（国籍槽位自 TagData 移出——国籍承载于
/// <see cref="FactionCostData"/> 合并组件、经受控写面可修改，不再经只读槽位承载）。
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
/// ① 必填稀有度（<see cref="Rarity"/> 规范标识）——恒有值且实例侧只读
///    （随定义声明固定；本批不支持运行时修改）。S10 起：国籍槽位自本组件移出——国籍承载于
///    【阵营〔国籍〕＋部署费】合并组件（<see cref="FactionCostData"/>；可修改形态）；本组件字段集＝稀有度 ＋ 开放 tag。
///    资产化 批次 4：稀有度由枚举槽位改为 string。
/// ② 开放 tag 集合（子类别：海军 / T-34 / 谢尔曼等）：平等的一组开放 tag 值（单一「子类别」性质集合、不另立命名维度）；
///    完全开放值域（任意非空白字符串可增删、无受控清单）；支持运行时增删（作用于目标卡牌实例、不影响定义与同定义的其它实例）；
///    增删为静默数据变更（不产生任何更新 / 通知——无消费方，读取与筛选随动即观测面）。
/// 与词条边界：开放 tag 不承载机制行为；机制词条不走本组件（独立机制体系、勿混用）。
/// 未加载实例＝组件缺失、明确错误（沿用「未装配实例取件＝明确错误」先例）。
/// </summary>
public sealed class TagData
{
    private readonly List<string> _tags = new();

    /// <summary>创建标签数据（稀缺度为必填标识；开放 tag 初始为空——后续经 <see cref="AddTag"/> 登记）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">rarity 为未定义标识。</exception>
    public TagData(string rarity)
    {
        if (!Orc.Game.Cards.Rarity.IsDefined(rarity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rarity), rarity, "稀有度为未定义标识（配置错误在定义期被拒绝）。");
        }

        Rarity = rarity;
    }

    /// <summary>稀有度（必填、只读；规范标识——资产化 批次 4：由枚举槽位改为 string）。</summary>
    public string Rarity { get; }

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
