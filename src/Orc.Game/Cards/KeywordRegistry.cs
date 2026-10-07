using Orc.Cards;

namespace Orc.Game.Cards;

/// <summary>
/// 词条注册面（2C-A1；进程级全局）：登记「词条标识 → 组件工厂（卡实例＋参值 → 词条组件）」。
/// 作用域与契约（实现 grill 第 2 批·场 1 第 5 轮裁定）：
/// 【进程级全局】生产内建十七枚（闪击/奋战/烟幕/伏击＋被压制/被抑制/动员/钳击/预报/免疫/重甲/情报/
/// 无法被压制/无法被抑制——A2 扩展；亡计——A4 扩展；老兵——S1 扩展；隐蔽——S2 扩展）经本面装配（取代硬编码工厂 switch）；开放扩展注册
/// （测试/后续批次可注册自定义标识）。
/// 【注册先于使用】为使用者义务：定义声明的合法性校验（<see cref="CardDefinition"/>）与加载期组件构造
/// （<see cref="KeywordManager"/>）均以本面内容为唯一来源（单源）；注册须发生在定义构造/加载装配之前。
/// 【失败模式】重复注册同标识＝拒绝（注册即配置——与效果工厂注册表先例一致；汇报口径）；未注册标识的
/// 组件构造＝fail-fast；工厂返回 null / 标识与注册键不一致＝配置错误 fail-fast。
/// 【线程安全】以内部锁保护全部读写（装配期注册为主；如需运行时注册/注销，操作线程安全、即刻可见）。
/// 【测试隔离】测试以唯一命名隔离为主；<see cref="Unregister"/> 为测试清理/装配维护的可选注销面
/// （注销生产内建属误用——后续定义校验/装载将 fail-fast）。
/// 【对战词条分类（A2 加性）】注册时以 <c>isBattleKeyword</c> 声明打标（标识级分类——非实例级）；
/// 打标全集＝「已实现且打标」（池边界）——供「获得 1 个随机对战词条」类消费由调用方构建池
/// （<see cref="BattleKeywordUniverse"/>＋取样＋授予组合；筛选/计数读取面见 <see cref="BattleKeywordRules"/>）。
/// 打标清单（Q&A-2，B 站 Wiki 2025-12 版判定）：闪击/奋战/烟幕/伏击/重甲X（本批 5 项）；
/// 「守护」「冲击」属标注全集但不在本批实现范围——不入批内池、留后续批次。
/// </summary>
public static class KeywordRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Func<Card, int?, KeywordComponent>> Factories = new(StringComparer.Ordinal);
    private static readonly List<string> Order = new();
    private static readonly HashSet<string> BattleKeywords = new(StringComparer.Ordinal);

    static KeywordRegistry()
    {
        // 生产内建十七枚（经注册面装配——合法标识集来源＝本面内容；S2 起含隐蔽——「十六枚」旧注释随注册面同步）。
        // 既有四枚（2C-A1 迁移）——均属对战词条（打标）。
        Register(KeywordIds.Blitz, (_, _) => new BlitzKeywordComponent(), isBattleKeyword: true);
        Register(KeywordIds.Fury, (_, _) => new FuryKeywordComponent(), isBattleKeyword: true);
        Register(KeywordIds.SmokeScreen, (_, _) => new PlainKeywordComponent(KeywordIds.SmokeScreen), isBattleKeyword: true);
        Register(KeywordIds.Ambush, (_, _) => new AmbushKeywordComponent(), isBattleKeyword: true);

        // A2 新增十枚——重甲为对战词条（打标）；其余不打标（wiki 无「属于对战词条」标注）。
        Register(KeywordIds.Suppressed, (_, value) => new SuppressedKeywordComponent(value));
        Register(KeywordIds.Inhibited, (_, _) => new PlainKeywordComponent(KeywordIds.Inhibited));
        Register(KeywordIds.Mobilize, (_, _) => new MobilizeKeywordComponent());
        Register(KeywordIds.Pincer, (_, _) => new PincerKeywordComponent());
        Register(KeywordIds.Forecast, (_, _) => new PlainKeywordComponent(KeywordIds.Forecast));
        Register(KeywordIds.Immune, (_, _) => new ImmuneKeywordComponent());
        Register(KeywordIds.Armor, (_, value) => new ArmorKeywordComponent(value), isBattleKeyword: true);
        Register(KeywordIds.Intelligence, (_, value) => new IntelligenceKeywordComponent(value));
        Register(KeywordIds.CannotBeSuppressed, (_, _) => new PlainKeywordComponent(KeywordIds.CannotBeSuppressed));
        Register(KeywordIds.CannotBeInhibited, (_, _) => new PlainKeywordComponent(KeywordIds.CannotBeInhibited));

        // A4 新增一枚——亡计（内容型；不打对战词条标——Q&A-6 第 4 点；内容经运行时授予通道的「内容装载点」注入）。
        Register(KeywordIds.Deathrattle, (_, _) => new DeathrattleKeywordComponent());

        // S1 新增一枚——老兵（标记型；无行为面；不打对战词条标——纯内容标记，读取面经 VeteranRules.IsVeteran）。
        Register(KeywordIds.Veteran, (_, _) => new PlainKeywordComponent(KeywordIds.Veteran));

        // S2 新增一枚——隐蔽（标记型；无行为面；不打对战词条标——纯内容标记，读取面经 CovertRules.IsCovert；
        // 豁免剔除经判定器默认规则/剔除点承载、揭示经 CovertRules.RevealAsync 承载）。
        Register(KeywordIds.Covert, (_, _) => new PlainKeywordComponent(KeywordIds.Covert));
    }

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

    /// <summary>注销词条（测试清理/装配维护用；未注册＝false 无操作、不抛错）。注销生产内建属误用（定义校验/装载 fail-fast）。</summary>
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
            return true;
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
