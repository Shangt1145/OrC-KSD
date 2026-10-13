using Orc.Cards;

namespace Orc.Game.Cards;

/// <summary>
/// 词条注册面（2C-A1；进程级全局）：登记「词条标识 → 组件工厂（卡实例＋参值 → 词条组件）」。
/// 作用域与契约（实现 grill 第 2 批·场 1 第 5 轮裁定）：
/// 【进程级全局】生产内建十九枚（闪击/奋战/烟幕/伏击＋被压制/被抑制/动员/钳击/预报/免疫/重甲/情报/
/// 无法被压制/无法被抑制——A2 扩展；亡计——A4 扩展；老兵——S1 扩展；隐蔽——S2 扩展；守护——批 4 扩展；冲击——批 5 扩展）经本面装配（取代硬编码工厂 switch）；开放扩展注册
/// （测试/后续批次可注册自定义标识）。
/// 【注册先于使用】为使用者义务：定义声明的合法性校验（<see cref="CardDefinition"/>）与加载期组件构造
/// （<see cref="KeywordManager"/>）均以本面内容为唯一来源（单源）；注册须发生在定义构造/加载装配之前。
/// 【失败模式】重复注册同标识＝拒绝（注册即配置——与效果工厂注册表先例一致；汇报口径）；未注册标识的
/// 组件构造＝fail-fast；工厂返回 null / 标识与注册键不一致＝配置错误 fail-fast。
/// 【线程安全】以内部锁保护全部读写（装配期注册为主；如需运行时注册/注销，操作线程安全、即刻可见）。
/// 【测试隔离】测试以唯一命名隔离为主；<see cref="Unregister"/> 为测试清理/装配维护的可选注销面
/// （注销生产内建属误用——后续定义校验/装载将 fail-fast）。
/// 【词条效果绑定（批 4 加性·数据化）】词条 → 数据壳效果清单的声明面（<see cref="DeclareEffectBindings"/>）：
/// 装载期实例化（<see cref="KeywordManager"/> 授予链的装载期实例化点）与装配期核验
/// （<see cref="KeywordEffectAssembly"/>）的查询源；生产内建绑定于本面静态构造声明（试点：闪击/动员）；
/// 绑定随 <see cref="Unregister"/> 一并清理。键常量登记处＝<see cref="KeywordEffectAssembly"/>。
/// 【对战词条分类（A2 加性）】注册时以 <c>isBattleKeyword</c> 声明打标（标识级分类——非实例级）；
/// 打标全集＝「已实现且打标」（池边界）——供「获得 1 个随机对战词条」类消费由调用方构建池
/// （<see cref="BattleKeywordUniverse"/>＋取样＋授予组合；筛选/计数读取面见 <see cref="BattleKeywordRules"/>）。
/// 打标清单（Q&A-2，B 站 Wiki 2025-12 版判定）：闪击/奋战/烟幕/伏击/重甲X（A2 批 5 项）＋守护（批 4——wiki『属于对战词条』）
/// ＋冲击（批 5——wiki『属于对战词条』）。
/// </summary>
public static class KeywordRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Func<Card, int?, KeywordComponent>> Factories = new(StringComparer.Ordinal);
    private static readonly List<string> Order = new();
    private static readonly HashSet<string> BattleKeywords = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReadOnlyList<KeywordEffectBinding>> EffectBindings = new(StringComparer.Ordinal);

    // 资产化 批次 2：生产内建十九枚的**注册内容已迁出**——见 `Orc.Plugins.BuiltIn.BuiltInPlugin.RegisterProcessDefaults()`
    // （内置插件在程序集加载时经本注册面装配；本面＝纯注册机制，不含内置内容）。
    // 合法标识集来源仍＝本面内容；S2 起含隐蔽、批 4 起含守护、批 5 起含冲击（原「十八枚」旧口径）。

    /// <summary>
    /// 注册词条（标识 → 组件工厂；工厂每次创建全新组件实例——加载装配/每次授予各自持数据与效果实例）。
    /// </summary>
    /// <param name="keyword">词条标识。</param>
    /// <param name="factory">组件工厂（参数＝宿主卡引用＋可选参值）。</param>
    /// <param name="isBattleKeyword">对战词条打标（标识级分类；默认 false——A2 加性）。</param>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">factory 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一标识重复注册（被拒绝——注册即配置）。</exception>
    public static void Register(string keyword, Func<Card, int?, KeywordComponent> factory, bool isBattleKeyword = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            if (Factories.ContainsKey(keyword))
            {
                throw new InvalidOperationException($"词条标识 '{keyword}' 已注册（重复注册被拒绝——注册即配置）。");
            }

            Factories.Add(keyword, factory);
            Order.Add(keyword);
            if (isBattleKeyword)
            {
                BattleKeywords.Add(keyword);
            }
        }
    }

    /// <summary>
    /// **注册或覆盖**词条（前端资产迁移 S5·D5：运行期**只允许新注册与覆盖、不允许注销**）：
    /// 已存在＝**替换工厂与打标**，并**清空该词条的效果绑定**（调用方随后重新 <see cref="DeclareEffectBindings"/>）。
    /// 覆盖只影响**引擎持有的引用**——已冻结快照的对局（见 <see cref="Snapshot"/>）不受影响。
    /// </summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">factory 为 null。</exception>
    public static void RegisterOrReplace(string keyword, Func<Card, int?, KeywordComponent> factory, bool isBattleKeyword = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentNullException.ThrowIfNull(factory);

        lock (Gate)
        {
            if (!Factories.ContainsKey(keyword))
            {
                Order.Add(keyword);
            }

            Factories[keyword] = factory;
            if (isBattleKeyword)
            {
                BattleKeywords.Add(keyword);
            }
            else
            {
                BattleKeywords.Remove(keyword);
            }

            // 覆盖＝旧绑定作废（同标识重建行为须重新声明；否则旧效果会挂到新实现上）。
            EffectBindings.Remove(keyword);
        }
    }

    /// <summary>
    /// **冻结词条快照**（S5·D5）：对局在装配开始时取一次，此后授予链/装载期实例化/效果绑定查询一律读该快照
    /// ⇒ 运行期"覆盖"只作用于**引擎持有引用**，**进行中的对局不受影响**（新快照只影响之后新建的对局）。
    /// </summary>
    public static KeywordSnapshot Snapshot()
    {
        lock (Gate)
        {
            return new KeywordSnapshot(Factories, BattleKeywords, EffectBindings);
        }
    }

    /// <summary>注销词条（**仅装配维护/测试清理**用；S5·D5 起**不对运行时开放**——运行期只允许
    /// <see cref="Register"/>／<see cref="RegisterOrReplace"/>，需要真注销＝冷重启进程）。
    /// 未注册＝false 无操作、不抛错。批 4：效果绑定随注销一并清理。</summary>
    public static bool Unregister(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        lock (Gate)
        {
            if (!Factories.Remove(keyword))
            {
                return false;
            }

            Order.Remove(keyword);
            BattleKeywords.Remove(keyword);
            EffectBindings.Remove(keyword); // 批 4：绑定声明随词条注销清理（测试隔离/装配维护）
            return true;
        }
    }

    /// <summary>
    /// 声明词条效果绑定（批 4 加性·数据化；词条 → 数据壳效果清单——登记一次性、重复拒绝）：
    /// 装载期实例化（授予链装载期实例化点）与装配期核验（<see cref="KeywordEffectAssembly"/>）的查询源。
    /// 「注册先于绑定」：词条须已注册（未注册＝明确拒绝）；声明内 PrefabId / EffectName 校验（非空白、两两不重复）。
    /// 绑定只影响声明之后的装载（已装载实例不追溯）；注销（<see cref="Unregister"/>）随词条一并清理。
    /// </summary>
    /// <param name="keyword">词条标识（须已注册）。</param>
    /// <param name="bindings">数据壳效果清单（声明序＝装载序；至少一项）。</param>
    /// <exception cref="ArgumentException">keyword 为 null/空白；bindings 为空集；项 PrefabId / EffectName 空白或重复。</exception>
    /// <exception cref="ArgumentNullException">bindings 或其项为 null。</exception>
    /// <exception cref="InvalidOperationException">词条未注册（注册先于绑定）；或同词条重复声明（拒绝）。</exception>
    public static void DeclareEffectBindings(string keyword, IEnumerable<KeywordEffectBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentNullException.ThrowIfNull(bindings);

        var list = new List<KeywordEffectBinding>();
        var prefabIds = new HashSet<string>(StringComparer.Ordinal);
        var effectNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(bindings), "绑定清单不能包含 null 元素。");
            }

            if (string.IsNullOrWhiteSpace(binding.PrefabId))
            {
                throw new ArgumentException("绑定 PrefabId 不能为空白。", nameof(bindings));
            }

            if (string.IsNullOrWhiteSpace(binding.EffectName))
            {
                throw new ArgumentException("绑定 EffectName 不能为空白。", nameof(bindings));
            }

            if (!prefabIds.Add(binding.PrefabId))
            {
                throw new ArgumentException($"绑定清单含重复 PrefabId '{binding.PrefabId}'。", nameof(bindings));
            }

            if (!effectNames.Add(binding.EffectName))
            {
                throw new ArgumentException($"绑定清单含重复 EffectName '{binding.EffectName}'。", nameof(bindings));
            }

            list.Add(binding);
        }

        if (list.Count == 0)
        {
            throw new ArgumentException("绑定清单至少须一项（空集拒绝——声明意图不明确）。", nameof(bindings));
        }

        lock (Gate)
        {
            if (!Factories.ContainsKey(keyword))
            {
                throw new InvalidOperationException(
                    $"词条标识 '{keyword}' 未注册（注册先于绑定——绑定声明被拒绝）。");
            }

            if (!EffectBindings.TryAdd(keyword, list))
            {
                throw new InvalidOperationException(
                    $"词条标识 '{keyword}' 的效果绑定已声明（重复声明被拒绝——注册即配置）。");
            }
        }
    }

    /// <summary>
    /// 读取词条效果绑定（装载期实例化查询源；未声明 / null / 空白＝空列表——不抛错、快速路径）。
    /// </summary>
    internal static IReadOnlyList<KeywordEffectBinding> GetEffectBindings(string keyword)
        => !string.IsNullOrWhiteSpace(keyword)
            && EffectBindings.TryGetValue(keyword, out var bindings)
                ? bindings
                : Array.Empty<KeywordEffectBinding>();

    /// <summary>
    /// 枚举全部词条效果绑定（装配期核验查询源；登记序快照——词条 + 绑定逐项）。
    /// </summary>
    internal static IReadOnlyList<(string Keyword, KeywordEffectBinding Binding)> EnumerateEffectBindings()
    {
        lock (Gate)
        {
            var result = new List<(string, KeywordEffectBinding)>();
            foreach (var (keyword, bindings) in EffectBindings)
            {
                foreach (var binding in bindings)
                {
                    result.Add((keyword, binding));
                }
            }

            return result;
        }
    }

    /// <summary>标识是否已注册（定义期合法性校验依据；null/空白＝false、不抛错）。</summary>
    public static bool IsDefined(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        lock (Gate)
        {
            return Factories.ContainsKey(keyword);
        }
    }

    /// <summary>
    /// 标识是否为对战词条（打标查询；A2 加性——「获得 1 个随机对战词条」类池构建/筛选的判定源；
    /// null/空白/未注册＝false、不抛错）。
    /// </summary>
    public static bool IsBattleKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        lock (Gate)
        {
            return BattleKeywords.Contains(keyword);
        }
    }

    /// <summary>
    /// 对战词条打标全集快照（登记序；＝「已实现且打标」池边界——调用方据此构建池）。
    /// </summary>
    public static IReadOnlyList<string> BattleKeywordUniverse
    {
        get
        {
            lock (Gate)
            {
                return Order.Where(BattleKeywords.Contains).ToArray();
            }
        }
    }

    /// <summary>已注册标识清单快照（登记序；错误信息/诊断用）。</summary>
    public static IReadOnlyList<string> Registered
    {
        get
        {
            lock (Gate)
            {
                return Order.ToArray();
            }
        }
    }

    /// <summary>
    /// 按标识创建词条组件（加载期装配/授予链的单源入口）：构造失败（未注册/工厂返回 null/标识不一致）＝fail-fast。
    /// 宿主＝引擎薄容器 <see cref="Card"/>（A2 泛化：单位与 HQ 同族承载）。
    /// </summary>
    /// <exception cref="InvalidOperationException">标识未注册、工厂返回 null 或返回组件的标识与注册键不一致（配置错误）。</exception>
    internal static KeywordComponent Create(Card card, string keyword, int? value)
    {
        Func<Card, int?, KeywordComponent> factory;
        lock (Gate)
        {
            if (!Factories.TryGetValue(keyword, out var registered))
            {
                throw new InvalidOperationException(
                    $"词条标识 '{keyword}' 未注册（注册面为唯一来源——注册先于使用；fail-fast）。");
            }

            factory = registered;
        }

        var component = factory(card, value)
            ?? throw new InvalidOperationException($"词条标识 '{keyword}' 的工厂返回 null（配置错误——fail-fast）。");

        if (!string.Equals(component.Keyword, keyword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"词条标识 '{keyword}' 的工厂返回了标识为 '{component.Keyword}' 的组件（标识不一致——配置错误、fail-fast）。");
        }

        return component;
    }
}

