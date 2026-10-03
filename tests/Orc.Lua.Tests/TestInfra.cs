using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>测试基础设施：组件实例创建与常用断言辅助。</summary>
internal static class TestInfra
{
    /// <summary>创建组件实例并注册给定的宿主回调。</summary>
    internal static LuaHandlerEngine NewEngine(params (string Name, Func<object?[], object?> Callback)[] verbs)
    {
        var engine = new LuaHandlerEngine();
        foreach (var (name, callback) in verbs)
            engine.RegisterVerb(name, callback);
        return engine;
    }

    /// <summary>初始化源并断言成功，返回可用单元。</summary>
    internal static LuaSourceUnit InitOk(LuaHandlerEngine engine, string source)
    {
        var result = engine.InitializeSource(source);
        Assert.True(result.IsSuccess, $"initialization should succeed but failed: {result.Error}");
        return result.Value!;
    }

    /// <summary>按名获取 handler 并断言成功。</summary>
    internal static LuaHandler HandlerOk(LuaSourceUnit unit, string name)
    {
        var result = unit.GetFunction(name);
        Assert.True(result.IsSuccess, $"GetFunction('{name}') should succeed but failed: {result.Error}");
        return result.Value!;
    }

    /// <summary>调用 handler 并断言成功，返回编组结果。</summary>
    internal static object? InvokeOk(LuaHandler handler, IDictionary<string, object?>? args = null)
    {
        var result = args is null ? handler.Invoke() : handler.Invoke(args);
        Assert.True(result.IsSuccess, $"invoke should succeed but failed: {result.Error}");
        return result.Value;
    }

    /// <summary>断言调用失败并返回结构化错误（分类 + 非空消息）。</summary>
    internal static LuaError AssertInvokeFailure(LuaResult<object?> result, LuaErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.False(string.IsNullOrEmpty(result.Error.Message));
        return result.Error;
    }

    /// <summary>断言初始化失败并返回结构化错误（分类 + 非空消息）。</summary>
    internal static LuaError AssertInitFailure(LuaResult<LuaSourceUnit> result, LuaErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.False(string.IsNullOrEmpty(result.Error.Message));
        return result.Error;
    }

    /// <summary>构造字符串键参数包。</summary>
    internal static Dictionary<string, object?> Args(params (string Key, object? Value)[] items)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in items)
            dict[key] = value;
        return dict;
    }
}
