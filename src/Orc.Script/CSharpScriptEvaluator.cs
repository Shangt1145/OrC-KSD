using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Orc.Core;

namespace Orc.Script;

/// <summary>
/// csx 脚本求值器（S-C6；<see cref="IScriptEvaluator"/> 的 Roslyn 实现）：
/// ①安全检查（引用白名单 → 导入白名单 → 关键字过滤）；②编译（诊断错误＝compile 失败）；
/// ③执行取入口（具名变量；入口缺失/类型不符＝entry-missing/signature）；④超时（timeout）；
/// ⑤运行时异常（runtime）。一切失败结构化返回、**不抛异常**。
/// 契约（csx 入口写法）：脚本以**具名变量**定义入口，值须为
/// <c>Func&lt;TView, Context, CancellationToken, Task&gt;</c>（如
/// <c>Func&lt;MyView, Context, CancellationToken, Task&gt; HandleAsync = (view, ctx, ct) =&gt; ...;</c>）。
/// 结果按（视图类型 + 入口名 + 超时 + 源码哈希）缓存复用（编译一次）。
/// 安全定位：**尽力而为**的弱沙箱（同进程）；非安全边界，强隔离须进程级。
/// </summary>
public sealed class CSharpScriptEvaluator : IScriptEvaluator
{
    private readonly CSharpScriptSecurityOptions _options;
    private readonly ConcurrentDictionary<string, ScriptEvaluationResult> _cache = new(StringComparer.Ordinal);
    private readonly Lazy<ScriptOptions> _scriptOptions;

    /// <summary>创建求值器（options 缺省＝<see cref="CSharpScriptSecurityOptions"/> 默认）。</summary>
    public CSharpScriptEvaluator(CSharpScriptSecurityOptions? options = null)
    {
        _options = options ?? new CSharpScriptSecurityOptions();
        _scriptOptions = new Lazy<ScriptOptions>(BuildScriptOptions, isThreadSafe: true);
    }

    /// <returns>总缓存条数（诊断用）。</returns>
    public int CacheCount => _cache.Count;

    /// <inheritdoc />
    public ScriptEvaluationResult Evaluate(ScriptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : _options.TimeoutMs;
        var key = $"{request.ViewType.AssemblyQualifiedName}|{request.EntryName}|{timeoutMs}|{StableHash.Fnv1a64(request.Source):x16}";

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = EvaluateCore(request, timeoutMs);
        _cache[key] = result;
        return result;
    }

    private ScriptEvaluationResult EvaluateCore(ScriptRequest request, int timeoutMs)
    {
        var violation = FindViolation(request.Source);
        if (violation is not null)
        {
            return ScriptEvaluationResult.Fail("sandbox", violation);
        }

        Script<object> script;
        try
        {
            script = CSharpScript.Create(request.Source, _scriptOptions.Value);
        }
        catch (Exception ex)
        {
            return ScriptEvaluationResult.Fail("compile", ex.Message);
        }

        var errors = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            return ScriptEvaluationResult.Fail("compile", string.Join("; ", errors.Select(e => e.GetMessage())));
        }

        ScriptState<object>? state;
        try
        {
            var run = script.RunAsync();
            if (!run.Wait(timeoutMs))
            {
                // 说明：同进程内无法真正终止脚本——超时＝逻辑中断（尽力而为；强隔离须进程级）。
                return ScriptEvaluationResult.Fail("timeout", $"脚本执行超过 {timeoutMs}ms（同进程尽力中断）。");
            }

            state = run.Result;
        }
        catch (AggregateException ex)
        {
            return ScriptEvaluationResult.Fail("runtime", ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            return ScriptEvaluationResult.Fail("runtime", ex.Message);
        }

        if (state is null)
        {
            return ScriptEvaluationResult.Fail("runtime", "脚本执行未产出状态。");
        }

        var entry = state.Variables.FirstOrDefault(v => string.Equals(v.Name, request.EntryName, StringComparison.Ordinal));
        if (entry is null)
        {
            return ScriptEvaluationResult.Fail("entry-missing", $"脚本未定义入口 '{request.EntryName}'。");
        }

        if (entry.Value is not Delegate handler)
        {
            return ScriptEvaluationResult.Fail(
                "entry-missing", $"入口 '{request.EntryName}' 不是委托值（实际：{entry.Value?.GetType().Name ?? "null"}）。");
        }

        var bound = ScriptHandlerAdapter.TryBind(request.ViewType, handler);
        if (bound is null)
        {
            return ScriptEvaluationResult.Fail(
                "signature",
                $"入口 '{request.EntryName}' 签名不符（期望 Func<{request.ViewType.Name}, Context, CancellationToken, Task>）。");
        }

        return ScriptEvaluationResult.Ok((Delegate)bound);
    }

    /// <summary>关键字过滤（文本级；命中＝沙箱拒绝。文档注明可被绕过，非安全边界）。</summary>
    private string? FindViolation(string source)
    {
        foreach (var keyword in _options.BlockedKeywords)
        {
            if (!string.IsNullOrEmpty(keyword) && source.Contains(keyword, StringComparison.Ordinal))
            {
                return $"脚本命中屏蔽片段 '{keyword}'（安全策略拒绝；非安全边界，仅尽力而为）。";
            }
        }

        return null;
    }

    /// <summary>按引用白名单构建 <see cref="ScriptOptions"/>（仅取白名单内的已加载程序集）。</summary>
    private ScriptOptions BuildScriptOptions()
    {
        var allowed = new HashSet<string>(_options.AllowedAssemblies, StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || !allowed.Contains(assembly.GetName().Name ?? string.Empty))
            {
                continue;
            }

            string location;
            try
            {
                location = assembly.Location;
            }
            catch (Exception)
            {
                continue; // 单文件发布等无路径场景：跳过
            }

            if (!string.IsNullOrEmpty(location))
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        return ScriptOptions.Default.WithReferences(references).WithImports(_options.AllowedImports);
    }
}
