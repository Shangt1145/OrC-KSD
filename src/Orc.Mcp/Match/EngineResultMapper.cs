using System.Text.Json.Nodes;
using Orc.Game;
using Orc.Game.Cards;
using Orc.Game.Commanding;
using Orc.Game.Managers;
using Orc.Game.Targeting;
using Orc.Mcp.Contracts;

namespace Orc.Mcp.Match;

/// <summary>
/// 引擎三态结果 → <see cref="ActionOutcome"/> 的统一映射。
/// <para>
/// 规则（R5）：**成功**＝completed；**取消**＝cancelled（合法结局，按成功处理）；**失败**＝failed +
/// <see cref="McpCodes.EngineRejected"/>。引擎原生字段（status / failureReason / targeting 的 status 与 failureReason）
/// 一律保留在 <see cref="ActionOutcome.Payload"/>，不丢信息。
/// </para>
/// </summary>
internal static class EngineResultMapper
{
    public static ActionOutcome FromPlay(PlayResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Map(
            "play",
            result.Status.ToString(),
            result.Status == PlayResultStatus.Success,
            result.Status == PlayResultStatus.Cancelled,
            result.FailureReason?.ToString(),
            result.Targeting);
    }

    public static ActionOutcome FromCommand(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Map(
            "command",
            result.Status.ToString(),
            result.Status == CommandResultStatus.Success,
            result.Status == CommandResultStatus.Cancelled,
            result.FailureReason?.ToString(),
            result.Targeting);
    }

    public static ActionOutcome FromMulligan(MulliganResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Map(
            "mulligan",
            result.Status.ToString(),
            result.Status == MulliganResultStatus.Success,
            result.Status == MulliganResultStatus.Cancelled,
            result.FailureReason?.ToString(),
            result.Targeting);
    }

    public static ActionOutcome FromPlace(CardPlaceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var payload = new JsonObject
        {
            ["kind"] = "place",
            ["status"] = result.Status.ToString(),
        };

        return ActionOutcome.Completed(payload);
    }

    /// <summary>查询类结果的统一包装（无三态：纯读取）。</summary>
    public static ActionOutcome Query(string kind, JsonNode? payload) => ActionOutcome.Completed(Wrap(kind, payload));

    private static ActionOutcome Map(
        string kind,
        string statusText,
        bool isOk,
        bool isCancelled,
        string? failureReason,
        TargeterResult? targeting)
    {
        var payload = new JsonObject
        {
            ["kind"] = kind,
            ["status"] = statusText,
            ["failureReason"] = failureReason,
            ["targetingStatus"] = targeting?.Status.ToString(),
        };

        if (isCancelled)
        {
            return ActionOutcome.Cancelled(payload);
        }

        if (isOk)
        {
            return ActionOutcome.Completed(payload);
        }

        return ActionOutcome.Failed(
            McpCodes.EngineRejected,
            failureReason is null ? "引擎拒绝（详见 engineResult）。" : $"引擎拒绝：{failureReason}",
            payload);
    }

    private static JsonNode Wrap(string kind, JsonNode? payload)
    {
        var node = payload as JsonObject ?? new JsonObject();
        node["kind"] = kind;
        return node;
    }
}
