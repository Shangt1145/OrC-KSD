namespace Orc.Output;

/// <summary>
/// 即时更新回调（UI 消费桥接；S8）：<see cref="Orc.Core.LogicEngine.OnImmediateUpdate"/> 的 handler 形态。
/// 非阻塞：引擎在更新广播时点调用，但**不等待**其完成、**不承载**除更新类型与载荷之外的状态；
/// 契约要求 handler 仅做瞬时操作（置标志 / 入队），不得阻塞。异常被隔离（记录、不破坏更新广播主流程）。
/// </summary>
public delegate void ImmediateUpdateCallback(
    string updateType, IReadOnlyDictionary<string, object?>? payload);
