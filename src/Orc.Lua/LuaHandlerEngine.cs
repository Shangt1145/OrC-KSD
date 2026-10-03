namespace Orc.Lua;

/// <summary>
/// 动态 handler 组件（组件实例级）：
/// - 白名单动词注册（须在任何源初始化之前完成；实例级独立、无全局单例；同名重复注册 / 层级冲突 = 显式拒绝）；
/// - 从 Lua 脚本源初始化"源单元"（每次初始化 = 全新独立环境）；
/// - 产出可复用的可调用 handler（同源共享环境、调用间状态延续；跨源隔离）。
/// 线程模型：不承诺线程安全——同一实例的一切操作由调用方保证同一线程串行执行；
/// 同一线程内的重入（脚本 → 宿主回调 → 再调用 handler）行为定义良好。
/// 后置项（本版明确不做、不预埋）：脚本加载逻辑（文件/目录加载、热重载、文件监控）；Orc 引擎集成适配器。
/// </summary>
public sealed class LuaHandlerEngine
{
    private readonly VerbNode _rootNode = new();
    private bool _initializationStarted;

    /// <summary>创建组件实例。</summary>
    /// <param name="options">选项（白名单域根名，默认 "orc"）；null 视为默认选项。</param>
    public LuaHandlerEngine(LuaHandlerEngineOptions? options = null)
    {
        var root = options is null
            ? LuaHandlerEngineOptions.DefaultRootNamespace
            : options.RootNamespace ?? throw new ArgumentNullException(
                nameof(options), "the root namespace must not be null");

        ValidateRootNamespace(root);
        RootNamespace = root;
    }

    /// <summary>白名单域根名（Lua 侧访问形如 <c>{RootNamespace}.verb</c>）。</summary>
    public string RootNamespace { get; }

    /// <summary>域注册树根（沙箱装配使用）。</summary>
    internal VerbNode RootNode => _rootNode;

    /// <summary>
    /// 注册宿主动词（可调用值，签名 <c>Func&lt;object?[], object?&gt;</c>：Lua 实参编组为数组，返回值编组回 Lua）。
    /// 注册名支持点分层级（如 <c>combat.play</c> → Lua 侧 <c>orc.combat.play</c>）。
    /// </summary>
    /// <param name="name">动词名（点分层级；null/空/非法段/重复/层级冲突 = 显式参数异常）。</param>
    /// <param name="callback">宿主回调。</param>
    public void RegisterVerb(string name, Func<object?[], object?> callback)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));
        if (_initializationStarted)
        {
            throw new InvalidOperationException(
                "verbs must be registered before any source unit is initialized (runtime registration is not supported)");
        }

        var segments = name.Split('.');
        foreach (var segment in segments)
        {
            if (!IsValidIdentifier(segment))
                throw new ArgumentException($"the verb name '{name}' contains an invalid segment '{segment}'", nameof(name));
        }

        var node = _rootNode;
        var prefix = string.Empty;
        foreach (var segment in segments)
        {
            if (node.IsLeaf)
                throw new ArgumentException($"cannot register '{name}': '{prefix}' is already a verb", nameof(name));

            prefix = prefix.Length == 0 ? segment : prefix + "." + segment;
            node = node.GetOrAddChild(segment);
        }

        if (node.IsLeaf)
            throw new ArgumentException($"the verb '{name}' is already registered", nameof(name));
        if (node.Children.Count > 0)
            throw new ArgumentException($"cannot register '{name}': it is already a namespace containing other verbs", nameof(name));

        node.Callback = callback;
    }

    /// <summary>
    /// 以脚本源初始化一个源单元。每次调用 = 全新独立环境（与源文本是否相同无关）；
    /// 初始化失败时返回结构化错误，且 Value 携带一个不可用单元句柄（后续获取操作复现原分类；宿主应丢弃）。
    /// </summary>
    /// <param name="source">Lua 脚本源（空字符串 = 合法空 chunk；显式 null 为 API 层误用，抛参数异常）。</param>
    public LuaResult<LuaSourceUnit> InitializeSource(string source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        _initializationStarted = true;

        var unit = LuaSourceUnit.Create(this, source);
        return unit.IsAvailable
            ? LuaResult<LuaSourceUnit>.Success(unit)
            : LuaResult<LuaSourceUnit>.FailureWithPayload(unit.InitializationError!, unit);
    }

    private static void ValidateRootNamespace(string root)
    {
        if (root.Length == 0)
            throw new ArgumentException("the root namespace must not be empty", nameof(root));
        if (!IsValidIdentifier(root))
            throw new ArgumentException($"the root namespace '{root}' must be a valid Lua identifier", nameof(root));
        if (LuaSandbox.RetainedNameSet.Contains(root))
            throw new ArgumentException($"the root namespace '{root}' collides with a retained global name", nameof(root));
        if (LuaSandbox.BlockedNameSet.Contains(root))
            throw new ArgumentException($"the root namespace '{root}' collides with a blocked global name", nameof(root));
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsAsciiLetter(value[0]) || value[0] == '_'))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(value[i]) || value[i] == '_'))
                return false;
        }

        return true;
    }
}
