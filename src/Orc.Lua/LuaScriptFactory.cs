using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 创建并装配单脚本环境（每次源初始化 = 一个全新独立环境）。
/// </summary>
internal static class LuaScriptFactory
{
    /// <summary>
    /// 等效"硬沙箱"的显式模块组合（R5：Preset_HardSandbox 或等效）：
    /// 不含 Metatables（元表操作对为禁用面）、LoadMethods、OS、IO、Debug、Coroutine、Bit32、Dynamic、Json；
    /// 装配阶段还会把全局表进一步收敛到保留面（LuaSandbox.Configure）。
    /// </summary>
    private const CoreModules SandboxModules =
        CoreModules.Basic
        | CoreModules.String
        | CoreModules.Table
        | CoreModules.Math
        | CoreModules.TableIterators
        | CoreModules.ErrorHandling
        | CoreModules.GlobalConsts;

    /// <summary>创建脚本实例并完成沙箱装配。</summary>
    internal static Script Create(LuaHandlerEngine engine)
    {
        var script = new Script(SandboxModules);

        // 交付静默：组件不向控制台/文件产生输出（诊断经结果对象）。
        script.Options.DebugPrint = _ => { };
        script.Options.DebugInput = _ => string.Empty;

        LuaSandbox.Configure(script, engine);
        return script;
    }
}
