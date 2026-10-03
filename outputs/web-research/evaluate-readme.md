<p align="center"><img src="logo.png" alt="Evaluate" width="180"></p>

# Evaluate

A data-driven, moddable game framework built as an extension that runs **inside
Godot** (Godot is the host; Evaluate has no `main`). Engine-side code is .NET/C#
(`net10.0`, Godot 4.6-mono). Gameplay is authored in "evt" scripts whose YAML
frontmatter declares a capability-scoped *signature*; a custom loader sandboxes
each script to exactly what it declares. Scripts are auto-discovered and
hot-reloaded by default.

## Architecture

- **Frontmatter → signature.** The leading `---` block is split off and parsed
  as real YAML (YamlDotNet) into `config:`, `apis:`, `register:`, `returns:`,
  `params:`, `require:`, `assets:`, `scenes:`, `properties:`, `behaviors:`,
  `machines:`, `attributes:`, `abilities:` (plus the machine-only
  `name:`/`states:`/`initial:`); the Lua body is handed to the VM verbatim (line
  numbers preserved). The signature *is* the C#↔Lua boundary contract —
  `returns` declares typed, access-scoped members (no Lua type system).
  (`src/Evaluate/Frontmatter.cs`)
- **Per-script sandbox — apis as modules.** Each body runs via
  `load(body, name, "t", env)` on a shared `LuaState`, where `env` holds *only*
  the declared `config.*`/`assets.*`, **each declared `apis:` entry as its own
  bare global table**, the always-available `std`, plus a few safe primitives —
  including the metatable builtins (`setmetatable`/`getmetatable`/`rawget`/
  `rawset`/`rawequal`/`rawlen`) so scripts can define classes the idiomatic Lua
  way. There is **no ambient `godot.*`**: an `apis:` name resolves **framework
  service → host-registered extension → Godot class/enum** (memoized), so
  `apis: [save, Node3D, Timer, Side]` injects the `save` service next to
  `Node3D.new()`, `Timer.TimerProcessCallback.Idle` and `Side.Bottom`. Declaration
  gates the **class table** only — instances reaching a script (`self`, `get_node`,
  signal args, return values) expose members regardless. An unknown `apis:` name is a
  load error (typo-proofing); a legacy `godot:`-prefixed entry errors with a
  migration hint; `ResourceLoader`/`ResourceSaver`/`FileAccess`/`DirAccess` are
  blocked from declaration (assets come from `assets:`, persistence from
  `save`/`sql`), and so is the whole raw-input surface — `Input`, `InputMap`,
  `Key`, `JoyButton`, `JoyAxis`, `MouseButton`, every `InputEvent*` — because input
  is native: subscribe to mapped actions via the `actions` api instead. Undeclared
  globals (`os`, `io`, …) are absent, and `pcall` is intentionally withheld (errors
  surface rather than being swallowed). (`src/Evaluate/Loader.cs`)
- **Host extension apis.** The embedding game registers C# api modules with
  `runtime.RegisterApi("combat_native", implObjectOrStaticType)` **before**
  adding the runtime to the tree; scripts declare `apis: [combat_native]` and
  call the public methods dot-style. Reserved names, collisions with Godot
  classes, duplicate names, and post-load registrations are all rejected.
- **Assets are declared, not loaded.** Frontmatter `assets:` is a **map**
  (`name: "res-relative/path"`; a filename `*` glob binds a stem-keyed table),
  **eagerly loaded at script load** (missing file = load error) and injected as
  the ambient `assets` table — the *only* way a script gets an asset. Hot reload
  is real: a `.gdshader` edit updates **in place** (the same `Shader` instance
  carries the new code, so live `ShaderMaterial`s recompile for free — shaders
  live in their own `.gdshader` files, never inline), while any other resource
  re-loads with cache-replace and its declaring scripts re-run
  `on_unload`/`on_load`.
