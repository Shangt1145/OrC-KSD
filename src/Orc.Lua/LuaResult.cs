namespace Orc.Lua;

/// <summary>
/// 统一结果对象（第一版公共契约）：成败判别 + 载荷。
/// 成功时载荷为编组后的结果值；失败时载荷为结构化错误信息（<see cref="Error"/>）。
/// </summary>
/// <typeparam name="T">成功载荷的类型。</typeparam>
public sealed class LuaResult<T>
{
    private LuaResult(bool isSuccess, T? value, LuaError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>是否成功。</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 载荷：成功时为编组结果；失败时为 <see langword="null"/>。
    /// 例外：<see cref="LuaHandlerEngine.InitializeSource"/> 的失败结果会携带一个不可用单元句柄，
    /// 供宿主检查"不可用单元"语义（宿主应丢弃该单元）。
    /// </summary>
    public T? Value { get; }

    /// <summary>失败时的结构化错误信息；成功时为 <see langword="null"/>。</summary>
    public LuaError? Error { get; }

    /// <summary>构造成功结果。</summary>
    public static LuaResult<T> Success(T? value) => new(true, value, null);

    /// <summary>构造失败结果。</summary>
    public static LuaResult<T> Failure(LuaError error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));

    /// <summary>构造失败结果（失败时仍携带载荷——用于初始化失败单元句柄）。</summary>
    internal static LuaResult<T> FailureWithPayload(LuaError error, T? payload) =>
        new(false, payload, error ?? throw new ArgumentNullException(nameof(error)));
}
