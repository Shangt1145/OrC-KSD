using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 提交元素（统一提交面的类别化载荷；与槽位类别对应）：
/// 引用类槽位＝引用元素（<see cref="FromReference"/>）；非引用类槽位＝标识元素（<see cref="FromIdentifier"/>）。
/// 校验归属：元素合法性（null / 空标识 / 类别与槽位不匹配）由提交校验裁决——违规＝拒绝（同"内容不合规"路径：
/// 不构成终局、请求继续等待、留痕）；本类型为纯载荷容器，不预校验（提交面为数据通道、"真实校验、不信任前端"）。
/// </summary>
public sealed class TargetSelection
{
    private TargetSelection(Ref<Entity>? reference, string? identifier)
    {
        Reference = reference;
        Identifier = identifier;
    }

    /// <summary>引用元素（引用类槽位提交用；null＝本元素非引用承载）。</summary>
    public Ref<Entity>? Reference { get; }

    /// <summary>标识元素（非引用类槽位提交用；null＝本元素非标识承载）。</summary>
    public string? Identifier { get; }

    /// <summary>是否引用承载（便捷判定；内容合法性仍由提交校验裁决）。</summary>
    public bool IsReference => Reference is not null;

    /// <summary>创建引用元素（引用类槽位提交用）。null 引用由提交校验裁决为拒绝（同既有"含 null 引用"路径）。</summary>
    /// <param name="reference">引用（可为 null——提交校验拒绝）。</param>
    public static TargetSelection FromReference(Ref<Entity>? reference)
        => new(reference, identifier: null);

    /// <summary>创建标识元素（非引用类槽位提交用）。null/空白标识由提交校验裁决为拒绝（同"内容不合规"路径）。</summary>
    /// <param name="identifier">标识（可为 null/空白——提交校验拒绝）。</param>
    public static TargetSelection FromIdentifier(string? identifier)
        => new(reference: null, identifier);
}