/// <summary>
/// 词条效果绑定项（批 4 数据化；词条 → 数据壳效果的关联声明——<see cref="KeywordRegistry.DeclareEffectBindings"/>）：
/// <see cref="PrefabId"/>＝词条效果库中的效果预制体标识（库键）；<see cref="EffectName"/>＝实例化效果名
/// （卡容器效果检索面契约——「效果名契约」保持项）。
/// </summary>
/// <param name="PrefabId">效果预制体标识（库键）。</param>
/// <param name="EffectName">效果名（实例化时随数据壳装载）。</param>
public sealed record KeywordEffectBinding(string PrefabId, string EffectName);

/// <summary>
/// 词条注册面的**冻结快照**（S5·D5）：对局在装配开始时冻结一次，装载/授予链只读本快照
/// ⇒ 运行期的"覆盖"不会作用到已开局的对局（与 <see cref="KeywordRegistry"/> 的引擎持有引用隔离）。
/// </summary>
public sealed class KeywordSnapshot
{
    private readonly Dictionary<string, Func<Card, int?, KeywordComponent>> _factories;
    private readonly HashSet<string> _battleKeywords;
    private readonly Dictionary<string, IReadOnlyList<KeywordEffectBinding>> _bindings;
    private readonly string[] _order;

    internal KeywordSnapshot(
        Dictionary<string, Func<Card, int?, KeywordComponent>> factories,
        HashSet<string> battleKeywords,
        Dictionary<string, IReadOnlyList<KeywordEffectBinding>> bindings)
    {
        _factories = new Dictionary<string, Func<Card, int?, KeywordComponent>>(factories, StringComparer.Ordinal);
        _battleKeywords = new HashSet<string>(battleKeywords, StringComparer.Ordinal);
        _bindings = new Dictionary<string, IReadOnlyList<KeywordEffectBinding>>(bindings, StringComparer.Ordinal);
        _order = [.. factories.Keys];
    }

