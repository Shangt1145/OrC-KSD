namespace Orc.Output;

/// <summary>
/// 更新回调（S5 骨架）：<see cref="Orc.Core.LogicEngine.Subscribe"/> 的回调形态；订阅单位＝更新（Emit 时回调）。
/// 回调异常被隔离（记录、不破坏更新广播主流程）；回调返回的 Task 会被等待（引擎不保证回调并发/顺序之外的语义）。
/// </summary>
public delegate Task EngineUpdateCallback(
    string updateType, IReadOnlyDictionary<string, object?>? payload, CancellationToken ct);
