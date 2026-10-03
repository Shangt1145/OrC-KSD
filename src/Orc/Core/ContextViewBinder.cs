using System.Collections.Concurrent;

namespace Orc.Core;

/// <summary>
/// 视图绑定器（S1 反射方案）：创建并绑定框架产物视图实例。
/// 元数据（属性表 / 必填清单 / 框架产物类型 / 实例工厂）按视图类型经 ConcurrentDictionary 缓存；
/// 缓存不引入行为差异——同一视图类型的多次创建/绑定行为一致。
/// 未来可在保持公共接口签名不变的前提下切换 Source Generator 方案。
/// </summary>
public static class ContextViewBinder
{
    private static readonly ConcurrentDictionary<Type, ViewMeta> Cache = new();

    /// <summary>
    /// 为视图类型创建并绑定视图实例。数据唯一来源为 ctx.Data 载体（不引入第二数据源）。
    /// 绑定前执行必填校验（EnsureRequired）；失败即抛、不返回实例（不存在半可用实例）。
    /// </summary>
    /// <exception cref="ArgumentNullException">ctx 为 null。</exception>
    /// <exception cref="ArgumentException">视图声明不合法（未标 [ContextView]、[Read]/[Mutate] 冲突、访问器非 virtual 等）。</exception>
    /// <exception cref="KeyNotFoundException">必填字段缺失（键不存在或值为 null）。</exception>
    public static TView Create<TView>(Context ctx) where TView : class
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var meta = Cache.GetOrAdd(typeof(TView), static viewType => ViewMeta.Build(viewType));
        EnsureRequired(meta, ctx.Data);

        var instance = meta.CreateInstance();
        ((IContextView)instance).Bind(ctx.Data, ctx);
        return (TView)instance;
    }

    /// <summary>必填校验：非 [Optional] 且带主特性的属性，其键必须存在且值非 null。</summary>
    private static void EnsureRequired(ViewMeta meta, Dictionary<string, object?> data)
    {
        foreach (var name in meta.RequiredNames)
        {
            if (!data.TryGetValue(name, out var value) || value is null)
            {
                throw new KeyNotFoundException($"视图必填字段 '{name}' 缺失（键不存在或值为 null）。");
            }
        }
    }
}
