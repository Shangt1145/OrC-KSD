# 研究-Filament.Core：C#/.NET 生态中的「Filament（Filament.Core）」调研

> 调研日期：2026-10-03 · 方法：联网检索（百度/Bing）+ GitHub & NuGet 官方 API + 仓库源码/文档逐文件审阅
> 证据等级：**【确证】**＝官方一手来源（仓库文件 / NuGet API / GitHub API 实测）；**【高置信】**＝一手文件直接推断；**【未确认】**＝未能核实，勿依赖

---

## 0. 结论速览（TL;DR）

1. **名称确证**：「Filament（Filament.Core）」= GitHub 项目 **[radical-beard/filament](https://github.com/radical-beard/filament)** 及其核心模块 **Filament.Core**；其 NuGet 包 ID 为 **`RadicalBeard.Filament.Core`**（当前仅 0.1.0）。C# 命名空间为 `Filament`。它与 MoonSharp 的关系不是 fork，而是**对 MoonSharp 2.0.0 的加固封装（hardened wrapper）**。**【确证】**
2. **定位**：小型 .NET「数据驱动游戏行为」框架——*C# 拥有引擎边界、Lua 负责快速迭代的行为逻辑、TOML 负责世界配置*；**Engine-free（引擎无关）**，不依赖 Unity/Godot。**【确证】**
3. **Lua 能力**：依托 **MoonSharp 2.0.0**（纯 C# 托管解释器、零原生依赖）提供：硬沙箱（`Preset_HardSandbox`）、热重载、last-good 脚本回退、C#↔Lua 编组、脚本分发（script dispatch）、沙箱动词白名单（`filament.<ns>.<verb>`）。**【确证】**
4. **.NET 8 可行性**：✅ **可在纯 .NET 8 类库（无 Unity / 无 Godot）中引用并执行 Lua**。`Filament.Core` / `Filament.Abstractions` / `Filament.Config` 目标框架均为 **netstandard2.1**（csproj 注释原文：*"netstandard2.1 so a net8 Godot game (and Filament.Godot) can consume it"*）；`Filament.Godot` 为 net8.0；仓库自带一个**非 Godot 控制台 demo** 验证完整链路。**【确证】**
5. **重大采用风险**：⚠️ **许可证为 all-rights-reserved 占位符**——*"No license is granted by this file. You may use, copy, modify, or distribute this software only with permission from the copyright holder."*；项目文档同时注明这是*待替换的保守占位*（"Replace it before public open-source distribution"），**尚未替换**。凡未获授权，不得用于商业/公开分发用途。**【确证】**
6. **成熟度**：0.1.0 单版本（2026-06-02 发布）；GitHub 0 star、无 Releases；**未检索到任何第三方公开使用案例/教程**；仓库最后一次 push 为 2026-06-02（距今约 4 个月）。**【确证】**

---

## 1. 身份确认（对应委托 Q1）

### 1.1 是什么

- **GitHub**：https://github.com/radical-beard/filament（owner：个人账号 `radical-beard`，User 类型）。创建于 2026-05-27。**【确证】**
- **官方描述**（README）：*"Filament is a small .NET framework for data-driven game behavior: C# owns the engine boundary, Lua owns fast-iteration behavior logic, and TOML owns world configuration."* **【确证】**

### 1.2 项目结构（仓库根目录实测）

| 模块 | 角色 | NuGet 包 ID | 目标框架 |
|---|---|---|---|
| Filament.Abstractions | `[Scriptable]`/`[ScriptMember]` 属性、`Result`/`Option`/`LuaError`/`LuaRef`/`Unit`（零依赖：无 MoonSharp、无 Godot） | `RadicalBeard.Filament.Abstractions` | netstandard2.1 |
| **Filament.Core** | **加固的 MoonSharp Lua 模块、热重载、last-good 回退、C#→Lua 编组、Lua→C# 返回值强转、沙箱动词** | **`RadicalBeard.Filament.Core`** | netstandard2.1 |
| Filament.SourceGen | 编译期 source generator（`[Scriptable]` 转换器），以 analyzer 形式打进 Core 包 | （不单独消费） | netstandard2.0 |
| Filament.Config | TOML 世界配置解析、实体属性归一化、类型化属性访问、world diff | `RadicalBeard.Filament.Config` | netstandard2.1 |
| Filament.Godot | Godot 4.6 .NET 适配器 + Godot 值类型（Vector2/3、Color）编组 | `RadicalBeard.Filament.Godot` | net8.0 |
| demo/ | **非 Godot 控制台 demo**：加载 Lua、调用、热重载演示 | — | — |
| playtest/ | MCP server（launches build + browser survey 收集真人测试反馈），与引擎逻辑无关 | — | — |

（来源：仓库根 contents API 实测 + 各模块 csproj + README + CHANGELOG）

### 1.3 与 MoonSharp 的关系

**不是 fork / 衍生项目，而是「依赖 + 加固封装」：**
- `Filament.Core.csproj` 依赖：`<PackageReference Include="MoonSharp" Version="2.0.0" />`，注释：*"Pure-managed Lua interpreter (no native deps) — the locked runtime."* **【确证】**
- README/CHANGELOG 用语：*"hardened MoonSharp Lua modules"*、*"engine-free Lua sandboxing"*。**【确证】**
- 备注：MoonSharp 本身是纯 C# 编写的 Lua（5.2 语义）解释器（非 native 绑定）；其 NuGet 最新版 2.0.0 发布于 **2016-10-14**；GitHub 仓库 moonsharp-devs/moonsharp 仍在零星维护（最近 push 2026-08-04，1620 stars）。**【确证】**

### 1.4 是否面向「游戏社区 mod/DIY 脚本场景」

- 设计要素直指该场景：**能力域沙箱**（社区脚本只能看到宿主显式注册的 `filament.*` 动词，"scripts never see the raw engine API"）、**热重载**（编辑 .lua 文件即时生效）、**last-good 回退**（社区脚本写坏了自动保留上一版可用脚本）、**数据绑定**（`"lua:<module>"` 引用写进 TOML，让数据指定行为模块）。**【确证】**
- 同作者的关联包 `RadicalBeard.Evaluate` 的 NuGet tags 含 **`moddable`**，描述 *"Gameplay is authored in sandboxed Lua (.evt) scripts + TOML — no engine recompiles"*。**【确证】**（见 §2.4 关联项目）
- 结论：**面向定位明确（面向游戏 mod/DIY/数据驱动脚本），但目前没有公开的第三方社区采用案例**（见 §5）。

---

## 2. 项目状态（对应委托 Q2）

### 2.1 维护活跃度

| 指标 | 数值 | 备注 |
|---|---|---|
| GitHub 创建 | 2026-05-27 | |
| 最后一次 push | **2026-06-02** | 距今约 4 个月（截至 2026-10-03），之后无提交 |
| Stars / Forks | **0 / 0** | 无外部关注 |
| GitHub Releases | **无**（空数组） | 只有 NuGet 发布 |
| Open Issues | 1 | |
| 开发要求 | .NET SDK **10.0.100+**（仓库 global.json） | 仅开发环境要求；**产物的 TFM 是 netstandard2.1/net8.0** |

### 2.2 许可证 ⚠️

- GitHub API 显示 license = *"Other"（NOASSERTION）*；仓库 `LICENSE.md`（395 字节）全文核心：**"No license is granted by this file. You may use, copy, modify, or distribute this software only with permission from the copyright holder. This placeholder is intentionally conservative for private distribution."** **【确证】**
- `docs/readiness.md` 的「Known non-blocking release choice」明确：*"The package license file is conservative and all-rights-reserved. **Replace it before public open-source distribution** if a permissive license is desired."*——即**作者自己知道并预留了这一待办，但尚未处理**。**【确证】**
- **含义**：代码公开可见 ≠ 授权使用。在许可替换前，该库（含 NuGet 包）**不可假定可自由使用/修改/分发**；严谨做法是联系作者取得许可或在技术上仅作参照实现。**这是采纳评估中的第一风险项。**

### 2.3 发布渠道与版本

- **NuGet.org**：`RadicalBeard.Filament.Abstractions` / `.Core` / `.Config` / `.Godot`，均 **0.1.0**，发布于 **2026-06-02**（catalog 时间戳实测）。下载量分别约 224 / 148 / 123 / 136（截至 2026-10-03）。**【确证】**
- 源码 checkout、本地/私有 NuGet feeds（README 明示支持）。
- 存在 CI（GitHub Actions：ci.yml + publish-nuget.yml，Ubuntu/macOS/Windows 矩阵）。**【确证】**
- 无 GitHub Releases、无 tag 记录检索到。**【确证】**

### 2.4 关联项目（同作者，供背景辨别）

| 项目 | 说明 | 与 Filament 的关系 |
|---|---|---|
| **radical-beard/evaluate**（+ NuGet `RadicalBeard.Evaluate` 0.12.0，2432 下载，**MIT 许可**，活跃至 2026-10-02） | *"A data-driven, moddable game framework built as an extension that runs **inside Godot**（net10.0）"*：.evt 脚本 = YAML frontmatter（能力签名）+ Lua body、逐脚本沙箱、热重载、场景/行为系统 | 同作者、同技术方向（沙箱 Lua + 数据驱动 + 热重载 + moddable）；**两者 README 均未明示互相引用**，精确关系**【未确认】**；时间线上 Filament（5-6 月）先出现、Evaluate（6 月-至今）持续演进 |
| radical-beard/luaml | Rust 项目（2026-04 创建，无描述、无许可） | 关系**【未确认】**，仅名字含 Lua |
| 其他 | campy-beta、breach、bestow-*、blue-prince-ssm、voxelerator 等约 20 个小仓库 | 多为游戏/工具试验，关系【未确认】 |

> ⚠️ 注意：**不要把 Filament 与 Evaluate 混为一谈**——Evaluate 是「Godot 内部扩展、net10.0、MIT」；Filament 是「engine-free 库、netstandard2.1、all-rights-reserved 占位」。（若未来需要 Godot 内一体化方案，Evaluate 反而更成熟。）

---

## 3. Unity / Godot 依赖与 .NET 8 可行性（对应委托 Q3）

### 3.1 依赖关系结论

- **Filament.Core 不依赖 Unity，也不依赖 Godot**。其唯一 PackageReference 是 `MoonSharp 2.0.0`（纯托管、无 native、无引擎运行时）。**【确证】**
- Godot 相关代码被严格隔离在 `Filament.Godot` 包（依赖 `GodotSharp 4.6.0`，TFM net8.0，注释 *"a net10 lib could not [be loaded by a real Godot game]"*）。**不装这个包就不会引入 Godot。****【确证】**
- `Filament.Config.csproj` 注释：*"Godot-free, net8-consumable. The TOML world-config model + diff; the Node-application/factory layer lives in Filament.Godot."* **【确证】**

### 3.2 纯 .NET 8 类库中能否使用 —— ✅ 能

证据链：
1. **TFM 层面**：Core/Abstractions/Config = `netstandard2.1` → 任何 .NET 8 项目可直接引用（含类库、控制台、服务端）。**【确证】**
2. **依赖层面**：MoonSharp 2.0.0 为纯托管程序集，跨平台（Windows/Linux/macOS/移动端）可运行，无原生 DLL 分发负担。**【高置信】**
3. **官方自证**：仓库自带 **`demo/`（控制台程序，非 Godot）**——*"A tiny non-Godot host that proves the whole loop: load a Lua behavior module, call into it with a [Scriptable] params record, and hot-reload edits live."*（demo/Program.cs 原文注释）。**【确证】**

### 3.3 若不用它，最接近的替代

- **直接使用 MoonSharp 2.0.0**（相同底座；许可为 BSD-3-Clause 风格，可自由集成）——自建一层薄封装即复刻 Filament.Core 的核心价值（源码总量很小：Core 目录 9 个文件约 20KB，见 §4.7）。**【高置信】**
- 或 **NLua / KeraLua**（原生 Lua、性能更高、MIT、活跃，但需分发 native 库）；**Lua-CSharp**（纯托管、MIT、活跃、主打高性能，2024 年起的新项目）。

---

## 4. 核心 API 形态（对应委托 Q4，均来自源码实测）

### 4.1 嵌入与执行（C# → Lua）

```csharp
// 1) 创建注册表（扫描目录下全部 *.lua），开启热重载
using var registry = new ScriptRegistry(luaRoot, debounceSeconds: 0.15);
registry.StatusChanged += s => Console.WriteLine($"[registry] {s.Kind}: {s.Path} {s.Message}");
Result<int, LuaError> init = registry.Initialize(liveReload: true);

// 2) 宿主循环（每帧/每 tick）驱动热重载（MoonSharp 只在宿主线程上被触碰）
registry.Pump(0.1);

// 3) 按逻辑路径取模块 → 类型化调用
if (registry.GetModule("policy").TryGet(out var module))
{
    var input = new PolicyInput(HpFraction: 0.4f, Distance: 3.2f);
    Result<string, LuaError> r = module.Call<string, PolicyInput>("describe", input);
    string text = r.Match(ok => ok, err => $"ERR {err}");
}
```

对应 Lua 侧（demo/lua/policy.lua 原文，注意该脚本运行于硬沙箱，无 `os`/`io`/`package`）：

```lua
local M = {}
function M.describe(p)                 -- 参数是单个 table，键为 snake_case
    local mood = "calm"
    if p.hp_fraction < 0.5 then mood = "frenzied" end
    return mood .. " @ " .. string.format("%.1f", p.distance) .. "m"
end
return M                                -- 每个脚本文件必须返回模块表
```

要点：**「一个 lua 文件 = 一个模块表（`return M`）」**；`Call<TRet,TParams>(method, args)` 把 `[Scriptable]` 参数记录编组为**单个 table 参数**，调用指定方法并按类型强转返回值。**【确证】**

### 4.2 向 Lua 暴露 C# 对象/函数（沙箱 verbs）

```csharp
// 宿主注册（进程级、幂等）；脚本侧得到 filament.<namespace>.<name>(...) 可调用
SandboxVerbs.Register("combat", "play_role", (ctx, args) =>
{
    var who = args.AsStringUsingMeta(ctx, 0);   // MoonSharp 回调签名
    /* 宿主实现真实逻辑 */
    return DynValue.NewBoolean(true);
});
```

- 注册的行为通过 `LuaSandbox.Create()` 安装到每个新 Script：全局表 `filament`（按命名空间分组）。**"This is the deliberate seam over the engine — host code registers verbs like spawn or play_role; scripts never see the raw engine API."（源码注释）****【确证】**
- 复杂数据结构通过 `[Scriptable]` record 双向编组：C# → Lua 变为 **snake_case 键的普通 table**（PascalCase `HpFraction` → `hp_fraction`）；可选字段用 `Option<T>`；列表编码为 **1-based Lua 数组**；source generator 自动生成转换器。**【确证】**

### 4.3 「动态 handler」：由 Lua 定义、被 C# 引擎调用

Filament 提供的机制（两种模式）：

1. **模块 + 方法名字符串 dispatch**（官方主推）：Lua 定义 `M.tick(state)`/`M.describe(p)` 等函数；C# 引擎在运行循环里 `module.Call<TRet,TParams>("tick", ctx)`。字符串调用在模块热重载后仍然有效（registry 在 gate 下替换内部 Script+table，"held references keep working"）。**【确证】**
2. **数据驱动绑定**：`LuaRef` 解析 `"lua:enemy/marionette"` 引用——TOML/配置声明哪个 Lua 模块支撑某实体行为，而非 C# 硬编码路径。**【确证】**

⚠️ **未发现**「把 Lua 函数包装为 C# `delegate`/回调对象」的成品 API。最接近的能力：`Call<TRet,...>` 的 `TRet` 可为 `DynValue`，即可以把 Lua 函数（function 类型）作为值取回并由 C# 侧持有、经 MoonSharp `Script.Call` 再次调用——**需要少量自研封装，Filament 未提供开箱即用的 delegate 桥**。**【高置信】**（对「触发器引擎原型」的设计有直接影响，请纳入评估。）

### 4.4 沙箱 / 安全限制

- `LuaSandbox.Create()` 使用 MoonSharp **`CoreModules.Preset_HardSandbox`**：**无 `os`、`io`、`package`、`debug`、`loadstring`**；仅 `string/math/table/bit32` 基础库。**【确证】**
- 额外收敛：`script.Options.DebugPrint` 至空操作；未注册任何 verb 时脚本甚至没有 `filament` 表。**【确证】**
- 社区脚本可访问的全部「引擎能力」＝宿主显式 `SandboxVerbs.Register(...)` 的白名单。这是「限制社区脚本可访问 API」的一等公民机制。**【确证】**
- 注：这是**合作式沙箱**（防误伤、限定 API 面），非强对抗式安全边界——MoonSharp 硬沙箱不承诺抵御恶意代码的所有攻击面（任何纯托管解释器均如此）。**【高置信】**

### 4.5 错误处理

- 全链路 **Result 风格，不抛异常**：`Result<TResult, LuaError>`；`LuaErrorKind` 语义化分类：`MethodMissing`（方法不存在）、`ParamMarshal`（参数编组失败）、`RuntimeError`（MoonSharp 运行时错误，含 DecoratedMessage 定位信息）、`ReturnCoercion`（返回值类型不匹配）、`RegistryNotReady`。**【确证】**
- 载入期：`LuaSandbox.LoadModule` 将解析/顶层运行时错误转为 `Err`（"never a throw"）。**【确证】**
- 热重载期：**parse/runtime 错误不打断运行**——保留上一个可用版本继续 live（last-good-state），并通过 `ScriptStatus`（`Failed`/`Reloaded`/`Removed` 等）上报。**【确证】**

### 4.6 热重载

- `ScriptRegistry` 内部用 `FileSystemWatcher`（含子目录）监听 `*.lua` 的 Changed/Created/Deleted/Renamed；事件在后台入队 + **防抖（默认 0.15s）**，实际切换 chunk 由宿主**在主线程调用 `Pump(delta)` 时**执行——「MoonSharp 只在宿主线程被触碰」，跨平台且可确定性测试。**【确证】**
- 单文件级重载：替换该模块内部 Script+table，外部持有的 `LuaModule` 引用继续有效。**【确证】**

### 4.7 代码量与可自研替代性

Filament.Core 全部源文件（GitHub contents 实测）：`LuaModule.cs`(4105B)、`ScriptRegistry.cs`(5652B)、`ScriptableMarshal.cs`(5472B)、`SandboxVerbs.cs`(1941B)、`LuaSandbox.cs`(1529B)、`ScriptStatus.cs`(604B)、`IScriptableConverter.cs`(622B)、`RoleClipMap.cs`(1169B)、`IsExternalInit.cs`(320B) + csproj。**整体是一层薄封装**——若因许可证/维护风险不能直接采纳，按 §4.1–4.6 的行为规格自研等价层的工程量可控。**【高置信】**

---

## 5. 典型集成实践与公开案例（对应委托 Q5）

### 5.1 官方示例

- **控制台 demo（非 Godot）**：`demo/Program.cs` + `demo/lua/policy.lua`——完整演示「加载模块→每 tick 调用→编辑文件热重载」。运行方式：`dotnet run --project demo/Filament.Demo.csproj -- --ticks 3`（不带 `--ticks` 则常驻观察热重载）。**【确证】**
- **Godot 集成（可选）**：`ScriptRegistryNode` 场景节点 + `res://lua` 目录；`registryNode.Module("enemy/marionette").TryGet(out var module)`。官方注明 Godot 集成「smoke coverage 只验证了包编译与值类型编组，缺少真实编辑器/运行时测试」。**【确证】**
- **Playtest MCP**：仓库含本地 MCP server（Python/uv），启动构建并通过浏览器问卷收集真人测试反馈——工程化程度有余，但与运行时脚本能力无关。**【确证】**

### 5.2 第三方/社区案例

- **未检索到任何公开第三方使用案例、教程、文章或视频**（中英文多轮检索：`"RadicalBeard.Filament"`、`radical-beard filament`、`Filament Lua .NET` 等均无有效命中）。**【高置信】**（注意：不排除英文长尾或未索引内容，但按「公开曝光≈0」评估是安全的。）
- 行业通用实践背景（非 Filament 特例）：Unity 系常见 XLua/tolua/UnLua + MoonSharp（纯净版用于 mod 场景）；非 Unity 的 .NET 场景中，用 MoonSharp 做 mod 脚本的公开讨论较多（CSDN/知乎均有「C# 集成 Lua」类文章），但**直接以「Filament」为对象的中文资料为 0**。**【高置信】**

---

## 6. 与主流替代方案对比（对应委托 Q6）

> 数据截至 2026-10-03，来源：NuGet API + GitHub API 实测（下载量为 NuGet totalDownloads 字段）。

| 方案 | 类型/依赖 | 许可证 | 最新版本（发布） | .NET 8 兼容 | 维护活跃度 | 要点 |
|---|---|---|---|---|---|---|
| **RadicalBeard.Filament.Core** | 纯托管封装（依赖 MoonSharp） | **⛔ all-rights-reserved 占位（未授权）** | 0.1.0（2026-06-02） | ✅ netstandard2.1 → net8 可引用 | ❄️ 低（4 个月未 push，0 star） | 沙箱 verbs + 热重载 + last-good + 编组一站式；但许可/成熟度风险最高 |
| **MoonSharp** | 纯托管 C# 解释器（零原生依赖） | BSD-3-Clause 风格（含组件混合；NOASSERTION 标记） | 2.0.0 NuGet（**2016-10-14**；GitHub 仓库 2026-08 仍有 push） | ✅（netstandard 系；被 Filament 以 netstandard2.1 消费验证） | 🔸 低-中（NuGet 停更 10 年，仓库零星维护） | Filament.Core 的底座；生态成熟（1620★），但版本老、性能一般；自带远程调试器（MoonSharp.Debugger 独立包） |
| **NLua** | 原生 Lua 绑定（KeraLua） | MIT | 1.7.9（2026-05-01） | ✅（需按平台分发 native 库） | 🟢 高（pushed 2026-09-13，2246★） | API 成熟、性能好；跨平台分发复杂度与 Apple 平台 JIT 限制需注意 |
| **KeraLua** | 原生 Lua 5.4 绑定（低层） | MIT | 1.4.10（2026-09-12） | ✅（同上，native） | 🟢 高（pushed 2026-09-12，376★） | NLua 的底座；适合需要直接控制 Lua C API 的场景 |
| **Lua-CSharp**（nuskey8） | 纯托管 C# 解释器（新） | MIT | 0.5.7（2025 内多次迭代；repo pushed 2026-09-26） | ✅（.NET + Unity） | 🟢 高（860★，45 open issues） | 主打高性能的现代替代；纯托管、无 native；API 与生态较新 |
| （背景）XLua / toLua / UnLua | Unity 专用 Lua 框架 | 各自开源许可 | — | 仅 Unity | 视项目 | 仅作 Unity 场景参照，与本项目「无 Unity 运行时」不符 |

**性能维度**：Filament 官方未发布任何基准；MoonSharp 作为纯托管解释器通常明显慢于原生 Lua（NLua/KeraLua），Lua-CSharp 声称高性能。若对脚本执行性能敏感，建议自行基准测试。**【未确认：Filament 性能数据】**

---

## 7. 名称歧义与同名物（对应委托 Q7）

「Filament」是一个高度多义的名字；「Filament.Core」这一**确切包名在 NuGet 上不存在**（检索 totalHits 无独立 `Filament.Core` 包）。经排查，与 C#/.NET 生态相关的主要同名物如下：

| 名称 | 领域 | 证据/结论 |
|---|---|---|
| **RadicalBeard.Filament.Core**（radical-beard/filament） | C#/.NET · Lua 脚本 · 游戏框架 | ✅ **本报告认定的目标对象**（描述逐字吻合：“Engine-free Filament Lua sandbox, hot reload, marshalling, and script dispatch.”） |
| Google Filament（google/filament） | C++ 3D 渲染引擎（Android/PC） | 同名不同物；.NET 侧只有社区绑定如 `chicken-with-lips/filament.net`、`Xamarin.Android.SceneForm.Filament`、`Com.Google.Android.Filament.MAUI` 等 NuGet 包——**均为渲染绑定，与 Lua 无关** |
| Laravel Filament（filamentphp.com） | PHP/Laravel 后台 UI 框架（v4.0，中文社区资料极多） | 同名不同物；完全无关 |
| Filament Games（filamentgames.com） | 教育游戏工作室（Unity） | 其 GitHub 有已归档的 `RuleScript`（Unity 规则脚本系统）——与「社区 DIY」字面相近但**非 Lua、非 .NET 8、已归档** |
| 其他 | 物理论文中的 filament、CSDN 噪音等 | 无关 |

**判断证据小结**：委托描述的「Filament（Filament.Core）· Lua 脚本执行 · 游戏逻辑 · 社区 DIY」四个要素，与 radical-beard/filament 的 README/CHANGELOG/NuGet 描述**逐项吻合且唯一**：
- “Lua owns fast-iteration behavior logic” ↔ Lua 脚本执行✅
- “sandbox verbs / hot reload / last-good fallback” ↔ 社区 DIY 友好✅
- NuGet 包层 `RadicalBeard.Filament.Abstractions/Core/Config/Godot` 且 README 明确写 “Use `RadicalBeard.Filament.Core` for engine-free Lua behavior scripting” ↔ 「Filament.Core」名称✅

> 可能性说明：若最初线索中的「Filament.Core」来自聊天/群组的口口相传，那基本就是本对象（唯一匹配且与「无 Unity/Godot 运行时」的描述严丝合缝——因为该库就是 engine-free 设计）。【高置信】

---

## 8. 对本项目场景（.NET 8 触发器引擎原型 / 社区 DIY）的可行性评估（分析，非委托问题，供决策参考）

**技术层面：可行，且机制对口。**
- ✅ 纯 .NET 8 类库引用无障碍（netstandard2.1 + MoonSharp 纯托管；官方 demo 即非 Godot 控制台）。
- ✅ 「社区 DIY 脚本」所需能力齐全：沙箱 + 动词白名单（限制 API 面）、热重载 + last-good（社区脚本迭代体验与容错）、Result/LuaError（不抛异常、状态流可观测）、`"lua:<module>"` 数据绑定。
- ⚠️ 「动态 handler = Lua 函数↔C# 委托」没有开箱 API（见 §4.3），若原型需要该形态需自行加一层（技术上可行：DynValue 持有 + MoonSharp Script.Call）。
- ⚠️ MoonSharp 为 Lua 5.2 语义、纯托管解释器（性能一般、2016 年后无新版）——触发器等高频调用场景建议先做性能预算测试。

**工程/合规层面：风险显著，建议谨慎。**
- ⛔ **许可证未授权（all-rights-reserved 占位）**：直接依赖其 NuGet 包/源码用于产品分发前，必须解决授权问题（联系作者或更换方案）。**这是硬性阻碍**。
- ❄️ 维护活跃度低（4 个月无提交、0 star、0.1.0、无第三方案例）——作为长期依赖的供应链风险高。
- 💡 **务实路径**：
  1. **参考其设计，自研薄封装**（推荐）：直接用 MoonSharp 2.0.0（BSD 类许可）按 §4 的行为规格实现等价层；Filament.Core 全套仅约 20KB 源码量级，自研成本低、无授权风险。
  2. 或等待/推动其许可证变更为开源后再评估直接采纳。
  3. 若追求高性能替代底座：评估 Lua-CSharp（MIT、纯托管、活跃）。
  4. 若最终宿主含 Godot 4.6：另可参考同作者的 Evaluate（MIT，但仅面向 Godot 场景）。

---

## 9. 关键来源列表

**一手来源（官方仓库 / 官方 API）：**
| # | 来源 | URL | 内容 |
|---|---|---|---|
| 1 | radical-beard/filament 仓库 | https://github.com/radical-beard/filament | 项目主页 |
| 2 | README.md（raw 实测） | https://raw.githubusercontent.com/radical-beard/filament/main/README.md | 定位、模块、NuGet 用法、要求 |
| 3 | Filament.Core.csproj | .../main/Filament.Core/Filament.Core.csproj | netstandard2.1、MoonSharp 2.0.0、注释原文 |
| 4 | Filament.Godot.csproj | .../main/Filament.Godot/Filament.Godot.csproj | net8.0、GodotSharp 4.6.0 |
| 5 | Filament.Abstractions / Filament.Config csproj | .../main/Filament.Abstractions/... 、.../main/Filament.Config/... | netstandard2.1、Tomlyn 0.19.0、"Godot-free, net8-consumable" |
| 6 | docs/scripting.md | .../main/docs/scripting.md | 脚本模型、沙箱、编组 |
| 7 | docs/package-usage.md | .../main/docs/package-usage.md | 包用法、[Scriptable] 示例 |
| 8 | docs/godot.md | .../main/docs/godot.md | Godot 适配、smoke 覆盖说明 |
| 9 | docs/readiness.md | .../main/docs/readiness.md | 许可占位说明、跨平台 CI |
| 10 | CHANGELOG.md / LICENSE.md / Directory.Build.props | 仓库根 | 0.1.0 功能清单 / 许可占位 / 包元数据 |
| 11 | 源码：LuaModule.cs / ScriptRegistry.cs / LuaSandbox.cs / SandboxVerbs.cs / LuaRef.cs / demo/Program.cs / demo/lua/policy.lua | 仓库内 | API 行为细节（本报告 §4 全部结论） |
| 12 | GitHub API：repos/radical-beard/filament、/releases、trees、contents | api.github.com | 时间线、0 star、无 Releases、文件树 |
| 13 | NuGet API：azuresearch q=radicalbeard、registration radicalbeard.filament.core | api.nuget.org | 包 ID/版本/发布日期/依赖/下载量 |
| 14 | radical-beard/evaluate（仓库 + NuGet RadicalBeard.Evaluate） | https://github.com/radical-beard/evaluate | 关联项目（MIT、Godot、0.12.0、活跃） |
| 15 | MoonSharp：moonsharp-devs/moonsharp + NuGet 2.0.0（published 2016-10-14）+ LICENSE | github.com/moonsharp-devs/moonsharp | 底座库状态与许可 |
| 16 | NLua 1.7.9（2026-05-01） / KeraLua 1.4.10（2026-09-12） / Lua-CSharp 0.5.7 | NuGet API + GitHub API | 对比数据 |
| 17 | filamentphp.com / Laravel Filament（中文站 laravel-filament.cn） | 百度检索 | 同名物排查 |
| 18 | Filament Games GitHub（RuleScript 归档仓库） | github.com/FilamentGames | 同名物排查 |
| 19 | Google Filament 生态（filament.net、Xamarin/MAUI 绑定包） | GitHub/NuGet 检索 | 同名物排查 |

**已落盘的过程产物**（供复查）：`outputs/web-research/` 下 30+ 个文件：GitHub/NuGet API 原始 JSON、README/docs 正文、核心源码副本、对比库元数据等（文件名前缀 `filament-*` / `radicalbeard-*` / `moonsharp-*` / `nlua-*` / `keralua-*` / `lua-csharp-*` / `evaluate-*` / `repo-*`）。

---

## 10. 未确认项与不确定性说明

1. **Filament 与 Evaluate 的精确关系**：同作者、同方向，但 README 无互相引用；「继任/并行实验」均为推测（【未确认】）。不影响「Filament.Core = radical-beard/filament」的认定。
2. **MoonSharp 许可细节**：LICENSE 为 BSD-3-Clause 风格文本（含 KopiLua/VS Code debugger 等组件的混合声明，标注 NOASSERTION）；用于合规流程前建议法务复核原文（https://github.com/moonsharp-devs/moonsharp/blob/master/LICENSE）。
3. **性能数据**：Filament 与 MoonSharp 均无官方基准；报告未做实测。
4. **第三方案例**：「未发现」≠「不存在」，但结合 0 star/0 fork/无文章，结论按「实际无公开案例」采用。
5. **NuGet 下载量数字**：为 2026-10-03 快照，会随时间变化。
