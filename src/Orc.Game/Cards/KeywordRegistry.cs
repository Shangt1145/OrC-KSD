namespace Orc.Game.Cards;

/// <summary>
/// 词条注册面（2C-A1；进程级全局）：登记「词条标识 → 组件工厂（卡实例＋参值 → 词条组件）」。
/// 作用域与契约（实现 grill 第 2 批·场 1 第 5 轮裁定）：
/// 【进程级全局】生产内建四枚（闪击/奋战/烟幕/伏击）经本面装配（取代硬编码工厂 switch）；开放扩展注册
/// （测试/后续批次可注册自定义标识——如「重甲」）。
/// 【注册先于使用】为使用者义务：定义声明的合法性校验（<see cref="CardDefinition"/>）与加载期组件构造
/// （<see cref="KeywordManager"/>）均以本面内容为唯一来源（单源）；注册须发生在定义构造/加载装配之前。
/// 【失败模式】重复注册同标识＝拒绝（注册即配置——与效果工厂注册表先例一致；汇报口径）；未注册标识的
/// 组件构造＝fail-fast；工厂返回 null / 标识与注册键不一致＝配置错误 fail-fast。
/// 【线程安全】以内部锁保护全部读写（装配期注册为主；如需运行时注册/注销，操作线程安全、即刻可见）。
/// 【测试隔离】测试以唯一命名隔离为主；<see cref="Unregister"/> 为测试清理/装配维护的可选注销面
/// （注销生产内建属误用——后续定义校验/装载将 fail-fast）。
/// </summary>
public static class KeywordRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Func<CardBase, int?, KeywordComponent>> Factories = new(StringComparer.Ordinal);
    private static readonly List<string> Order = new();

    static KeywordRegistry()
    {
        // 生产内建四枚（经注册面装配——合法标识集来源＝本面内容）。
        Register(KeywordIds.Blitz, (_, _) => new BlitzKeywordComponent());
        Register(KeywordIds.Fury, (_, _) => new FuryKeywordComponent());
        Register(KeywordIds.SmokeScreen, (_, _) => new PlainKeywordComponent(KeywordIds.SmokeScreen));
        Register(KeywordIds.Ambush, (_, _) => new AmbushKeywordComponent());
    }

    /// <summary>注册词条（标识 → 组件工厂；工厂每次创建全新组件实例——加载装配/每次授予各自持数据与效果实例）。</summary>
    /// <exception cref="ArgumentException">keyword 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">factory 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一标识重复注册（被拒绝——注册即配置）。</exception>
    public static void Register(string keyword, Func<CardBase, int?, KeywordComponent> factory)
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
    /// </summary>
    /// <exception cref="InvalidOperationException">标识未注册、工厂返回 null 或返回组件的标识与注册键不一致（配置错误）。</exception>
    internal static KeywordComponent Create(CardBase card, string keyword, int? value)
    {
        Func<CardBase, int?, KeywordComponent> factory;
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