- **Custom `require`** narrows the returned module to its `returns` contract
  (get/set property → `get_`/`set_` accessors; plain method; read-only hides the
  setter; missing accessor errors). A script may also declare its modules in
  frontmatter — `require:` binds each to a sandbox local (`require: { base:
  "lib/base.evt" }` → `base` usable with no `local base = require(...)` line),
  resolving the same narrowed handle. A **require cycle** (direct or transitive,
  including self-require) is rejected with the offending chain rather than a stack
  overflow, and editing a required module **hot-reloads every consumer** that
  requires it (transitively — systems re-run, live behaviors refresh).
- **Data lives in the engine.** Game objects *are* Godot nodes — declared in scene
  files or created via a declared class module (`Node3D.new()`) / `world` — so
  there is no separate entity system. The Lua handle is a thin proxy whose
  metatable routes reads/writes into the engine. (`src/Evaluate/GodotBinder.cs`)
- **Auto-discovery.** The runtime recursively scans `res://scripts`; any system
  `.evt` with a `register:` block is wired to the Godot lifecycle. A hook may be
  registered **once per scene** (see *Scenes & layers*) — so the same `on_update`
  can be registered in `menu`, `level1`, and globally, each by a different script.
- **Hot reload (default).** A `FileSystemWatcher` watches scripts, scene files,
  configs, frontmatter-declared `assets:`, and `.ability`/`.effect` files;
  changes reload on the main thread. A changed system or behavior script re-runs
  its body and refreshes its hook closures while live nodes persist; a changed
  `*.scene` rebuilds the active scene. (`src/Evaluate/EvaluateRuntime.cs`)
