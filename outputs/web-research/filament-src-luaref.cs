namespace Filament;

using System;

/// <summary>
/// Parses a <c>"lua:&lt;script&gt;"</c> behavior reference — the data hook that lets
/// TOML/config wire which Lua module backs an entity's behavior, instead of the
/// script path being hardcoded in C#. <c>"lua:enemy/marionette"</c> → the module
/// at logical path <c>enemy/marionette</c>.
/// </summary>
public static class LuaRef
{
    public const string Prefix = "lua:";

    public static bool IsRef(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>True (+ the logical script path) when <paramref name="value"/> is a
    /// non-empty <c>"lua:..."</c> reference.</summary>
    public static bool TryParse(string? value, out string scriptPath)
    {
        if (IsRef(value))
        {
            scriptPath = value!.Substring(Prefix.Length).Trim();
            return scriptPath.Length > 0;
        }
        scriptPath = string.Empty;
        return false;
    }
}
