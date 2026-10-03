namespace Filament;

using System;
using MoonSharp.Interpreter;

/// <summary>
/// A loaded Lua module — the table returned by a <c>return M</c> script — that
/// survives hot-reload (the registry swaps the inner <see cref="Script"/> +
/// table under a gate, so held references keep working).
///
/// <see cref="Call{TRet, TParams}"/> marshals a <c>[Scriptable]</c> params
/// record into a single Lua table argument, invokes the named method, and
/// coerces the return. Every failure is a <see cref="LuaError"/> value; the
/// only caught exception is MoonSharp's unavoidable runtime-error throw.
/// </summary>
public sealed class LuaModule
{
    public string Path { get; }

    private readonly object _gate = new();
    private Script _script;
    private Table _module;

    internal LuaModule(string path, Script script, Table module)
    {
        Path = path;
        _script = script;
        _module = module;
    }

    internal void Replace(Script script, Table module)
    {
        lock (_gate)
        {
            _script = script;
            _module = module;
        }
    }

    public Result<TRet, LuaError> Call<TRet, TParams>(string method, TParams args)
    {
        Script s;
        Table m;
        lock (_gate)
        {
            s = _script;
            m = _module;
        }

        var fn = m.Get(method);
        if (ScriptableMarshal.IsAbsent(fn) || fn.Type != DataType.Function)
            return Result.Err(LuaError.MethodMissing(Path, method));

        Table argTable;
        try
        {
            argTable = ScriptableMarshal.ToLua(args, s);
        }
        catch (Exception ex)
        {
            return Result.Err(LuaError.ParamMarshal($"could not marshal params for {Path}::{method}: {ex.Message}"));
        }

        DynValue ret;
        try
        {
            ret = s.Call(fn, DynValue.NewTable(argTable));
        }
        catch (ScriptRuntimeException ex)
        {
            return Result.Err(LuaError.RuntimeError(Path, method, ex.DecoratedMessage ?? ex.Message));
        }

        return ConvertReturn<TRet>(ret, Path, method);
    }

    public Result<Unit, LuaError> CallVoid<TParams>(string method, TParams args)
        => Call<Unit, TParams>(method, args);

    private static Result<TRet, LuaError> ConvertReturn<TRet>(DynValue ret, string path, string method)
    {
        var t = typeof(TRet);

        if (t == typeof(Unit)) return Result.Ok((TRet)(object)Unit.Value);
        if (t == typeof(DynValue)) return Result.Ok((TRet)(object)ret);

        // A registered [Scriptable] return type → route through its converter.
        if (ScriptableMarshal.TryGet<TRet>(out var conv))
        {
            if (ret.Type != DataType.Table)
                return Result.Err(LuaError.ReturnCoercion(path, method, $"expected a table, got {ret.Type}"));
            return conv.FromLua(ret);
        }

        // Primitives.
        object? boxed = null;
        if (t == typeof(bool)) boxed = ret.Type == DataType.Boolean ? ret.Boolean : null;
        else if (t == typeof(string)) boxed = ret.Type == DataType.String ? ret.String : null;
        else if (t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double))
        {
            if (ret.Type == DataType.Number)
            {
                var d = ret.Number;
                if (t == typeof(int)) boxed = (d >= int.MinValue && d <= int.MaxValue && Math.Floor(d) == d) ? (int)d : null;
                else if (t == typeof(long)) boxed = (Math.Abs(d) <= 9007199254740992d && Math.Floor(d) == d) ? (long)d : (object?)null;
                else if (t == typeof(float)) boxed = (float)d;
                else boxed = d;
            }
        }
        else
        {
            return Result.Err(LuaError.ReturnCoercion(path, method,
                $"no conversion to '{t.Name}' (mark it [Scriptable] or return a primitive)"));
        }

        if (boxed is null)
            return Result.Err(LuaError.ReturnCoercion(path, method, $"value {ret.Type} not convertible to {t.Name}"));
        return Result.Ok((TRet)boxed);
    }
}
