using MoonSharp.Interpreter;

namespace Orc.Lua;

/// <summary>
/// 组件内部错误工具：
/// - 组件主动从 Lua 侧抛出的错误使用专用异常类型 <see cref="OrcLuaScriptException"/>（携带结构化分类），
///   未捕获路径回到宿主边界时按"异常类型"还原分类——脚本伪造消息文本无法影响分类（防伪造）；
/// - 消息文案非空、不构成对外契约（验收以分类 + 非空断言为准；消息中的类别词仅助于诊断）。
/// </summary>
internal static class OrcLuaErrors
{
    /// <summary>沙箱限制错误（禁用面访问 / string.dump 访问）。</summary>
    internal static OrcLuaScriptException Sandbox(string message) =>
        new(LuaErrorKind.SandboxRestriction, $"[OrcLua:SandboxRestriction] {message}");

    /// <summary>白名单拒绝错误（域未注册访问 / 域写入）。</summary>
    internal static OrcLuaScriptException Whitelist(string message) =>
        new(LuaErrorKind.WhitelistRejected, $"[OrcLua:WhitelistRejected] {message}");

    /// <summary>编组错误（跨边界值转换失败）。</summary>
    internal static OrcLuaScriptException Marshal(string message) =>
        new(LuaErrorKind.MarshallingError, $"[OrcLua:MarshallingError] {message}");

    /// <summary>
    /// 未捕获异常 → 结构化错误分类。
    /// 以"使该次调用失败的最终未捕获错误"为准：仅当异常（或其包装链）为组件内部标记类型时，
    /// 才还原为对应的沙箱/白名单/编组分类（脚本环境无法构造该类型，分类不可伪造）；其余归运行时错误。
    /// </summary>
    internal static LuaError ClassifyUncaught(Exception ex)
    {
        var current = ex;
        for (int depth = 0; current is not null && depth < 8; depth++, current = current.InnerException)
        {
            if (current is OrcLuaScriptException marked)
                return new LuaError(marked.Kind, PickMessage(marked.DecoratedMessage, marked.Message));
        }

        return new LuaError(LuaErrorKind.RuntimeError,
            PickMessage((ex as InterpreterException)?.DecoratedMessage, ex.Message));
    }

    private static string PickMessage(string? decorated, string message)
    {
        var text = string.IsNullOrEmpty(decorated) ? message : decorated;
        return string.IsNullOrEmpty(text) ? "(no message)" : text;
    }
}

/// <summary>
/// 组件标记异常：组件主动从 Lua 侧抛出的脚本错误，携带结构化分类。
/// 类型仅组件内部可见（脚本环境无法构造此类型）；宿主边界按类型还原分类、防脚本伪造。
/// </summary>
internal sealed class OrcLuaScriptException : ScriptRuntimeException
{
    internal OrcLuaScriptException(LuaErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>结构化分类。</summary>
    internal LuaErrorKind Kind { get; }
}
