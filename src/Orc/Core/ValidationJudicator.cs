namespace Orc.Core;

/// <summary>
/// 验证判定器逻辑（强类型面；J2）：输入＝refs（触发数据第一层引用收集，维持现状收集面）＋
/// subject（被判定对象引用——显式、Ref 形态；可为 null＝无被判定对象）；
/// 输出＝<see cref="ValidationVerdict"/>（合法性＋拒绝类别）。
/// </summary>
/// <param name="refs">引用收集面（触发数据第一层引用类值；按引用相等去重、保持插入序；无引用＝空列表）。</param>
/// <param name="subject">被判定对象引用（如费用/反制＝被判定卡牌；复验＝操作单位；可为 null）。</param>
/// <returns>验证判定结果。</returns>
public delegate ValidationVerdict ValidationJudicatorLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject);

/// <summary>
/// 验证判定器（J2；判定器子类形态之一）：承载「合法性验证」的独立无状态服务——
/// 统一强类型契约（refs＋subject → 验证判定结果）；子类实现 <see cref="ValidateLogic"/>（默认逻辑）；
/// 「拆包→强类型调用→组包」适配与统一主方法在基类封闭（验证判定器的输入/输出契约固定：
/// 统一面载荷＝<c>[refs, subject]</c>、输出＝<c>[ValidationVerdict]</c>——moding 改写经同一契约承载）。
/// 无状态：不持有跨调用可变状态；允许装配期注入对局级只读设施引用（仅只读使用）。
/// 改写：经注册表 moding 面（双形态：统一签名底层＋强类型封装便捷面）——改写＝该验证逻辑替换、其全部引用点生效。
/// </summary>
public abstract class ValidationJudicator : Judicator<ValidationJudicatorLogic>
{
    /// <summary>
    /// 验证逻辑（子类实现；默认逻辑——moding 生效时被栈顶替换逻辑取代）。
    /// 契约：只读、无副作用；载荷校验失败（如被判定对象载体不符）＝明确异常（fail-fast 族——由触发路径契约兜底承接）。
    /// </summary>
    /// <param name="refs">引用收集面。</param>
    /// <param name="subject">被判定对象引用（可为 null）。</param>
    /// <returns>验证判定结果。</returns>
    protected abstract ValidationVerdict ValidateLogic(IReadOnlyList<Ref<Entity>> refs, Ref<Entity>? subject);

    /// <inheritdoc />
    protected internal sealed override Func<object[]?, object[]?> Adapt(ValidationJudicatorLogic handler)
        => args =>
        {
            var refs = Unpack<IReadOnlyList<Ref<Entity>>>(args, 0);
            var subject = Unpack<Ref<Entity>>(args, 1);
            return new object[] { handler(refs, subject) };
        };

    /// <inheritdoc />
    public sealed override object[]? Invoke(object[]? args) => Adapt(ValidateLogic)(args);
}
