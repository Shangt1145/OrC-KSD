using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 保留面缺失时的内建回退实现（仅在对应全局函数缺失时注入；当前模块组合下通常不会触发）。
/// </summary>
internal static class LuaBuiltinFallbacks
{
    /// <summary>unpack(t [, i [, j]])：将 t 的 i..j 元素作为多值返回。</summary>
    internal static DynValue Unpack(ScriptExecutionContext context, CallbackArguments args)
    {
        var table = args.AsType(0, "unpack", DataType.Table).Table;

        int start = 1;
        int end = table.Length;

        if (args.Count > 1 && !args[1].IsNil())
            start = (int)args[1].Number;
        if (args.Count > 2 && !args[2].IsNil())
            end = (int)args[2].Number;

        var values = new List<DynValue>();
        for (int i = start; i <= end; i++)
            values.Add(table.Get(i));

        return DynValue.NewTuple(values.ToArray());
    }
}
