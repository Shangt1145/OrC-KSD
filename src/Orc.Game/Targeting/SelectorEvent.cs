using Orc.Core;

namespace Orc.Game.Targeting;

/// <summary>
/// 语义事件（前端把原始手势<b>归一</b>后提交给后端选择器实例；前端不含判定逻辑）。
/// 判定与校验归后端实例：点选空提交＝<see cref="SelectorFailureReason.InvalidSelection"/>；
/// 拖拽落在空处＝取消；显式取消＝取消。
/// </summary>
public abstract class SelectorEvent
{
}

/// <summary>点选提交（引用元素与/或标识元素；空提交＝未选任何）。</summary>
public sealed class PickEvent : SelectorEvent
{
    /// <summary>创建点选事件。</summary>
    /// <param name="references">选中的引用（可为 null＝空）。</param>
    /// <param name="identifiers">选中的标识（可为 null＝空）。</param>
    public PickEvent(IReadOnlyList<Ref<Entity>>? references = null, IReadOnlyList<string>? identifiers = null)
    {
        References = references ?? Array.Empty<Ref<Entity>>();
        Identifiers = identifiers ?? Array.Empty<string>();
    }

    /// <summary>选中的引用（只读快照）。</summary>
    public IReadOnlyList<Ref<Entity>> References { get; }

    /// <summary>选中的标识（只读快照）。</summary>
    public IReadOnlyList<string> Identifiers { get; }

    /// <summary>是否空提交（未选任何）。</summary>
    public bool IsEmpty => References.Count == 0 && Identifiers.Count == 0;
}

/// <summary>拖拽落点事件（<see cref="Target"/> 为 null＝落在空处）。</summary>
public sealed class DropEvent : SelectorEvent
{
    /// <summary>创建拖拽落点事件。</summary>
    /// <param name="target">落点引用（null＝落在空处）。</param>
    public DropEvent(Ref<Entity>? target)
    {
        Target = target;
    }

    /// <summary>落点引用（null＝落在空处）。</summary>
    public Ref<Entity>? Target { get; }
}

/// <summary>显式取消事件（点取消按钮 / 拖拽取消）。</summary>
public sealed class CancelEvent : SelectorEvent
{
}
