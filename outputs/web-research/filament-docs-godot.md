# Godot Integration

`Filament.Godot` targets Godot 4.6 .NET and provides:

- `ScriptRegistryNode`, a scene node that initializes and pumps `ScriptRegistry`.
- `GodotMarshal` for `Vector2`, `Vector3`, and `Color` in `[Scriptable]` types.

## Setup

Add the package or project reference, then add `ScriptRegistryNode` near the root
of a scene. By default it loads Lua modules from:

```text
res://lua
```

Resolve modules by logical path:

```csharp
if (registryNode.Module("enemy/marionette").TryGet(out var module))
{
    var result = module.Call<Decision, EnemyState>("tick", state);
}
```

Godot integration smoke coverage currently verifies package compilation and
value-type marshalling. A live Godot editor/runtime test should be added when a
sample Godot project is introduced.
