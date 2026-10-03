using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 源单元：一次"初始化"动作的产物。
/// - 每次初始化 = 全新独立 Lua 环境（与源文本是否相同无关）；同源产出的多个 handler 共享本单元环境（同源共享）；
/// - 单元之间互不可见（跨源隔离）；
/// - 初始化失败的单元不可用：后续获取操作立即以原初始化错误复现失败（不再执行任何操作）。
/// </summary>
public sealed class LuaSourceUnit
{
    private readonly Script? _script;
    private readonly DynValue? _executionResult;

    private LuaSourceUnit(Script script, DynValue executionResult)
    {
        _script = script;
        _executionResult = executionResult;
        IsAvailable = true;
    }

    private LuaSourceUnit(LuaError error)
    {
        IsAvailable = false;
        InitializationError = error;
    }

    /// <summary>单元是否可用（初始化成功）。</summary>
    public bool IsAvailable { get; }

    /// <summary>初始化失败时的结构化错误（不可用单元）；可用时为 <see langword="null"/>。</summary>
    public LuaError? InitializationError { get; }

    /// <summary>
    /// 按名获取函数（单层解析）：先查源执行结果（若结果为表，取其成员），未命中再查源环境全局；
    /// 命中值须为函数——名字不存在、命中非函数、结果非函数均以"缺失函数"结构化失败（即验即败）。
    /// </summary>
    /// <param name="name">函数名（单层；null/空为 API 层误用，抛参数异常）。</param>
    public LuaResult<LuaHandler> GetFunction(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentException("the function name must not be empty", nameof(name));

        if (!IsAvailable)
            return LuaResult<LuaHandler>.Failure(InitializationError!);

        DynValue? candidate = null;
        if (_executionResult!.Type == DataType.Table)
        {
            var member = _executionResult.Table.Get(name);
            if (!member.IsNil())
                candidate = member;
        }

        candidate ??= _script!.Globals.Get(name);

        if (candidate.Type == DataType.Nil)
        {
            return Missing(
                $"function '{name}' was not found " +
                "(resolved against the execution result table first, then the environment globals)");
        }

        if (!IsCallable(candidate))
        {
            return Missing(
                $"'{name}' is not a function (found a value of type '{candidate.Type}')");
        }

        return LuaResult<LuaHandler>.Success(new LuaHandler(_script!, candidate, name));
    }

    /// <summary>
    /// 取执行结果函数：源的执行结果本身是函数时得 handler；否则以"缺失函数"结构化失败（含 nil）。
    /// </summary>
    public LuaResult<LuaHandler> GetResultFunction()
    {
        if (!IsAvailable)
            return LuaResult<LuaHandler>.Failure(InitializationError!);

        var result = _executionResult!;
        if (!IsCallable(result))
            return Missing($"the source execution result is not a function (got '{result.Type}')");

        return LuaResult<LuaHandler>.Success(new LuaHandler(_script!, result, "result"));
    }

    /// <summary>执行源，产出单元（初始化阶段错误一律结构化：编译/语法、运行时、白名单/沙箱类）。</summary>
    internal static LuaSourceUnit Create(LuaHandlerEngine engine, string source)
    {
        var script = LuaScriptFactory.Create(engine);
        try
        {
            var result = script.DoString(source, codeFriendlyName: "orc-lua-source");
            return new LuaSourceUnit(script, result);
        }
        catch (SyntaxErrorException ex)
        {
            return new LuaSourceUnit(new LuaError(LuaErrorKind.SyntaxError, ex.Message));
        }
        catch (InterpreterException ex)
        {
            // 含 ScriptRuntimeException 的未捕获路径：按最终未捕获错误分类（含沙箱/白名单标记还原）。
            return new LuaSourceUnit(OrcLuaErrors.ClassifyUncaught(ex));
        }
        catch (Exception ex)
        {
            // 防御：解释器内部异常不炸穿宿主。
            return new LuaSourceUnit(new LuaError(
                LuaErrorKind.RuntimeError,
                $"internal interpreter error: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static LuaResult<LuaHandler> Missing(string message) =>
        LuaResult<LuaHandler>.Failure(new LuaError(LuaErrorKind.MissingFunction, message));

    private static bool IsCallable(DynValue value) =>
        value.Type is DataType.Function or DataType.ClrFunction;
}
