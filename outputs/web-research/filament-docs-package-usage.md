# Package Usage

Public NuGet package IDs use the `RadicalBeard.Filament.*` prefix. The C#
namespaces remain `Filament` and `Filament.Config`.

Use `RadicalBeard.Filament.Core` for engine-free Lua behavior scripting:

```sh
dotnet add package RadicalBeard.Filament.Core --version 0.1.0
```

Then define scriptable records in your game/tool assembly:

```csharp
using Filament;

[Scriptable]
public partial record PolicyInput(float HpFraction, float Distance);
```

The `RadicalBeard.Filament.Core` package includes the analyzer/source generator
that registers the converter for `PolicyInput` at module-load time. You do not
need to reference `Filament.SourceGen` directly when using packages.

Use `RadicalBeard.Filament.Config` for TOML world files:

```sh
dotnet add package RadicalBeard.Filament.Config --version 0.1.0
```

Use `RadicalBeard.Filament.Godot` for Godot 4.6 .NET projects:

```sh
dotnet add package RadicalBeard.Filament.Godot --version 0.1.0
```

`RadicalBeard.Filament.Godot` depends on `RadicalBeard.Filament.Core`, so the
source generator arrives through the package graph once. The package smoke test
verifies this to avoid duplicate generator execution.
