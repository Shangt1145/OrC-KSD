namespace Orc.Lua;

/// <summary>
/// 结构化错误信息：分类 + 人类可读消息。
/// 来源/位置等附加信息（行号、堆栈、阶段）属"应尽力提供"项，由消息文本承载。
/// </summary>
public sealed class LuaError
{
    /// <summary>创建结构化错误。</summary>
    /// <param name="kind">错误分类。</param>
    /// <param name="message">人类可读消息（始终非空）。</param>
    public LuaError(LuaErrorKind kind, string message)
    {
        Kind = kind;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>错误分类（可按枚举断言）。</summary>
    public LuaErrorKind Kind { get; }

    /// <summary>人类可读消息（始终非空）。</summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Message}";
}
