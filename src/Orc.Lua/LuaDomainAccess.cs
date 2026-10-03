using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 白名单域的 Lua 侧代理访问（每脚本一个实例）：
/// 域为只读的代理表树——读取已注册成员返回对应子树/动词，读取未注册成员与任何写入均以"白名单拒绝"失败。
/// 代理表保持 raw 为空（注册树数据仅存于 C# 侧），因此对已注册成员的覆盖写入同样被拦截（能力不可篡改）。
/// 域始终存在（与注册数无关）；代理值按节点缓存，保证同一路径的读取引用稳定。
/// </summary>
internal sealed class LuaDomainAccess
{
    private readonly Script _script;
    private readonly string _rootName;
    private readonly VerbNode _rootNode;
    private readonly Dictionary<VerbNode, DynValue> _values = new(ReferenceComparer<VerbNode>.Instance);

    internal LuaDomainAccess(Script script, string rootName, VerbNode rootNode)
    {
        _script = script;
        _rootName = rootName;
        _rootNode = rootNode;
    }

    /// <summary>域根的 DynValue（table 值）。</summary>
    internal DynValue RootValue => GetValue(_rootNode, _rootName);

    private DynValue GetValue(VerbNode node, string path)
    {
        if (_values.TryGetValue(node, out var cached))
            return cached;

        var value = node.IsLeaf ? BuildVerbValue(node, path) : BuildProxyValue(node, path);
        _values.Add(node, value);
        return value;
    }

    private DynValue BuildProxyValue(VerbNode node, string path)
    {
        var table = new Table(_script);
        var meta = new Table(_script);

        meta.Set("__index", DynValue.NewCallback((context, args) =>
        {
            if (args[1].Type == DataType.String && node.Children.TryGetValue(args[1].String, out var child))
                return GetValue(child, path + "." + args[1].String);

            throw OrcLuaErrors.Whitelist($"'{path}.{DisplayKey(args[1])}' is not a registered verb");
        }, path + ".__index"));

        meta.Set("__newindex", DynValue.NewCallback((context, args) =>
        {
            throw OrcLuaErrors.Whitelist($"the verb namespace '{path}' is protected and cannot be written");
        }, path + ".__newindex"));

        // __pairs：枚举注册树（注册顺序）。MoonSharp 支持 __pairs 时生效；
        // 枚举面仅作实现原则，不入验收断言（需求 R14）。
        meta.Set("__pairs", DynValue.NewCallback((context, args) => DynValue.NewTuple(
            DynValue.NewCallback(BuildEnumerator(node, path), path + ".__pairs_iter"),
            args[0],
            DynValue.Nil), path + ".__pairs"));

        table.MetaTable = meta;
        return DynValue.NewTable(table);
    }

    private Func<ScriptExecutionContext, CallbackArguments, DynValue> BuildEnumerator(VerbNode node, string path)
    {
        return (context, args) =>
        {
            int start = 0;
            if (args[1].Type == DataType.String)
            {
                var index = node.ChildOrder.IndexOf(args[1].String);
                if (index < 0)
                    return DynValue.Nil;
                start = index + 1;
            }

            if (start >= node.ChildOrder.Count)
                return DynValue.Nil;

            var name = node.ChildOrder[start];
            return DynValue.NewTuple(DynValue.NewString(name), GetValue(node.Children[name], path + "." + name));
        };
    }

    private DynValue BuildVerbValue(VerbNode node, string path)
    {
        var callback = node.Callback!;
        return DynValue.NewCallback((context, args) =>
        {
            var clrArgs = new object?[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                try
                {
                    clrArgs[i] = LuaValueMarshaller.FromLua(args[i]);
                }
                catch (LuaMarshalException ex)
                {
                    throw OrcLuaErrors.Marshal($"cannot marshal argument #{i + 1} of verb '{path}': {ex.Message}");
                }
            }

            object? result;
            try
            {
                result = callback(clrArgs);
            }
            catch (Exception ex)
            {
                // 回调异常 → 运行时错误（消息标明回调来源），不炸穿宿主；允许脚本 pcall 捕获。
                throw new ScriptRuntimeException($"verb callback '{path}' threw {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                return LuaValueMarshaller.ToLuaValue(_script, result);
            }
            catch (LuaMarshalException ex)
            {
                throw OrcLuaErrors.Marshal($"cannot marshal the return value of verb '{path}': {ex.Message}");
            }
        }, path);
    }

    private static string DisplayKey(DynValue key) =>
        key.Type == DataType.String ? key.String : key.Type.ToLuaTypeString();
}
