# Orc.Mcp.Stdio —— OrC 引擎的 MCP stdio 适配示例

把 `src/Orc.Mcp`（协议无关服务层）接到 MCP 的 stdio 传输上。**零第三方依赖**（手写最小 JSON-RPC 分发）。

## 启动（一条命令）

```bash
dotnet run --project samples/Orc.Mcp.Stdio -- --mode authoring
```

参数：

| 参数 | 说明 | 默认 |
|---|---|---|
| `--mode authoring\|player` | 服务层运行模式。`authoring`＝卡牌编辑者（沙盒，含调试工具）；`player`＝agent 玩家（不含调试工具，本轮未接入对局注入） | `authoring` |
| `--sandbox <dir>` | 沙盒根（**唯一可写根**） | `<当前工作目录>/.orc-sandbox` |
| `--packs <dir>` | 正式资产根（只读；`asset.promote` 的允许写入范围） | 无 |
| `--promote` | 启用产物提升（沙盒 → 正式卡包） | 关闭 |
| `--csx` | 启用 csx 编辑能力（弱沙箱，非安全边界） | 关闭 |
| `--seed <N>` | 沙盒对局随机种子（复现用） | 引擎自动生成 |

日志走 **stderr**；stdout 只输出协议帧。

## MCP 客户端配置（可直接复制）

把下面的片段加入你的 MCP 客户端配置（路径按需改成绝对路径）：

```json
{
  "mcpServers": {
    "orc-authoring": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<仓库绝对路径>/samples/Orc.Mcp.Stdio",
        "--no-build",
        "--",
        "--mode",
        "authoring",
        "--sandbox",
        "<仓库绝对路径>/.orc-sandbox"
      ]
    }
  }
}
```

> 若宿主不支持 `dotnet run`，可先 `dotnet build`，再把 `command` 指向
> `samples/Orc.Mcp.Stdio/bin/Debug/net8.0/Orc.Mcp.Stdio.exe`（Windows）或对应可执行文件。

## 支持的协议方法

`initialize` / `notifications/initialized` / `tools/list` / `tools/call` / `ping`。

本示例刻意**不实现** `resources/*`（文档读取已由 `docs.list`/`docs.read` 工具提供）与 prompts/logging 等能力，以保持"薄适配"。

## 结果约定

- 业务失败（如参数错误、校验失败、对局未就绪）返回 `content` 内嵌 `{ ok:false, code, message }`，**`isError` 保持 false**（便于 agent 区分"可重试的业务失败"与"工具崩了"）。
- 仅工具自身故障（`code = "internal"`）置 `isError: true`。
- 目标选择是**两次调用**：动作工具返回 `code = "targeting"` + `details.pending`，再用 `target.submit` / `target.cancel` 应答。

## 首次使用建议流程

1. `service.info` —— 读模式、门控与限制清单。
2. `docs.list` / `docs.read` —— 读创作文档（卡牌数据体、效果、词条、卡包目录、工作流）。
3. `sandbox.create` —— 建沙盒局（生成脚手架）。
4. `card.write` → `asset.validate` → `card.load` —— 写卡、校验、逐卡注册。
5. `sandbox.spawn` / `sandbox.emit` / `sandbox.damage` —— 构造验证场景。
6. `observe.snapshot` / `observe.segments` —— 观察数值与因果。
7. `sandbox.dispose` —— 收尾（如需交付，`--promote` 后用 `asset.promote`）。
