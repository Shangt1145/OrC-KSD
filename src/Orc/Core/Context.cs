namespace Orc.Core;

/// <summary>
/// 执行上下文（S1 精简版）：一次结算的数据载体。
/// 仅含数据（Data）与最小读取 API；停止 / 中断 / 链标识等语义待 S2 对齐后再定。
/// </summary>
public sealed class Context
{
    /// <summary>数据载体（库内使用：视图绑定与代理直连此载体，不做拷贝）。</summary>
    internal Dictionary<string, object?> Data { get; }

    /// <summary>
    /// 创建一个上下文。传入的非 null 字典会被拷贝一次，此后由本上下文独占该载体、不再拷贝；传 null 视为空载体。
    /// </summary>
    public Context(IDictionary<string, object?>? data = null)
    {
        Data = data is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(data);
    }

    /// <summary>尝试读取数据载体中的值（供外部观察最终数据）。</summary>
    public bool TryGet(string key, out object? value) => Data.TryGetValue(key, out value);

    /// <summary>读取数据载体中的值；键不存在时抛出 <see cref="KeyNotFoundException"/>。</summary>
    public object? Get(string key) => Data.TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException($"上下文数据中不存在键 '{key}'。");
}
