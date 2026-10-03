using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 可复用、可调用的动态 handler（第一版仅 C# 方法调用形态）：
/// 调用 = 参数包（字符串键）→ 单个参数表 → Lua 函数 → 首个返回值编组回宿主（快照语义）。
/// - 同一 handler 连续调用可观察到前次调用留下的环境状态（状态延续）；
/// - 一次调用失败后 handler 仍可用（不要求状态回滚）；
/// - 默认不向宿主抛异常：脚本与数据错误一律以结构化结果返回。
/// </summary>
public sealed class LuaHandler
{
    private readonly Script _script;
    private readonly DynValue _function;

    internal LuaHandler(Script script, DynValue function, string name)
    {
        _script = script;
        _function = function;
        Name = name;
    }

    /// <summary>获取时的名字（按名获取 = 注册名；取执行结果 = "result"）。</summary>
    public string Name { get; }

    /// <summary>无参数调用：Lua 侧收到空表（非 nil）。</summary>
    public LuaResult<object?> Invoke() => InvokeCore(null);

    /// <summary>以字符串键参数包调用（常规入口）。</summary>
    /// <param name="args">参数包；显式 null 为 API 层误用（抛 <see cref="ArgumentNullException"/>）。</param>
    public LuaResult<object?> Invoke(IDictionary<string, object?> args)
    {
        if (args is null)
            throw new ArgumentNullException(nameof(args));

        return InvokeCore(args.Select(kv => new KeyValuePair<object, object?>(kv.Key, kv.Value)));
    }

    /// <summary>
    /// 以任意键类型的参数包调用（边界检查入口）：顶层参数包键必须为字符串，
    /// 非字符串键（数字键等）以结构化"编组错误"失败（调用失败、不抛）。
    /// </summary>
    /// <param name="args">参数包；显式 null 为 API 层误用（抛 <see cref="ArgumentNullException"/>）。</param>
    public LuaResult<object?> InvokeWithObjectKeys(IDictionary<object, object?> args)
    {
        if (args is null)
            throw new ArgumentNullException(nameof(args));

        return InvokeCore(args);
    }

    private LuaResult<object?> InvokeCore(IEnumerable<KeyValuePair<object, object?>>? args)
    {
        // 1) 参数编组（入方向；失败 = 结构化"编组错误"，不执行脚本）。
        Table argsTable;
        try
        {
            argsTable = LuaValueMarshaller.BuildArgsTable(_script, args);
        }
        catch (LuaMarshalException ex)
        {
            return LuaResult<object?>.Failure(new LuaError(
                LuaErrorKind.MarshallingError, $"cannot marshal the invocation arguments: {ex.Message}"));
        }

        // 2) 调用（首次返回值）。
        DynValue callResult;
        try
        {
            callResult = _script.Call(_function, new DynValue[] { DynValue.NewTable(argsTable) });
        }
        catch (SyntaxErrorException ex)
        {
            return LuaResult<object?>.Failure(new LuaError(LuaErrorKind.SyntaxError, ex.Message));
        }
        catch (InterpreterException ex)
        {
            return LuaResult<object?>.Failure(OrcLuaErrors.ClassifyUncaught(ex));
        }
        catch (Exception ex)
        {
            // 防御：解释器内部异常不炸穿宿主。
            return LuaResult<object?>.Failure(new LuaError(
                LuaErrorKind.RuntimeError, $"internal interpreter error: {ex.GetType().Name}: {ex.Message}"));
        }

        var first = FirstReturnValue(callResult);

        // 3) 返回值编组（出方向）。
        try
        {
            return LuaResult<object?>.Success(LuaValueMarshaller.FromLua(first));
        }
        catch (LuaMarshalException ex)
        {
            return LuaResult<object?>.Failure(new LuaError(
                LuaErrorKind.MarshallingError, $"cannot marshal the result: {ex.Message}"));
        }
    }

    /// <summary>结果 = 第一个返回值（多返回时其余忽略）；nil 返回 = 成功 + 空结果。</summary>
    private static DynValue FirstReturnValue(DynValue result)
    {
        if (result.Type == DataType.Tuple)
            return result.Tuple.Length > 0 ? result.Tuple[0] : DynValue.Nil;
        return result;
    }
}
