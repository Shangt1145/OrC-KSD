namespace Orc.Core;

/// <summary>
/// 框架侧视图契约：由绑定器创建的框架产物（派生自作者视图类）在运行时实现。
/// 视图作者类不声明 / 不实现本接口——作者只写「类 + [ContextView] + 特性属性」，声明即工作。
/// </summary>
public interface IContextView
{
    /// <summary>将数据载体与上下文绑定到视图（框架内部流程调用；数据唯一来源为 Context.Data 载体）。</summary>
    void Bind(Dictionary<string, object?> data, Context ctx);

    /// <summary>封印视图会话（幂等；封印不导致授权——权限矩阵照常生效，封印亦不冻结数据）。</summary>
    void Seal();
}

/// <summary>标记类为上下文视图（必须项；未标注的类不能被绑定器创建）。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ContextViewAttribute : Attribute
{
}

/// <summary>声明属性为只读数据面：读允许，写拒绝（<see cref="PermissionDeniedException"/>）。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ReadAttribute : Attribute
{
}

/// <summary>声明属性为读写数据面：读、写均允许（写入可增改）。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MutateAttribute : Attribute
{
}

/// <summary>修饰特性：声明属性可缺省（缺省读取返回 default）。需与 [Read] / [Mutate] 组合使用。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalAttribute : Attribute
{
}