    /// <summary>冻结时点的已注册标识清单（登记序近似——字典序展开，仅诊断用）。</summary>
    public IReadOnlyList<string> Registered => _order;

    /// <summary>标识是否在快照内注册。</summary>
    public bool IsDefined(string keyword)
        => !string.IsNullOrWhiteSpace(keyword) && _factories.ContainsKey(keyword);

    /// <summary>标识是否为对战词条（快照时点的打标）。</summary>
    public bool IsBattleKeyword(string keyword)
        => !string.IsNullOrWhiteSpace(keyword) && _battleKeywords.Contains(keyword);

    /// <summary>按快照构造词条组件（未注册/工厂返回 null/标识不一致＝fail-fast，同静态面口径）。</summary>
    internal KeywordComponent Create(Card card, string keyword, int? value)
    {
        if (!_factories.TryGetValue(keyword, out var factory))
        {
            throw new InvalidOperationException(
                $"词条标识 '{keyword}' 不在本局词条快照内（快照为唯一来源——fail-fast）。");
        }

        var component = factory(card, value)
            ?? throw new InvalidOperationException($"词条标识 '{keyword}' 的工厂返回 null（配置错误——fail-fast）。");
        if (!string.Equals(component.Keyword, keyword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"词条标识 '{keyword}' 的工厂返回了标识为 '{component.Keyword}' 的组件（标识不一致——配置错误、fail-fast）。");
        }

        return component;
    }

    /// <summary>快照时点的词条效果绑定（未声明＝空）。</summary>
    internal IReadOnlyList<KeywordEffectBinding> GetEffectBindings(string keyword)
        => !string.IsNullOrWhiteSpace(keyword) && _bindings.TryGetValue(keyword, out var bindings)
            ? bindings
            : Array.Empty<KeywordEffectBinding>();
}
