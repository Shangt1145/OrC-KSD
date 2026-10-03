# Filament

Filament is a small .NET framework for data-driven game behavior: C# owns the
engine boundary, Lua owns fast-iteration behavior logic, and TOML owns world
configuration.

The repository is ready for source checkout use, public NuGet packages,
local/private NuGet feeds, and agent-assisted playtest loops.

## What is included

- `Filament.Abstractions`: `[Scriptable]`, `[ScriptMember]`, `Result`,
  `Option`, `LuaError`, and `LuaRef`.
- `Filament.Core`: hardened MoonSharp Lua modules, hot reload, last-good script
  fallback, C# to Lua marshalling, Lua to C# return coercion, and sandbox verbs.
- `Filament.SourceGen`: analyzer/source generator for `[Scriptable]` converters.
- `Filament.Config`: TOML world parsing, normalized entity props, typed prop
  access, and world diffs.
- `Filament.Godot`: Godot 4.6 .NET adapter and Godot value-type marshalling.
- `demo/`: console demo for Lua loading, calls, and hot reload.
- `playtest/`: MCP server that launches a build and collects structured human
  playtest feedback through a local browser survey.

## Requirements

- .NET SDK 10.0.100 or newer in the 10.0 line.
- Godot 4.6 .NET only if you use `Filament.Godot`.
- Python 3.11+ and `uv` only if you use the playtest MCP server.

The repo includes `global.json` so `dotnet` resolves a compatible 10.0 SDK.

## Quick start

```sh
dotnet restore filament.slnx
dotnet test filament.slnx
dotnet run --project demo/Filament.Demo.csproj -- --ticks 3
```

Run without `--ticks` to keep the demo alive for hot reload. From a source
checkout, the demo loads `demo/lua/policy.lua`; while it is running, edit that
file and watch the output change. Standalone binary launches fall back to the
copied `lua/` directory beside the executable.

## One-command verification

Any platform with .NET:

```sh
dotnet run --project tools/Filament.Verify/Filament.Verify.csproj
```

macOS/Linux:

```sh
scripts/verify.sh
```

Windows or any machine with PowerShell 7:

```powershell
./scripts/verify.ps1
```

These run the same .NET verifier: build, format check, tests, demo smoke,
playtest-agent smoke, package packing, and fresh package-consumer checks.

## Use from NuGet

Install the package that matches the layer your project needs:

```sh
dotnet add package RadicalBeard.Filament.Core --version 0.1.0
```

Use `RadicalBeard.Filament.Core` for engine-free Lua behavior scripting. It
includes the `[Scriptable]` analyzer/source generator, so package consumers do
not reference `Filament.SourceGen` directly.

```sh
dotnet add package RadicalBeard.Filament.Config --version 0.1.0
```

Add `RadicalBeard.Filament.Config` when you need TOML world files, normalized
entity props, typed prop access, or world diffs.

```sh
dotnet add package RadicalBeard.Filament.Godot --version 0.1.0
```

Use `RadicalBeard.Filament.Godot` for Godot 4.6 .NET projects. It depends on
`RadicalBeard.Filament.Core`, so the source generator arrives once through the
package graph.

The C# namespaces remain `Filament` and `Filament.Config`:

```csharp
using Filament;
using Filament.Config;
```

## Use from a source checkout

For a game or tool in the same repository, reference the runtime project you
need plus the source generator as an analyzer:

```xml
<ProjectReference Include="path/to/Filament.Core/Filament.Core.csproj" />
<ProjectReference Include="path/to/Filament.SourceGen/Filament.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

For Godot:

```xml
<ProjectReference Include="path/to/Filament.Godot/Filament.Godot.csproj" />
```

Add `ScriptRegistryNode` near the root of a scene and keep Lua behavior modules
under `res://lua`.

## Pack locally

```sh
scripts/package-smoke.sh
```

See `docs/distribution.md` for the verification command.

For usage examples, see `docs/package-usage.md`, `docs/scripting.md`, and
`docs/godot.md`.

## Agent playtest MCP

This repo includes `.mcp.json` for MCP clients that support project-level MCP
configuration. It registers `filament-playtest` with:

```sh
uv run --script playtest/server.py
```

The tool accepts a `launch_dir` containing `filament.toml`; the root
`filament.toml` launches the console demo. See `docs/agent-setup.md` and
`playtest/README.md`.

## Development checks

macOS/Linux:

```sh
scripts/verify.sh
```

Windows/PowerShell:

```powershell
./scripts/verify.ps1
```
