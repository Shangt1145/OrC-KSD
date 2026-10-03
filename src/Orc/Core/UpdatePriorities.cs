namespace Orc.Core;

/// <summary>
/// 挂载优先级参考常量集（S3；固定类名/成员名）。提供给被动触发器构造期声明使用。
/// 纯参考——不作为取值校验依据（任意 int 含负值均合法、不设界）；属"可修订的常量"（数值可随版本调整），非运行期可配置。
/// 同一更新下排序：挂载优先级降序（数值大者先）→ 注册序升序兜底。
/// （注意：与触发器内部事件的 band 排序方向相反——band 是"阶段序号"、挂载优先级是"介入优先级"，语义不同。）
/// </summary>
public static class UpdatePriorities
{
    /// <summary>高优先级（先执行；100）。</summary>
    public const int High = 100;

    /// <summary>常规优先级（默认；0）。</summary>
    public const int Normal = 0;

    /// <summary>低优先级（后执行；-100）。</summary>
    public const int Low = -100;
}
