# Scripting Model

Filament scripts are Lua modules. Each file returns a table of functions:

```lua
local M = {}

function M.describe(p)
  if p.hp_fraction < 0.5 then
    return "frenzied"
  end
  return "calm"
end

return M
```

C# calls a method through `LuaModule`:

```csharp
var result = module.Call<string, PolicyInput>("describe", input);
```

Failures cross the boundary as `Result<_, LuaError>` values. Parse or runtime
errors during hot reload keep the last good script live.

## Sandbox

`LuaSandbox.Create()` uses MoonSharp's hard sandbox preset. Scripts do not get
raw `os`, `io`, `package`, `debug`, or dynamic loading APIs. Hosts expose engine
actions through explicit `SandboxVerbs.Register(...)` calls, which appear in Lua
under `filament.<namespace>.<verb>`.

## Marshalling

`[Scriptable]` records become plain Lua tables with snake_case keys. Use
`Option<T>` instead of nullable references for optional fields. Lists are encoded
as 1-based Lua arrays.
