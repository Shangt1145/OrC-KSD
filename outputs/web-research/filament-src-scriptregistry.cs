namespace Filament;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Loads <c>*.lua</c> modules under a root directory and hot-reloads them on
/// edit. Engine-agnostic: filesystem events are queued off-thread, but the
/// actual chunk swap happens when the host calls <see cref="Pump"/> (each frame
/// in Godot, each tick in a console/test), so MoonSharp is only ever touched
/// from the host thread and reload is deterministically testable.
///
/// Parse/runtime errors on an edit keep the previous chunk live (last-good-state)
/// and surface a <see cref="ScriptStatusKind.Failed"/> status.
/// </summary>
public sealed class ScriptRegistry : IDisposable
{
    private readonly string _root;
    private readonly double _debounceSeconds;
    private readonly object _lock = new();
    private readonly Dictionary<string, LuaModule> _modules = new();
    private readonly HashSet<string> _pending = new();
    private FileSystemWatcher? _watcher;
    private double _debounceRemaining;

    public event Action<ScriptStatus>? StatusChanged;

    public ScriptRegistry(string luaRoot, double debounceSeconds = 0.15)
    {
        _root = Path.GetFullPath(luaRoot);
        _debounceSeconds = debounceSeconds;
    }

    /// <summary>Number of currently loaded modules.</summary>
    public int Count
    {
        get { lock (_lock) return _modules.Count; }
    }

    /// <summary>Load every <c>*.lua</c> under the root and (if requested) start
    /// watching for edits. Returns the count loaded, or an <c>Err</c> if the
    /// root doesn't exist.</summary>
    public Result<int, LuaError> Initialize(bool liveReload = true)
    {
        if (!Directory.Exists(_root))
            return Result.Err(LuaError.RegistryNotReady($"lua root does not exist: {_root}"));

        foreach (var file in Directory.GetFiles(_root, "*.lua", SearchOption.AllDirectories))
            LoadOrReload(file, initial: true);

        if (liveReload) StartWatcher();
        return Result.Ok(Count);
    }

    public Option<LuaModule> GetModule(string logicalPath)
    {
        lock (_lock)
            return _modules.TryGetValue(logicalPath, out var m) ? Option.Some(m) : Option<LuaModule>.None;
    }

    /// <summary>Drive pending reloads. Call once per frame/tick with the elapsed
    /// time; reloads fire after the debounce window elapses.</summary>
    public void Pump(double delta)
    {
        List<string>? toProcess = null;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            _debounceRemaining -= delta;
            if (_debounceRemaining > 0) return;
            toProcess = new List<string>(_pending);
            _pending.Clear();
        }
        foreach (var file in toProcess) LoadOrReload(file, initial: false);
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_root, "*.lua")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Enqueue(e.OldFullPath);
        Enqueue(e.FullPath);
    }

    private void Enqueue(string fullPath)
    {
        lock (_lock)
        {
            _pending.Add(fullPath);
            _debounceRemaining = _debounceSeconds;
        }
    }

    private void LoadOrReload(string fullPath, bool initial)
    {
        var logical = Logical(fullPath);

        if (!File.Exists(fullPath))
        {
            bool removed;
            lock (_lock) removed = _modules.Remove(logical);
            if (removed) Raise(ScriptStatusKind.Removed, logical);
            return;
        }

        string source;
        try
        {
            source = File.ReadAllText(fullPath);
        }
        catch (IOException ex)
        {
            Raise(ScriptStatusKind.Failed, logical, $"read failed: {ex.Message}");
            return;
        }

        var script = LuaSandbox.Create();
        var loaded = LuaSandbox.LoadModule(script, source, logical);
        if (loaded.TryGetError(out var err))
        {
            Raise(ScriptStatusKind.Failed, logical, err.Message); // last-good chunk stays live
            return;
        }
        loaded.TryGet(out var table);

        LuaModule? existing;
        lock (_lock) _modules.TryGetValue(logical, out existing);

        if (existing is not null)
        {
            existing.Replace(script, table);
            if (!initial) Raise(ScriptStatusKind.Reloaded, logical);
        }
        else
        {
            var module = new LuaModule(logical, script, table);
            lock (_lock) _modules[logical] = module;
            Raise(ScriptStatusKind.Loaded, logical);
        }
    }

    private string Logical(string fullPath)
    {
        var rel = Path.GetRelativePath(_root, Path.GetFullPath(fullPath)).Replace('\\', '/');
        return rel.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? rel.Substring(0, rel.Length - 4)
            : rel;
    }

    private void Raise(ScriptStatusKind kind, string path, string? message = null)
        => StatusChanged?.Invoke(new ScriptStatus(kind, path, message));

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
