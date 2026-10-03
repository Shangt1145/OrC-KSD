using System.Linq.Expressions;
using System.Reflection;

namespace Orc.Core;

/// <summary>属性的访问权限级别（权限矩阵的一格）。</summary>
internal enum ViewPropertyAccess
{
    /// <summary>无有效访问特性（无 [Read]/[Mutate]，含仅 [Optional] 而缺主特性）：读写均拒绝。</summary>
    None,

    /// <summary>[Read]：读允许，写拒绝。</summary>
    Read,

    /// <summary>[Mutate]：读、写均允许。</summary>
    Mutate,
}

/// <summary>单个视图属性的构建期元数据（权限、缺省、失效判定、代理生成所需信息）。</summary>
internal sealed class PropertyMeta
{
    internal PropertyMeta(PropertyInfo property, ViewPropertyAccess access, bool optional, bool isRefType)
    {
        Property = property;
        Access = access;
        Optional = optional;
        IsRefType = isRefType;
    }

    internal PropertyInfo Property { get; }

    internal string Name => Property.Name;

    internal ViewPropertyAccess Access { get; }

    internal bool Optional { get; }

    internal bool IsRefType { get; }
}

/// <summary>
/// 视图类型的元数据：必填清单、框架产物类型与实例工厂。
/// 由绑定器按视图类型经 ConcurrentDictionary 缓存；同一视图类型的多次创建行为一致。
/// </summary>
internal sealed class ViewMeta
{
    private ViewMeta(Type viewType, Type proxyType, IReadOnlyList<string> requiredNames, Func<object> createInstance)
    {
        ViewType = viewType;
        ProxyType = proxyType;
        RequiredNames = requiredNames;
        CreateInstance = createInstance;
    }

    internal Type ViewType { get; }

    internal Type ProxyType { get; }

    /// <summary>必填字段名清单：非 [Optional] 且带主特性（[Read]/[Mutate]）的属性。</summary>
    internal IReadOnlyList<string> RequiredNames { get; }

    /// <summary>框架产物实例工厂（每次调用新建一个视图实例——每次执行会话新建实例）。</summary>
    internal Func<object> CreateInstance { get; }

    /// <summary>
    /// 构建视图元数据：声明校验（[ContextView] 必须、可继承、public、无参构造、主特性唯一、访问器 virtual），
    /// 构建属性表与必填清单，生成框架产物类型。校验失败在此阶段抛 <see cref="ArgumentException"/>（绑定期明确拒绝）。
    /// </summary>
    internal static ViewMeta Build(Type viewType)
    {
        if (!viewType.IsDefined(typeof(ContextViewAttribute), inherit: true))
        {
            throw new ArgumentException($"视图类型 '{viewType.FullName}' 未标注 [ContextView]，不能被创建。", nameof(viewType));
        }

        if (viewType.IsSealed)
        {
            throw new ArgumentException($"视图类型 '{viewType.FullName}' 不能为 sealed（框架产物需要派生）。", nameof(viewType));
        }

        if (!viewType.IsPublic && !viewType.IsNestedPublic)
        {
            throw new ArgumentException($"视图类型 '{viewType.FullName}' 必须为 public（框架产物需跨程序集派生）。", nameof(viewType));
        }

        if (viewType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new ArgumentException($"视图类型 '{viewType.FullName}' 需要 public 无参构造。", nameof(viewType));
        }

        var properties = new List<PropertyMeta>();
        var requiredNames = new List<string>();

        foreach (var property in viewType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue; // 不处理索引器（S1 视图不提供访问任意键的通用入口）
            }

            var read = property.IsDefined(typeof(ReadAttribute), inherit: true);
            var mutate = property.IsDefined(typeof(MutateAttribute), inherit: true);
            var optional = property.IsDefined(typeof(OptionalAttribute), inherit: true);

            if (read && mutate)
            {
                throw new ArgumentException(
                    $"视图属性 '{viewType.Name}.{property.Name}' 不能同时标注 [Read] 与 [Mutate]（非法声明）。",
                    nameof(viewType));
            }

            var getter = property.GetGetMethod();
            var setter = property.GetSetMethod();
            if (getter is null && setter is null)
            {
                continue;
            }

            if (getter is not null && (!getter.IsVirtual || getter.IsFinal))
            {
                throw new ArgumentException(
                    $"视图属性访问器 '{viewType.Name}.{property.Name}.get' 必须为 virtual（框架产物需覆写以实施访问闸门）。",
                    nameof(viewType));
            }

            if (setter is not null && (!setter.IsVirtual || setter.IsFinal))
            {
                throw new ArgumentException(
                    $"视图属性访问器 '{viewType.Name}.{property.Name}.set' 必须为 virtual（框架产物需覆写以实施访问闸门）。",
                    nameof(viewType));
            }

            var access = mutate
                ? ViewPropertyAccess.Mutate
                : read
                    ? ViewPropertyAccess.Read
                    : ViewPropertyAccess.None;

            var propertyType = property.PropertyType;
            var isRefType = propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(Ref<>);

            properties.Add(new PropertyMeta(property, access, optional, isRefType));

            if (!optional && access != ViewPropertyAccess.None)
            {
                requiredNames.Add(property.Name);
            }
        }

        var proxyType = ViewProxyFactory.CreateProxyType(viewType, properties);
        return new ViewMeta(viewType, proxyType, requiredNames, BuildFactory(proxyType));
    }

    private static Func<object> BuildFactory(Type proxyType) =>
        Expression
            .Lambda<Func<object>>(Expression.Convert(Expression.New(proxyType), typeof(object)))
            .Compile();
}
