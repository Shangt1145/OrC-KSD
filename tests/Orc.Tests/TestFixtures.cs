#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Core;

namespace Orc.Tests;

/// <summary>贴设计文档形态的样例视图（S1 作者契约偏离：不声明 IContextView；属性为 virtual 供框架产物覆写）。</summary>
[ContextView]
public class ShieldView
{
    [Read]
    public virtual Ref<Entity> Target { get; set; }

    [Read]
    public virtual Ref<Entity> Source { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }
}

/// <summary>覆盖权限矩阵全部格子的样例视图。</summary>
[ContextView]
public class MatrixView
{
    [Read]
    public virtual Ref<Entity> Target { get; set; }

    [Read]
    public virtual int ReadOnlyAmount { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }

    [Mutate]
    public virtual string Label { get; set; }

    [Optional]
    [Read]
    public virtual string? Note { get; set; }

    [Optional]
    [Mutate]
    public virtual int Bonus { get; set; }

    [Optional]
    [Read]
    public virtual Ref<Entity>? Extra { get; set; }

    /// <summary>仅 [Optional] 而缺主特性——归入「无有效访问特性」：读写均拒绝。</summary>
    [Optional]
    public virtual int Draft { get; set; }

    /// <summary>无任何访问特性——访问期读写均拒绝。</summary>
    public virtual int Undeclared { get; set; }
}

/// <summary>共享性验收用样例视图：全可选，可从空载体起步（含引用类型属性用于同一性断言）。</summary>
[ContextView]
public class CounterView
{
    [Optional]
    [Mutate]
    public virtual int Count { get; set; }

    [Optional]
    [Mutate]
    public virtual object? Payload { get; set; }
}

/// <summary>负面样例：未标 [ContextView]。</summary>
public class UntaggedView
{
    [Mutate]
    public virtual int Amount { get; set; }
}

/// <summary>负面样例：[Read] 与 [Mutate] 同标（非法声明）。</summary>
[ContextView]
public class ConflictView
{
    [Read]
    [Mutate]
    public virtual int Both { get; set; }
}

/// <summary>负面样例：访问器非 virtual（无法代理，绑定期拒绝）。</summary>
[ContextView]
public class NonVirtualView
{
    [Mutate]
    public int Amount { get; set; }
}
