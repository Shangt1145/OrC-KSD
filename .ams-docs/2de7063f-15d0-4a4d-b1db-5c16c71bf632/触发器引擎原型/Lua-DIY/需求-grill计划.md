# 需求 Grill 计划 — 社区 DIY（Lua 动态 handler）

> 需求（2026-10-03 用户提出）：集成 Lua 脚本能力，支持"动态 handler"，实现面向社区 DIY 的能力。
> 状态：**需求 grill 完成**；实现委托已发（e8802a93）。

## 研究结论摘要（2026-10-03，详见 研究-Filament.Core.md）

- 对象确证：radical-beard/filament（RadicalBeard.Filament.Core v0.1.0）——基于 MoonSharp 2.0.0 的引擎无关 Lua 行为脚本框架。
- 可行性：✅ 纯 .NET 8 可用；风险：⚠ 许可证未授权占位 + 项目极早期 → **不直接集成**。

## 已定决策（全部）

- **Q1 集成路径**：MoonSharp 直接依赖 + 仅参考 Filament 设计自建框架（不集成 Filament.Core）。
- **范围**：当前只独立实现和测试**动态 handler 本体**；**加载逻辑后置**（文件/目录/热重载不做）。
- **Q2 队列时机 = a**：立即启动（独立任务，与 S3~S5 写入区域无交集）。
- **Q3 API 形态 = a**：通用动态形态——Lua 函数接收"参数表"、返回结果值/表；C# 侧 `Invoke(参数包) → Result<结果>`。
- **Q4 模块形态**：`src/Orc.Lua/`（独立程序集，不引用 Orc 核心）+ `tests/Orc.Lua.Tests/`。
- **Q5 参考落地项**：硬沙箱 + 动词白名单（如 `orc.*`）+ 结构化错误模型。

## 后置项（记录，后续演进）

- 脚本加载逻辑与热重载（文件/目录监控、last-good 回退）；
- 引擎适配器（Lua handler ↔ Orc 事件签名 `(view, ctx, ct)` 的桥接）；
- 强类型编组协议；[Scriptable] 式编组工具化；
- 社区分发/安全策略（签名、权限分级等，若需要）。
