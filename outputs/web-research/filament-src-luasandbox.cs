namespace Filament;

using MoonSharp.Interpreter;

/// <summary>
/// Creates hardened MoonSharp <see cref="Script"/> instances and loads module
/// chunks. The sandbox uses <c>Preset_HardSandbox</c> (no os/io/package/debug/
/// loadstring); only string/math/table/bit32 are available.
/// </summary>
public static class LuaSandbox
{
    public static Script Create()
    {
        var script = new Script(CoreModules.Preset_HardSandbox);
        script.Options.DebugPrint = _ => { };
        SandboxVerbs.InstallInto(script); // expose registered filament.<ns>.<verb> callables
        return script;
    }

    /// <summary>
    /// Run a chunk and return its module table (the <c>return M</c> value). A
    /// parse or top-level runtime error becomes an <c>Err</c> — never a throw.
    /// </summary>
    public static Result<Table, LuaError> LoadModule(Script script, string source, string path)
    {
        DynValue returned;
        try
        {
            returned = script.DoString(source, codeFriendlyName: path);
        }
        catch (InterpreterException ex)
        {
            return Result.Err(new LuaError(LuaErrorKind.RuntimeError, ex.DecoratedMessage ?? ex.Message, path));
        }

        if (returned.Type != DataType.Table)
        {
            return Result.Err(new LuaError(
                LuaErrorKind.RuntimeError,
                $"script must end with `return M` (a module table); got {returned.Type}",
                path));
        }
        return Result.Ok(returned.Table);
    }
}
