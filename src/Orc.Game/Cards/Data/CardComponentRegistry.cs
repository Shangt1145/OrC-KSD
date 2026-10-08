using System.Text.Json;

namespace Orc.Game.Cards.Data;

/// <summary>
/// 组件写出委托（X1 加性·写方向对称载体）：把组件定义实例的**字段内容**写进 JSON 对象——
/// <c>component</c> 类型名由序列化器按注册名单点注入（单一真源，防双写漂移）；
/// 警告通道（<paramref name="warnings"/>）＝义务：发现不可还原项（如无反向映射的词条）必须逐条上报、不得静默；
/// 执行抛错（整体性非法除外）＝该组件隔离（警告＋跳过、不阻断其余组件）。
/// 读方向对称载体＝<see cref="CardComponentEntry.Reader"/>（静态 <c>Read</c> ↔ 静态 <c>Write</c>）。
/// </summary>
/// <typeparam name="TDefinition">组件定义类型（实现 <see cref="ICardDataComponentDefinition"/>）。</typeparam>
/// <param name="definition">组件定义实例（具体类型由注册面保证，writer 内可安全向下转型）。</param>
/// <param name="writer">JSON 写出器（只写字段内容——序列化器已把光标定位在组件对象内）。</param>
/// <param name="warnings">警告通道（不可还原项＝逐条上报；文案不含卡 id——由序列化器统一加前缀）。</param>
public delegate void CardComponentWriter<TDefinition>(
    TDefinition definition,
    Utf8JsonWriter writer,
    IList<string> warnings)
    where TDefinition : class, ICardDataComponentDefinition;

/// <summary>组件注册项（类型名 ＋ 相位 ＋ loader ＋ 反序列化入口 ＋ 写出入口；注册序＝第一段加载顺序）。</summary>
/// <param name="Name">组件类型名（数据体里的 <c>component</c> 值）。</param>
/// <param name="Phase">加载相位。</param>
/// <param name="Loader">装配动作。</param>
/// <param name="Reader">反序列化动作（由定义类型的静态 <c>Read</c> 承载）。</param>
/// <param name="IsBuiltIn">是否内置组件（内置＝加载期第一段按注册序；扩展＝第二段按数据体声明序）。</param>
/// <param name="Writer">
/// 写出动作（X1 加性·可选；缺省＝合法状态——只读/只装载组件；写出时＝隔离警告「无写出器」、
/// 跳过该组件、不阻断其余）。</param>
public sealed record CardComponentEntry(
    string Name,
    CardComponentPhase Phase,
    CardComponentLoader Loader,
    Func<JsonElement, ICardDataComponentDefinition> Reader,
    bool IsBuiltIn,
    CardComponentWriter<ICardDataComponentDefinition>? Writer = null);

/// <summary>
/// 组件 loader 注册面（P1/S2；进程级全局、静态——对齐 <c>KeywordRegistry</c> 先例）：
/// 登记「组件类型名 → （相位 ＋ loader ＋ 反序列化入口）」，**注册序即第一段加载顺序**（防组件间依赖丢失）。
/// 注册即配置：类型名空白/重复注册/loader 或 reader 为 null＝注册期明确拒绝（fail-fast、不吞）。
/// 第二段（数据体声明了但未经本面注册的自定义组件）由数据体声明序驱动——见 <c>CardBase.LoadAsync</c>。
/// </summary>
public static class CardComponentRegistry
{
    private static readonly object Gate = new();
    private static readonly List<CardComponentEntry> Order = new();
    private static readonly Dictionary<string, CardComponentEntry> ByName = new(StringComparer.Ordinal);

    /// <summary>静态构造：注册六个内置组件（注册先于使用——对齐 <c>KeywordRegistry</c> 先例）。</summary>
    static CardComponentRegistry()
    {
        Components.BuiltInCardComponents.Register();
    }

    /// <summary>
    /// 注册组件（类型名由调用方显式给出——与定义类型同处一地声明，避免反射）。
    /// </summary>
    /// <typeparam name="TDefinition">组件定义类型（实现 <see cref="ICardDataComponentDefinition"/>）。</typeparam>
    /// <param name="componentName">组件类型名（非 null/空白；数据体里的 <c>component</c> 值）。</param>
    /// <param name="phase">加载相位。</param>
    /// <param name="loader">装配动作。</param>
    /// <param name="reader">反序列化动作（定义类型的静态 <c>Read</c>）。</param>
    /// <param name="isBuiltIn">是否内置（内置＝第一段按注册序；扩展／社区＝第二段按数据体声明序）。</param>
    /// <param name="writer">写出动作（X1 加性·可选——定义类型的静态 <c>Write</c>；缺省＝合法状态，写出时隔离警告）。</param>
    /// <exception cref="ArgumentException">componentName 为 null/空白。</exception>
    /// <exception cref="ArgumentNullException">loader 或 reader 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一类型名重复注册（被拒绝——注册即配置）。</exception>
    public static void Register<TDefinition>(
        string componentName,
        CardComponentPhase phase,
        CardComponentLoader loader,
        Func<JsonElement, TDefinition> reader,
        bool isBuiltIn = false,
        CardComponentWriter<TDefinition>? writer = null)
        where TDefinition : class, ICardDataComponentDefinition
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(reader);

        CardComponentWriter<ICardDataComponentDefinition>? entryWriter = writer is null
            ? null
            : (definition, jsonWriter, warnings) => writer((TDefinition)definition, jsonWriter, warnings);

        var entry = new CardComponentEntry(
            componentName,
            phase,
            loader,
            element => reader(element),
            isBuiltIn,
            entryWriter);

        lock (Gate)
        {
            if (ByName.ContainsKey(componentName))
            {
                throw new InvalidOperationException(
                    $"组件类型名 '{componentName}' 已注册（重复注册被拒绝——注册即配置）。");
            }

            ByName.Add(componentName, entry);
            Order.Add(entry);
        }
    }

    /// <summary>注销组件（测试清理/装配维护用；未注册＝false 无操作、不抛错）。</summary>
    public static bool Unregister(string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return false;
        }

        lock (Gate)
        {
            if (!ByName.Remove(componentName))
            {
                return false;
            }

            Order.RemoveAll(entry => string.Equals(entry.Name, componentName, StringComparison.Ordinal));
            return true;
        }
    }

    /// <summary>按类型名解析注册项（未注册＝false；装载链查询面）。</summary>
    public static bool TryResolve(
        string componentName,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CardComponentEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return false;
        }

        lock (Gate)
        {
            return ByName.TryGetValue(componentName, out entry);
        }
    }

    /// <summary>是否已注册（存在性判定；null/空白＝false、不抛错）。</summary>
    public static bool IsRegistered(string componentName)
        => !string.IsNullOrWhiteSpace(componentName) && ByName.ContainsKey(componentName);

    /// <summary>注册项清单快照（登记序＝第一段加载顺序）。</summary>
    public static IReadOnlyList<CardComponentEntry> Registered
    {
        get
        {
            lock (Gate)
            {
                return Order.ToArray();
            }
        }
    }
}
