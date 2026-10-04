#pragma warning disable CS8618 // 视图属性值由框架在绑定时提供；声明期不做初始化（视图类不应写属性初始化器）。

using Orc.Core;

namespace Orc.Tests;

/// <summary>贴设计文档形态的样例视图（S1 作者契约偏离：不声明 IContextView；属性为 virtual 供框架产物覆写）。</summary>
[ContextView]
public class SampleView
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

/// <summary>S2 专门 band 方案示例（作者声明；成员可显式指定值）。
/// 别名说明：同值不同名视作同一区段（排序键相同）。</summary>
public enum DamageBands
{
    Prevent = 100,
    AliasPrevent = 100, // 别名示例：与 Prevent 同值
    Reduce = 300,
    Finalize = 500,
}

/// <summary>S2 band 边界用例方案（负值 / 超限 / 上限）。</summary>
public enum BoundaryBands
{
    Normal = 0,
    Negative = -1,
    TooLarge = 1000001,
    Max = 1000000,
}

/// <summary>S2 伤害示例视图：含手写静态 Translate（转接书写约定）。</summary>
[ContextView]
public class DamageView
{
    [Read]
    public virtual Ref<Entity> Source { get; set; }

    [Read]
    public virtual Ref<Entity> Target { get; set; }

    [Mutate]
    public virtual int Amount { get; set; }

    [Optional]
    [Read]
    public virtual int Modifier { get; set; }

    /// <summary>
    /// 手写转接（书写约定：作者视图类上的静态方法；框架不建立强制契约、不隐式调用；转接不依赖视图实例状态）。
    /// 引用类参数以同一 Ref 实例入（不拷贝包装、不拷贝目标对象）；值类参数以快照入（直接透传赋值，无包装 API）。
    /// 引擎引用可用于转接期访问引擎域内容（本示例不向数据注入额外项）。
    /// </summary>
    public static void Translate(
        LogicEngine engine,
        Ref<Entity> source,
        int amount,
        Ref<Entity> target,
        int modifier,
        Dictionary<string, object?> data)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);

        data["Source"] = source;
        data["Amount"] = amount;
        data["Target"] = target;
        data["Modifier"] = modifier;
    }
}

/// <summary>S2 引用键改写用例视图：Ref 键可被 [Mutate] 属性改写。</summary>
[ContextView]
public class LinkView
{
    [Mutate]
    public virtual Ref<Entity> Link { get; set; }
}
