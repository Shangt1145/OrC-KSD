namespace Filament;

using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

/// <summary>
/// The curated verb facade: C# functions exposed into every sandboxed script as
/// <c>filament.&lt;namespace&gt;.&lt;name&gt;(...)</c>. This is the deliberate seam over the
/// engine — host code (e.g. Filament.Godot) registers verbs like <c>spawn</c> or
/// <c>play_role</c>; scripts never see the raw engine API.
///
/// Registration is process-wide and idempotent per (namespace, name).
/// <see cref="LuaSandbox.Create"/> installs the current set into each new script.
/// </summary>
public static class SandboxVerbs
{
    public delegate DynValue Verb(ScriptExecutionContext ctx, CallbackArguments args);

    private static readonly object _gate = new();
    private static readonly Dictionary<(string, string), Verb> _verbs = new();

    public static void Register(string @namespace, string name, Verb verb)
    {
        lock (_gate) _verbs[(@namespace, name)] = verb;
    }

    public static void Clear()
    {
        lock (_gate) _verbs.Clear();
    }

    /// <summary>Build the <c>filament</c> global table (namespaced verbs) on a script.</summary>
    public static void InstallInto(Script script)
    {
        List<KeyValuePair<(string ns, string name), Verb>> snapshot;
        lock (_gate)
        {
            if (_verbs.Count == 0) return;
            snapshot = new List<KeyValuePair<(string, string), Verb>>(_verbs);
        }

        var root = new Table(script);
        foreach (var kv in snapshot)
        {
            var (ns, name) = kv.Key;
            var nsTable = root.Get(ns).Type == DataType.Table ? root.Get(ns).Table : new Table(script);
            var verb = kv.Value;
            nsTable.Set(name, DynValue.NewCallback((c, a) => verb(c, a)));
            root.Set(ns, DynValue.NewTable(nsTable));
        }
        script.Globals.Set("filament", DynValue.NewTable(root));
    }
}
