# 需求 Grill 计划 — 社区 DIY（Filament.Core + Lua）

> 需求（2026-10-03 用户提出）：集成 Filament.Core，支持"动态 handler + Lua 脚本"，实现**面向社区 DIY** 的能力。
> 当前：信息收集完成（联网研究）；需求 grill 第 1 轮。

## 研究结论摘要（2026-10-03，详见 研究-Filament.Core.md）

- **对象确证**：radical-beard/filament（NuGet：**RadicalBeard.Filament.Core v0.1.0**）——基于 MoonSharp 2.0.0 加固封装的"引擎无关"Lua 行为脚本框架：硬沙箱（Preset_HardSandbox）+ `filament.<ns>.<verb>` 动词白名单 + 热重载（last-good 回退）+ [Scriptable] 源生成编组 + Result/LuaError 无异常模型。
- **可行性**：✅ netstandard2.1，可在**纯 .NET 8 类库**使用（Godot 依赖隔离于独立包 Filament.Godot；官方有非 Godot 控制台 demo）。
- **风险**：⚠ **许可证为 all-rights-reserved 占位（尚未授权）**；项目极早期（0.1.0 单版本、0 star、无公开案例、2026-06-02 后无提交）。
- **缺口**：无"Lua 函数→C# 委托"开箱 API（官方为「模块+方法名」字符串 dispatch；委托形态需少量自研）。
- 研究过程产物：`outputs/web-research/`。

## Unresolved Questions

### Q1: 集成路径与许可 {In Progress}
- 选项：a 直接集成（若用户即作者/有直接授权）；b 参考其行为规格自研（MoonSharp 薄层，无授权风险）；c 等许可变更后再评估
- 第 1 轮已问

### Q2: 时机与范围 {Pending}
- 该能力属原型之后的演进，还是影响现计划（S2~S5 队列）

### Q3: 与"csx/gds 脚本接口 + 继承模式"的关系 {Pending}
- 取代 / 并列 / 分层（开发者脚本 vs 社区脚本）

### Q4: 动态 handler 边界与安全 {Pending}
- Lua 可定义到哪一层（事件 handler / 触发器 / 效果）；社区脚本安全边界；热重载语义（是否需要）
