namespace Orc.Core;

/// <summary>
/// 验证判定结果（J2；验证判定器的输出契约载体）：承载「合法性＋（不合法时）拒绝类别」——
/// 取数形态；合法＝无拒绝类别（<see cref="RejectionReason"/>＝null）；不合法时拒绝类别可为 null（未提供/不可辨识）。
/// 消费语义分层：触发内验证与公开预检按布尔语义消费（<see cref="IsValid"/>）；
/// 入口映射按取数语义消费（<see cref="RejectionReason"/>——类别缺失或不可辨识时由消费侧降级为一般性失败原因）。
/// 由 <see cref="ValidationJudicator"/> 产出；经判定器双形态承载（统一 object[] 面＋强类型面）。
/// 不可变；不含对局状态（契约：只读、无副作用）。
/// </summary>
public sealed class ValidationVerdict
{
    private ValidationVerdict(bool isValid, object? rejectionReason)
    {
        IsValid = isValid;
        RejectionReason = rejectionReason;
    }

    /// <summary>合法（无拒绝类别；null 形态）。</summary>
    public static ValidationVerdict Valid { get; } = new(true, null);

    /// <summary>
    /// 不合法（拒绝类别可选）：<paramref name="rejectionReason"/>＝拒绝类别（判定器自定义载体——如游戏层拒绝类别枚举）；
    /// 缺省＝未提供类别（消费侧降级为一般性失败原因——不伪造具体类别）。
    /// </summary>
    public static ValidationVerdict Invalid(object? rejectionReason = null) => new(false, rejectionReason);

    /// <summary>本次是否合法（触发内验证与公开预检的布尔消费面）。</summary>
    public bool IsValid { get; }

    /// <summary>拒绝类别（合法＝null；不合法时可为 null＝未提供/不可辨识——入口映射按取数消费）。</summary>
    public object? RejectionReason { get; }
}
