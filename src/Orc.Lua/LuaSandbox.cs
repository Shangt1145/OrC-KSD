using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 单脚本沙箱装配：
/// - 全局表规范化：收敛到保留面（R5 最低集 + R13 补入）；禁用面与其余名字全部移除，
///   其访问经拦截层显式失败（不得静默）；
/// - 全局表访问拦截：禁用面（读/写）→ 沙箱限制；白名单域（读合法/写拒绝）→ 业务代理；
/// - string 库 dump 成员封锁（成员级，沙箱限制）。
/// 拦截依赖 MoonSharp VM 对表索引的元方法支持（__index/__newindex，已核实）。
/// </summary>
internal static class LuaSandbox
{
    /// <summary>保留面：规范化后保留、且必须可用的全局名字。</summary>
    internal static readonly HashSet<string> RetainedNameSet = new(StringComparer.Ordinal)
    {
        "string", "math", "table",
        "pairs", "ipairs", "next", "select", "unpack",
        "type", "tostring", "tonumber", "pcall", "error", "assert",
        "_G",
    };

    /// <summary>
    /// 禁用面：读取 / 写入 / 调用一律失败（分类：沙箱限制）。
    /// R5 所列（io/os/package/debug/require/loadstring/loadfile/dofile/print/collectgarbage）
    /// + R13 所列（raw 系 rawget/rawset/rawlen/rawequal、元表操作对 setmetatable/getmetatable）
    /// + 同族补全（load：动态代码家族；"实现可增"）。
    /// </summary>
    internal static readonly HashSet<string> BlockedNameSet = new(StringComparer.Ordinal)
    {
        "io", "os", "package", "debug", "require",
        "loadstring", "loadfile", "dofile", "load",
        "print", "collectgarbage",
        "rawget", "rawset", "rawlen", "rawequal",
        "setmetatable", "getmetatable",
    };

    /// <summary>装配沙箱（在源执行之前调用）。</summary>
    internal static void Configure(Script script, LuaHandlerEngine engine)
    {
        NormalizeGlobals(script);
        InstallGlobalsGuard(script, engine);
        InstallStringDumpGuard(script);
    }

    /// <summary>把全局表收敛到保留面：移除其余一切名字；确保必需名齐全。</summary>
    private static void NormalizeGlobals(Script script)
    {
        var globals = script.Globals;

        var toRemove = new List<DynValue>();
        foreach (var pair in globals.Pairs)
        {
            if (pair.Key.Type == DataType.String && RetainedNameSet.Contains(pair.Key.String))
                continue;
            toRemove.Add(pair.Key);
        }

        foreach (var key in toRemove)
            globals.Remove(key);

        // unpack：模块组合可能只提供 table.unpack；确保全局 unpack 可用（优先复用现成实现）。
        if (globals.Get("unpack").Type == DataType.Nil)
        {
            var tableLib = globals.Get("table");
            var unpack = tableLib.Type == DataType.Table ? tableLib.Table.Get("unpack") : DynValue.Nil;
            globals.Set("unpack", IsCallable(unpack)
                ? unpack
                : DynValue.NewCallback(LuaBuiltinFallbacks.Unpack, "unpack"));
        }

        // _G 兜底：确保指向全局表本身（GlobalConsts 模块通常已提供）。
        if (globals.Get("_G").Type == DataType.Nil)
            globals.Set("_G", DynValue.NewTable(globals));
    }

    /// <summary>安装全局表访问拦截（禁用面失败 + 白名单域代理 + 写入保护）。</summary>
    private static void InstallGlobalsGuard(Script script, LuaHandlerEngine engine)
    {
        var globals = script.Globals;
        var rootName = engine.RootNamespace;
        var domain = new LuaDomainAccess(script, rootName, engine.RootNode);

        var meta = new Table(script);
        meta.Set("__index", DynValue.NewCallback((context, args) =>
        {
            if (args[1].Type == DataType.String)
            {
                var name = args[1].String;
                if (BlockedNameSet.Contains(name))
                    throw OrcLuaErrors.Sandbox($"access to '{name}' is not allowed by the sandbox");
                if (name == rootName)
                    return domain.RootValue;
            }

            return DynValue.Nil;
        }, "globals.__index"));

        meta.Set("__newindex", DynValue.NewCallback((context, args) =>
        {
            if (args[1].Type == DataType.String)
            {
                var name = args[1].String;
                if (BlockedNameSet.Contains(name))
                    throw OrcLuaErrors.Sandbox($"writing to '{name}' is not allowed by the sandbox");
                if (name == rootName)
                    throw OrcLuaErrors.Whitelist($"the verb namespace '{name}' is protected and cannot be written");
            }

            args[0].Table.Set(args[1], args[2]);
            return DynValue.Void;
        }, "globals.__newindex"));

        globals.MetaTable = meta;
    }

    /// <summary>string.dump 成员级封锁：移除 raw 成员，且以元表拦截其读取与写入。</summary>
    private static void InstallStringDumpGuard(Script script)
    {
        var stringValue = script.Globals.Get("string");
        if (stringValue.Type != DataType.Table)
            return;

        var stringTable = stringValue.Table;
        stringTable.Remove("dump");

        var meta = new Table(script);
        meta.Set("__index", DynValue.NewCallback((context, args) =>
        {
            if (args[1].Type == DataType.String && args[1].String == "dump")
                throw OrcLuaErrors.Sandbox("'string.dump' is blocked by the sandbox");
            return DynValue.Nil;
        }, "string.__index"));

        meta.Set("__newindex", DynValue.NewCallback((context, args) =>
        {
            if (args[1].Type == DataType.String && args[1].String == "dump")
                throw OrcLuaErrors.Sandbox("writing to 'string.dump' is blocked by the sandbox");
            args[0].Table.Set(args[1], args[2]);
            return DynValue.Void;
        }, "string.__newindex"));

        stringTable.MetaTable = meta;
    }

    private static bool IsCallable(DynValue value) =>
        value.Type is DataType.Function or DataType.ClrFunction;
}
