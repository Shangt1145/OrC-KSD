namespace Orc.Lua;

/// <summary>
/// 白名单域注册树节点（组件实例级；多实例之间互相独立、无全局单例）。
/// 节点为"动词叶子"（<see cref="Callback"/> 非空）或"子层级"（<see cref="Children"/> 非空）之一，两者互斥。
/// </summary>
internal sealed class VerbNode
{
    /// <summary>子层级节点（名字 → 节点）。</summary>
    public Dictionary<string, VerbNode> Children { get; } = new(StringComparer.Ordinal);

    /// <summary>子节点注册顺序（供 <c>__pairs</c> 枚举注册树使用）。</summary>
    public List<string> ChildOrder { get; } = new();

    /// <summary>叶子动词的宿主回调；子层级节点为 <see langword="null"/>。</summary>
    public Func<object?[], object?>? Callback { get; set; }

    /// <summary>是否为动词叶子。</summary>
    public bool IsLeaf => Callback is not null;

    /// <summary>获取或创建子节点。</summary>
    public VerbNode GetOrAddChild(string name)
    {
        if (!Children.TryGetValue(name, out var child))
        {
            child = new VerbNode();
            Children.Add(name, child);
            ChildOrder.Add(name);
        }

        return child;
    }
}
