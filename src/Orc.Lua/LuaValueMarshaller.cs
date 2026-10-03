using System.Collections;
using System.Globalization;
using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// C# 值 ↔ Lua 值的双向编组（值拷贝、快照语义——跨边界互不穿透）。
/// 输入面：标量 / null / 字符串键字典 / 序列列表 / 嵌套组合；其余类型为编组错误。
/// 输出面：标量 / nil→空 / 表自适应（纯序列→列表；其余含空→字典，字符串键保真、数字键字符串化）。
/// </summary>
internal static class LuaValueMarshaller
{
    /// <summary>嵌套结构最大深度（防御超深/循环结构；超出为编组错误）。</summary>
    private const int MaxDepth = 64;

    // ---------------------------------------------------------------
    // C# → Lua
    // ---------------------------------------------------------------

    /// <summary>
    /// 构建 handler 调用的参数表（顶层：null = 空表；键必须为字符串；值递归编组）。
    /// </summary>
    internal static Table BuildArgsTable(Script script, IEnumerable<KeyValuePair<object, object?>>? args)
    {
        var table = new Table(script);
        if (args is null)
            return table;

        var visited = new HashSet<object>(ReferenceComparer<object>.Instance);
        foreach (var kv in args)
        {
            if (kv.Key is not string key)
                throw new LuaMarshalException($"argument key must be a string (got {DescribeValue(kv.Key)})");
            table.Set(key, ToLua(script, kv.Value, visited, 0));
        }

        return table;
    }

    /// <summary>单个 C# 值 → Lua 值（用于宿主回调返回值）。</summary>
    internal static DynValue ToLuaValue(Script script, object? value)
    {
        var visited = new HashSet<object>(ReferenceComparer<object>.Instance);
        return ToLua(script, value, visited, 0);
    }

    private static DynValue ToLua(Script script, object? value, HashSet<object> visited, int depth)
    {
        if (depth > MaxDepth)
            throw new LuaMarshalException($"structure is too deep (max depth {MaxDepth})");

        switch (value)
        {
            case null:
                return DynValue.Nil;
            case bool b:
                return DynValue.NewBoolean(b);
            case string s:
                return DynValue.NewString(s);
            case char c:
                return DynValue.NewString(c.ToString());
            // 数值统一按 Lua double 语义编组（全部 C# 数值类型 → double）。
            // 注意：绝对值超过 2^53 的整数（long/ulong/decimal 等）会丢失精度
            //（如 ulong.MaxValue → 1.8446744073709552E+19）；需要无损传递的大整数
            // 建议以字符串形式传参，或接受 double 精度损失。
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            case IDictionary dict:
                return DictionaryToTable(script, dict, visited, depth);
            case IEnumerable seq:
                return SequenceToTable(script, seq, visited, depth);
            default:
                throw new LuaMarshalException($"unsupported value type '{value.GetType().FullName}'");
        }
    }

    private static DynValue DictionaryToTable(Script script, IDictionary dict, HashSet<object> visited, int depth)
    {
        // 循环引用防御：以进入/离开栈语义检测真环（允许 DAG 共享引用）。
        if (!visited.Add(dict))
            throw new LuaMarshalException("cyclic reference detected while marshalling a dictionary");

        try
        {
            var table = new Table(script);
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is not string key)
                    throw new LuaMarshalException($"dictionary key must be a string (got {DescribeValue(entry.Key)})");
                table.Set(key, ToLua(script, entry.Value, visited, depth + 1));
            }

            return DynValue.NewTable(table);
        }
        finally
        {
            visited.Remove(dict);
        }
    }

    private static DynValue SequenceToTable(Script script, IEnumerable seq, HashSet<object> visited, int depth)
    {
        if (!visited.Add(seq))
            throw new LuaMarshalException("cyclic reference detected while marshalling a sequence");

        try
        {
            var table = new Table(script);
            int index = 0;
            foreach (var item in seq)
                table.Set(++index, ToLua(script, item, visited, depth + 1));

            return DynValue.NewTable(table);
        }
        finally
        {
            visited.Remove(seq);
        }
    }

    // ---------------------------------------------------------------
    // Lua → C#
    // ---------------------------------------------------------------

    /// <summary>
    /// 单个 Lua 值 → C# 值（用于 handler 返回值与宿主回调参数）。
    /// 表自适应：纯序列（1..n 连续数字键、无其他键）→ <see cref="List{T}"/>；
    /// 其余（含空表/混合）→ <see cref="Dictionary{TKey,TValue}"/>（字符串键保真、数字键字符串化、键冲突为编组错误）。
    /// 函数/线程/用户数据等特殊类型为编组错误。
    /// </summary>
    internal static object? FromLua(DynValue value) => FromLua(value, 0);

    private static object? FromLua(DynValue value, int depth)
    {
        if (depth > MaxDepth)
            throw new LuaMarshalException($"structure is too deep (max depth {MaxDepth})");

        switch (value.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                return null;
            case DataType.Boolean:
                return value.Boolean;
            case DataType.Number:
                return value.Number;
            case DataType.String:
                return value.String;
            case DataType.Table:
                return TableToClr(value.Table, depth);
            default:
                throw new LuaMarshalException(
                    $"value of type '{value.Type}' cannot cross the Orc.Lua boundary " +
                    "(supported: nil / boolean / number / string / table)");
        }
    }

    private static object? TableToClr(Table table, int depth)
    {
        var pairs = new List<TablePair>(table.Length);
        foreach (var pair in table.Pairs)
        {
            if (!pair.Value.IsNil())
                pairs.Add(pair);
        }

        if (IsPureSequence(pairs))
        {
            var list = new object?[pairs.Count];
            foreach (var pair in pairs)
                list[(int)pair.Key.Number - 1] = FromLua(pair.Value, depth + 1);
            return new List<object?>(list);
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var key = KeyToString(pair.Key);
            if (dict.ContainsKey(key))
                throw new LuaMarshalException($"result table contains colliding keys after stringification ('{key}')");
            dict[key] = FromLua(pair.Value, depth + 1);
        }

        return dict;
    }

    /// <summary>纯序列判定：非空，且所有键均为 1..n（n = 键数）范围内的整数。</summary>
    private static bool IsPureSequence(List<TablePair> pairs)
    {
        if (pairs.Count == 0)
            return false;

        foreach (var pair in pairs)
        {
            if (pair.Key.Type != DataType.Number)
                return false;
            double d = pair.Key.Number;
            if (d != Math.Floor(d) || d < 1 || d > pairs.Count)
                return false;
        }

        return true;
    }

    private static string KeyToString(DynValue key) => key.Type switch
    {
        DataType.String => key.String,
        DataType.Number => NumberKeyToString(key.Number),
        _ => throw new LuaMarshalException(
            $"result table key of type '{key.Type}' is not supported (string / number keys only)"),
    };

    private static string NumberKeyToString(double d)
    {
        if (double.IsNaN(d))
            throw new LuaMarshalException("NaN cannot be used as a table key");

        // ±2^53 内的整数值 → 十进制整数文本；其余 → 往返格式。
        if (d == Math.Floor(d) && d >= -9007199254740992d && d <= 9007199254740992d)
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string DescribeValue(object? value) =>
        value is null ? "null" : $"'{value.GetType().FullName}'";
}

/// <summary>编组失败（组件内部信号；对宿主以结构化编组错误呈现，绝不外泄为异常）。</summary>
internal sealed class LuaMarshalException : Exception
{
    internal LuaMarshalException(string message) : base(message)
    {
    }
}
