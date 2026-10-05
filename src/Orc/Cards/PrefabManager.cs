using Orc.Core;

namespace Orc.Cards;

/// <summary>
/// 预制体管理器（S-C7；引擎级）：预览体（事件/触发器/效果）与处理器的注册、解析与本地分发。
/// 来源两条（Q16=a）：
/// ①**程序集来源**——显式注册 API（装配期逐条登记 assemblyKey → 委托；注册即配置、重复拒绝）；
/// ②**本地明文目录来源**——扫描目录下的 <c>*.prefab.json</c> 明文文件读入效果快照（无在线分发）。
/// 解析（<see cref="ResolveHandler"/>）：程序集来源查注册表；csx 来源经 <see cref="LogicEngine.ScriptEvaluator"/>
/// （未装配＝结构化失败）。委托为泛型签名，故解析结果经 <see cref="ScriptHandlerAdapter"/> 按视图类型绑定。
/// 单线程语义（与引擎一致）。
/// </summary>
public sealed class PrefabManager
{
    /// <summary>本地明文预制体文件名后缀（分发约定）。</summary>
    public const string PrefabFilePattern = "*.prefab.json";

    private readonly LogicEngine _engine;
    private readonly Dictionary<string, Delegate> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectSnapshot> _prefabs = new(StringComparer.Ordinal);

    internal PrefabManager(LogicEngine engine) => _engine = engine;

    /// <summary>已注册处理器键（登记序快照）。</summary>
    public IReadOnlyList<string> HandlerKeys => _handlers.Keys.ToArray();

    /// <summary>已注册效果快照 id（登记序快照）。</summary>
    public IReadOnlyList<string> PrefabIds => _prefabs.Keys.ToArray();

    /// <summary>注册程序集来源处理器（显式；注册即配置）。</summary>
    /// <exception cref="ArgumentException">assemblyKey 为空白。</exception>
    /// <exception cref="ArgumentNullException">handler 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一键重复注册（被拒绝）。</exception>
    public void RegisterHandler(string assemblyKey, Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyKey);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(assemblyKey, handler))
        {
            throw new InvalidOperationException($"处理器键 '{assemblyKey}' 已注册（重复注册被拒绝——注册即配置）。");
        }
    }

    /// <summary>按键取处理器（未注册＝false）。</summary>
    public bool TryGetHandler(string assemblyKey, out Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(assemblyKey);
        return _handlers.TryGetValue(assemblyKey, out handler!);
    }

    /// <summary>注册效果快照（预制体 id 为键；重复＝拒绝）。</summary>
    /// <exception cref="ArgumentNullException">snapshot 为 null。</exception>
    /// <exception cref="InvalidOperationException">同一 id 重复注册（被拒绝）。</exception>
    public void RegisterPrefab(EffectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!_prefabs.TryAdd(snapshot.Root.Id, snapshot))
        {
            throw new InvalidOperationException($"效果预制体 '{snapshot.Root.Id}' 已注册（重复注册被拒绝）。");
        }
    }

    /// <summary>按 id 取效果快照（未注册＝false）。</summary>
    public bool TryGetPrefab(string id, out EffectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _prefabs.TryGetValue(id, out snapshot!);
    }

    /// <summary>
    /// 从本地明文目录加载预制体（<c>*.prefab.json</c>；只扫顶层、按文件名序确定性处理）。
    /// 单文件失败＝隔离记录、不阻断其余（返回报告）；目录不存在＝空报告。
    /// </summary>
    /// <exception cref="ArgumentNullException">directoryPath 为 null。</exception>
    public PrefabLoadReport LoadDirectory(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            return new PrefabLoadReport(0, Array.Empty<string>());
        }

        var loaded = 0;
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(directoryPath, PrefabFilePattern, SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            try
            {
                var snapshot = PrefabJson.Deserialize(File.ReadAllText(file));
                RegisterPrefab(snapshot);
                loaded++;
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        return new PrefabLoadReport(loaded, failures);
    }

    /// <summary>
    /// 解析事件预制体为可调用处理器（按视图类型绑定）。失败结构化返回、不抛（除参数为 null）。
    /// 分类：<c>handler-missing</c>（程序集键未注册）/ <c>evaluator-missing</c>（未装配脚本求值器）/
    /// <c>signature</c>（签名不符）/ 以及 csx 求值器的 compile/runtime/entry-missing/sandbox/timeout。
    /// </summary>
    /// <exception cref="ArgumentNullException">prefab 或 viewType 为 null。</exception>
    public HandlerResolution ResolveHandler(EventPrefab prefab, Type viewType)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        ArgumentNullException.ThrowIfNull(viewType);

        if (prefab.AssemblyKey is not null)
        {
            if (!_handlers.TryGetValue(prefab.AssemblyKey, out var handler))
            {
                return HandlerResolution.Fail("handler-missing", $"未注册程序集处理器 '{prefab.AssemblyKey}'。");
            }

            var bound = ScriptHandlerAdapter.TryBind(viewType, handler);
            return bound is null
                ? HandlerResolution.Fail("signature", $"程序集处理器 '{prefab.AssemblyKey}' 签名与视图 '{viewType.Name}' 不符。")
                : HandlerResolution.Ok((Delegate)bound);
        }

        if (_engine.ScriptEvaluator is null)
        {
            return HandlerResolution.Fail("evaluator-missing", "未装配脚本求值器（engine.ScriptEvaluator）——csx 处理器不可用。");
        }

        var result = _engine.ScriptEvaluator.Evaluate(new ScriptRequest(prefab.CsxSource!, prefab.EntryName, viewType));
        return result.Success
            ? HandlerResolution.Ok(result.Handler!)
            : HandlerResolution.Fail(result.ErrorCategory ?? "script", result.Error ?? "脚本求值失败。");
    }
}

/// <summary>本地目录加载报告（S-C7）：成功条数与失败明细（单文件失败隔离）。</summary>
/// <param name="Loaded">成功加载并注册的条数。</param>
/// <param name="Failures">失败明细（文件名 + 原因）。</param>
public sealed record PrefabLoadReport(int Loaded, IReadOnlyList<string> Failures);

/// <summary>处理器解析结果（S-C7；结构化——失败不抛）。</summary>
public sealed class HandlerResolution
{
    private HandlerResolution(bool success, Delegate? handler, string? errorCategory, string? error)
    {
        Success = success;
        Handler = handler;
        ErrorCategory = errorCategory;
        Error = error;
    }

    /// <summary>是否成功。</summary>
    public bool Success { get; }

    /// <summary>绑定后的泛型委托（成功时非 null）。</summary>
    public Delegate? Handler { get; }

    /// <summary>失败分类。</summary>
    public string? ErrorCategory { get; }

    /// <summary>失败消息。</summary>
    public string? Error { get; }

    internal static HandlerResolution Ok(Delegate handler) => new(true, handler, null, null);

    internal static HandlerResolution Fail(string category, string error) => new(false, null, category, error);
}