- **Scenes & layers.** Gameplay is split into a persistent **global layer** and a
  swappable **scene layer**, so one program holds many scenes, each with its own
  registered functions:
  - **`global.scene`** (reserved manifest) declares nodes that *never* clear
    plus `start_scene` and (optionally) `controls = "…toml"` — the action map the
    implicit native PlayerController loads (see *Native input* below). Loaded once
    into a persistent **Global root**.
  - **`*.scene`** files (TOML content) declare a node tree as keyed, nested tables —
    `[nodes.Player]` then `[nodes.Player.Camera]` makes Camera a child of Player.
    A node's name is the table key; reserved keys `type`/`behaviors`/`machines`
    (/`script`, the deprecated alias); a **sub-table is a child node** and a
    **scalar/array is a property** (`position = [x, y, z]`). A
    sub-table tagged with **`_type`** is a *property*, not a child — it names an inline
    resource (`mesh = { _type = "BoxMesh", size = [1,1,1] }`) **or a builtin struct**
    (`custom_aabb = { _type = "Aabb", position = [1,2,3], size = [4,5,6] }`), which is how
    composite structs like `Transform3D`/`Rect2`/`Basis`/`Projection` are written. A
    property key may be quoted to carry a `/` (Control theme overrides:
    `"theme_override_colors/font_color" = [..]`). An optional top-level
    **`description = "…"`** holds scene docs as data. Parsed with Tomlyn; instantiated
    under a per-scene container that is **freed wholesale** on switch. The editor addon
    can round-trip all of this — including `behaviors`/`machines` lists — back to the
    `.scene` (see below).
  - **`*.behavior.evt`** is node behavior, attached via the scene's
    `behaviors = ["path", { script = "path", params = {…} }]` list; its hooks run with
    **`self`** bound to that node (no spawning in the script). A **node holds N
    behaviors** (hooks fire in attachment order) and **one behavior file drives many
    nodes**, each attachment with its own params. A behavior's own frontmatter
    `behaviors:`/`machines:` lists compose further attachments onto the same node
    (depth-first, deduped per node). `*.node.evt` + `script =` remain as the
    single-behavior alias (deprecated; new code uses behaviors). A behavior may
    declare a **`params:`** block — typed, per-instance values the scene supplies and
    the body reads through the ambient **`params`** table; the per-node analogue of
    `config` (which is shared). Entries are `name: <default>` (type inferred),
    `name: <type>` (required), or `name: "<type> = <default>"`; types include **`dna`**
    — a hand-authored 64-bit identity hash (`"0x"` + exactly 16 hex digits, never
    framework-generated) the body reads as `params.<name>:trait(1..16)` (nibbles,
    MSB-first) / `:hex()`. The loader fills defaults, type-checks each supplied value,
    and rejects an undeclared or missing-required param — the same
    signature-is-the-contract stance as the sandbox. Stashed as `__evt_params` so the
    editor round-trips it. A behavior may also declare **`properties:`** — native Godot
    properties applied to `self` at attach and re-applied on script hot reload; the
    scene's own property keys always win, and an unknown property name is a load error.
    Convention: static initial engine state lives in the frontmatter/scene, not the body.
  - **`*.statemachine.evt`** is a declarative FSM attached via `machines = [...]` (or a
    script's `machines:` frontmatter). Frontmatter `name:` (default file stem),
    `states:` (required), `initial:` (default first); the body **returns the ordered
    transition list** — `{ from, to, when = fn(self) | on = "event" | after = seconds
    [, run = fn(self, from, to)] }` (`do` is a Lua keyword). Guards are polled per
    physics tick in order (first match wins, max one transition/tick); `:fire("event")`
    is immediate (reentrant fires defer a tick); `from = "*"` is a wildcard. Behaviors
    react via the node surface `self.fsm.<name>` — `.state`, `:is(s)`, `:fire(evt)`,
    `:on_exit(state, fn(to))`, and `self.fsm.<name>.<state> = fn(from)` *appends* an
    enter listener. Hot reload: a machine edit keeps state + listeners; a behavior
    reload drops that behavior's stale listeners (re-subscribe in `on_load`).
    (`src/Evaluate/StateMachine.cs`)
  - **GAS-lite** (`src/Evaluate/Abilities.cs`): node-attached scripts declare
    **`attributes:`** — per-node pools (`base/min/max/regen/regen_delay/recover`) with
    built-in stamina semantics: regen per second after `regen_delay` since the last
    spend; a spend that drains to `min` **exhausts** the pool (tag `exhausted:<name>`)
    until it regens back to `recover` — and **`abilities:`** — `*.ability` files (TOML:
    `cooldown`, `channeled` with per-second cost, `cost = { attribute, amount }`,
    `tags`/`block_tags`/`grant_tags`, `[[effects]]` blocks) granted at attach.
    `*.effect` files (TOML: dotted `attribute` field targets like `"stamina.max"`,
    `op = add|mul|set`, `magnitude`, `duration` 0 = instant / −1 = while-active / >0 =
    timed, `period` for dots) mutate or modify pools. Lua surface:
    `self.attributes.<name>` (modifier-aware clamped read; assignment = clamped hard
    set), `:max(n)`, `:has(n)`; `self.abilities:grant/activate/deactivate/is_active/
    can_activate/cooldown/has_tag/apply/on_ended`. `.ability`/`.effect` files
    hot-reload live.
  - **`*.evt`** systems are conductors: no `scenes:` ⇒ **global** (always run);
    `scenes: [a, b]` ⇒ active only while `a`/`b` is current. The `scene` API does
    routing — `scene.change(name[, ctx])` (applied at the next frame boundary, never
    mid-hook; `ctx` is a table carried to the destination), `scene.current()`,
    `scene.find(path)` (unique by node path, e.g. `"Level/Enemy"`), `scene.add(node)`,
    `scene.list()`, plus the scene STACK — `scene.push(name[, ctx])` overlays a scene
    (the one beneath freezes: engine processing off, no hooks, still rendered — a pause
    menu over the live world), `scene.pop()` thaws it back (controller scenario +
    possession restore to their push-time values), `scene.stack()` lists the layers,
    and `scene.context()` returns the current transition's context (caller keys +
    `from`/`to`/`reason`). `scene.change` clears the whole stack.
  - **Scene-level `[player]` spawner.** A scene that wants a possessed player declares
    `player = { node = "<fragment>", spawn = "<module.evt>" }` at top level: the
    single-root fragment scene is instantiated on entry, placed by the module's
    exported `spawn(ctx) -> { x, y, z, facing? }` (ctx = the transition context — a
    loading zone passes `{ entry = ... }` through `scene.change`), and auto-possessed
    by the native controller; an optional top-level `scenario = "…"` switches the
    controller scenario on entry. Hot-reload rebuilds of the active scene preserve the
    live player transform (spawn scripts don't re-run).
- **Native input — the PlayerController + `actions`.** Raw input never reaches scripts:
  the `input` service and `on_input` hook are gone, and the input classes (`Input`,
  `Key`, `JoyButton`, `InputEvent*`, …) are blocked from `apis:`. Instead the manifest
  declares `controls = "…toml"` and an implicit **`PlayerController`** node in the
  global layer maps devices → ACTIONS: TOML sections are SCENARIOS (`[Gameplay]`,
  `[Menu]`, the always-active `[Always]`, `[settings]` for deadzone/threshold), keys
  are binding tokens (`Button_a`, `Key_space`, `Stick_left`, `Axis_trigger_right`,
  `Mouse_left`; digital keys feed vector axes via `"Move+x"` suffixes), values are
  action names. **Keyboard layouts**: declare `layouts = ["qwerty", "dvorak"]` in
  `[settings]` and scope bindings with `[Scenario.<layout>]` sub-blocks; the active
  layout persists in the save DB (`controller.layout(name)` / `layouts()`). Devices
  are polled once per physics tick (before any
  `on_physics_update`); scripts declare `apis: [actions]` and use
  `actions.<Scenario>.<Action>` — `subscribe{ on = "press|release|tap|held",
  after = seconds, run = fn }` (a `cancel()` handle back; hot-reload- and
  scene-lifetime-safe) or the live `down`/`value`/`vector` reads. The `controller` api
  adds `scenario()`, `possess()`/`possessed()`, save-DB rebinds
  (`rebind`/`overrides`/`reset_overrides` — a `control_overrides` table applied over
  the TOML), `rumble`, and `capture_text` (menu typing without raw key access).
  (`src/Evaluate/Controls.cs`, `src/Evaluate/PlayerController.cs`)
- **`store` — global session state.** `store.set/get/has/delete/keys(prefix)` +
  `store.subscribe(key_or_"prefix.", fn(key, new, old))`: values live in the global
  layer and survive every scene switch/push/pop; nothing touches disk (durable saves
  stay `save`/`sql`). Subscriptions are owner-attributed and die with their scene
  layer. (`src/Evaluate/Store.cs`)
- **Editor preview + write-back (optional addon).** A `.scene` file is TOML, which the
  Godot editor doesn't render natively. The `dev/addons/evaluate_scene` addon registers an
  `EditorImportPlugin` that converts `.scene` → a `PackedScene` (via the *same*
  `SceneFile`/`SceneBuilder` the runtime uses), so the editor shows and renders the node
  tree. It also adds an **"Evaluate" dock** (and Tools-menu entries) to open a `.scene` as
  an *editable* native scene, edit it with the normal editor — move nodes, create and place
  new nodes, set properties, attach a node script via a `__evt_script` metadata entry — and
  **Save to .scene**, which serializes the edited tree back to the TOML file via
  `SceneWriter` (the inverse of `SceneFile`/`SceneBuilder`). Editor-only (`#if TOOLS`) and
  needs no running game (a running game's hot-reload picks the saved file up for free). The
  addon is **not** part of the NuGet package (the runtime library carries no editor types);
  instead it ships as a drop-in zip on each [GitHub release](https://github.com/radical-beard/evaluate/releases)
  (`evaluate_scene-<version>.zip`): extract it into your project to get
  `res://addons/evaluate_scene/`, then enable the plugin in Project Settings (it needs the
  `RadicalBeard.Evaluate` package for the build/serialize logic). A C# editor addon can't be a
  bare DLL — its `.cs` must live under `res://addons/` and compile with the game — so the zip
  carries the source; the stable `Evaluate.Editor` namespace means it drops in verbatim.
- **Lifecycle hooks.** `register:` wires Godot's Node lifecycle. **System hooks:**
  `on_start` (global, once), `on_load`/`on_unload` (every (re)load / before reload),
  `on_enter`/`on_exit` (scene-scoped, per activation), `on_update(dt)`,
  `on_physics_update(dt)`, `on_focus_in`/`on_focus_out`,
  `on_pause`/`on_resume` (app pause AND scene-stack freeze/thaw), `on_quit`.
  **Behavior hooks** (`*.behavior.evt` / `*.node.evt`, with `self`): `on_attach`
  (once, at first attach), `on_load`/`on_unload`, `on_update`, `on_physics_update`,
  `on_exit`, `on_quit`, plus the same focus/pause pairs. (`on_input` was removed in
  0.11.0 — input arrives as mapped actions.) Machines register no hooks — they
  are polled data.
- **`std.*` standard library.** Real C#-backed types via the `[LuaObject]` source
  generator — `std.vec3`, `std.vec2`, `std.color`, `std.vector`,
  `std.linked_list` (`src/Evaluate/Std.cs`).
- **Persistence (`save`).** SQLite-backed runtime/player data in Godot's
  per-project `user://` directory (the platform-native, per-game path Steam Cloud
  syncs from) — `save.set/get/delete` (`src/Evaluate/Persistence.cs`).
- **Godot binding (declared class modules).** Any Godot class/enum name in
  `apis:` resolves to a bound module table (`src/Evaluate/GodotBinder.cs`):
  - **instances** — `Node3D.new()` (Activator); member access routes
    through the engine ClassDB (`GodotObject.Call/Get/Set`), not reflection.
    Instances are `ILuaUserData`, so they pass back into the engine
    (`world:add_child(node)`);
  - **properties** — `node.position = std.vec3(…)` / `node.position`;
  - **signals** — `node:connect("timeout", fn)` (0–6 args, dispatched on the
    main thread) and `node:emit_signal(...)`;
  - **enums/constants** — `Key.Space`, `Timer.TimerProcessCallback.Idle`;
  - **marshalling** — exhaustive: primitives, strings, `NodePath`, objects,
    `Array`/`Dictionary`; rich C#-backed `Vector2/3` & `Color` (↔ `std.*`); and
    every other struct (`Vector4`, `Vector2I/3I`, `Quaternion`, `Rect2`, `Plane`,
    `Aabb`, `Basis`, `Transform2D/3D`) + packed arrays as named-field/list tables.
    Reads are tables; writes are **target-type-aware** — assigning a table to a
    struct property builds the exact struct (so `node.transform = t` round-trips);
  - **statics** — reflection fallback, or a **pre-baked** zero-reflection binding.
- **Pre-bake (source generator).** `generator/` is a Roslyn
  `IIncrementalGenerator`: `[BindGodot(typeof(Godot.OS))]` emits `OsBinding.Create()`
  with direct, reflection-free calls (53 bound `OS` methods), filtered to
  Lua-convertible signatures. The binder prefers pre-baked bindings.
- **API spec & agent skill (consumer onboarding).** `godot --headless --path . --
  --emit-api <dir>` dumps the *entire* Lua surface a script sees — read live, so nothing is
  hand-written or hard-coded: the declarable Godot class modules (methods/properties/signals)
  come from engine `ClassDB` introspection, enums/constants/statics + the class set from
  GodotSharp reflection, `std.*` from the `[LuaObject]` types, the capability + host apis by
  walking the tables `Loader` builds, and hooks/frontmatter from the runtime's arrays
  (`src/Evaluate/EvaluateDocs.cs` — the runtime analogue of the build-time generator). It
  emits `evaluate-api.json`, LuaCATS `---@meta` files for IDE autocomplete (the whole
  class-module/`std.*` surface), and a Markdown reference. A pre-generated copy plus a
  drop-in **agent skill** (teaching an LLM the script contract) live under `downloads/`;
  each release also ships `evaluate-downloads-<version>.zip`.

## Base VM

[Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) (`LuaCSharp` 0.5.5, Lua 5.2).
Source-generator interop (`[LuaObject]`) → AOT-clean, low-allocation. Its API is
async-only; we bridge to sync for Godot's `_Process` (safe because script calls
don't actually await). Dialect note: scripts use `x = x + …`, not the planned
custom dialect's `+=`.

## Run

    godot --headless --path dev                       # demo: global layer, scene switch (menu -> level1), behaviors
    godot --headless --path dev -- --test             # enforcement suite (115 tests)
    godot --headless --path dev -- --quit-after 8     # demo, then quit after 8 frames
    godot --headless --path dev -- --emit-api out/    # dump the full Lua API spec (json + LuaCATS + markdown)

## Layout

    README.md  CHANGELOG.md  Directory.Build.props  Evaluate.slnx  global.json  LICENSE.md
    src/Evaluate/            Evaluate.csproj  EvaluateRuntime.cs  Loader.cs  Std.cs
                             Frontmatter.cs  Toml.cs  SceneFile.cs  SceneBuilder.cs  SceneWriter.cs
                             StateMachine.cs  Abilities.cs  Sql.cs  GodotBinder.cs  GodotStructCodec.cs
                             Persistence.cs  BindGodotAttribute.cs  Prebaked.cs  EvaluateTests.cs
                             EvaluateDocs.cs  EvaluateDocsModel.cs  EvaluateDocsWriters.cs   (--emit-api spec generator)
    src/Evaluate.Generator/  Evaluate.Generator.csproj  BindGodotGenerator.cs   (Roslyn source generator)
    downloads/  drop-in resources for consumers: spec/ (generated Lua API spec — json + LuaCATS
                + markdown) and skill/evaluate-scripting/ (a downloadable agent skill)
    editors/vscode/  VS Code extension for .evt: YAML+Lua highlighting, frontmatter IntelliSense,
                and sandbox-aware Lua-body autocomplete via lua-language-server + the LuaCATS spec
    editors/nvim/    Neovim plugin for .evt: the same filetype/highlighting, frontmatter
                IntelliSense (completion/diagnostics/hover) and Lua-body lua-language-server wiring
    dev/        the demo / enforcement-test harness game (consumes the lib by project reference):
      project.godot  main.tscn  EvaluateHost.cs  Dev.csproj
      scripts/  global.scene  menu.scene  level1.scene
                player.node.evt  enemy.node.evt (per-node params)  showcase.evt  menu.evt  level1.evt
                lib/mathx.evt  game.toml  player.toml
      addons/evaluate_scene/  editor import plugin (.scene -> PackedScene preview)
      tests/    enforcement-suite fixtures (.evt/.scene/.gdshader) used by EvaluateTests.cs:
                forbidden_global / undeclared_api / missing_accessor / readonly_module /
                metatable_oop / require_* / scene_* / system_* / self_node / sql_probe /
                struct_read + assets/ (glob/hot-reload shaders), + global.scene
    artifacts/  packed .nupkgs (gitignored)

## Remaining work toward "full featured"

- **Godot coverage is complete**: instance + static members (any type), all
  Variant structs + packed arrays, enums/constants, and signals — all two-way.
  Static methods with struct/packed signatures bind via the reflection fallback
  (layered over the pre-baked primitives). Instance access is reflection-free
  (engine ClassDB `Call/Get/Set`). Remaining (perf only): extend the source-gen
  pre-bake to struct/packed static signatures so they too avoid reflection.
- **Frontmatter LSP** (deferred): editor diagnostics for the signature block —
  e.g. red-underline an unknown `apis:` entry or missing config file *as you
  type* (the loader already rejects both at load). The Lua body itself rides
  the standard Lua LSP.
- **AOT export**: built (0.12.0) — trim/AOT-analyzer-clean library, embedded
  trimming roots, NativeAOT proof harness (`tools/AotSmoke`), hot-reload
  auto-off in exported builds, iOS-safe SQLite (bundle_green), Android/iOS
  export presets + the `mobile-smoke` CI lane. Consumer guide: `EXPORTING.md`.
  Mod packaging/**distribution** (runtime-loading third-party scripts from
  outside the PCK): still desktop-only, not yet built.
- Out of scope by decision: a Lua type system (signature contract is enough) and
  sandbox resource limits / DoS protection (modding is free).
