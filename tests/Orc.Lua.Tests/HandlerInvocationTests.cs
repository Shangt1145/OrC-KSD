using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 定义与调用成功路径：按名获取 / 取执行结果；参数表（空表、标量、null、嵌套）；返回值编组（标量 / 表自适应 / nil）。
/// </summary>
public class HandlerInvocationTests
{
    [Fact]
    public void ByNameFunction_Invoke_ReturnsMarshalledStringResult()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function greet(args)
                return "hi " .. args.name
            end
            """);

        var handler = TestInfra.HandlerOk(unit, "greet");
        var result = TestInfra.InvokeOk(handler, TestInfra.Args(("name", "orc")));

        Assert.Equal("hi orc", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResultFunction_Invoke_ReturnsMarshalledNumberResult()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            return function(args)
                return args.x + 1
            end
            """);

        var result = unit.GetResultFunction();
        Assert.True(result.IsSuccess, $"GetResultFunction failed: {result.Error}");
        var value = TestInfra.InvokeOk(result.Value!, TestInfra.Args(("x", 41)));

        Assert.Equal(42.0, Assert.IsType<double>(value));
    }

    [Fact]
    public void ReturnScalars_NumberStringBoolean_Marshalled()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function num() return 3.5 end
            function str() return "orc" end
            function flag() return true end
            """);

        Assert.Equal(3.5, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "num"))));
        Assert.Equal("orc", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "str"))));
        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "flag"))));
    }

    [Fact]
    public void ReturnPureSequenceTable_AsList()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return {10, 20, 30} end");

        var list = Assert.IsType<List<object?>>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f")));

        Assert.Equal(3, list.Count);
        Assert.Equal(10.0, Assert.IsType<double>(list[0]));
        Assert.Equal(20.0, Assert.IsType<double>(list[1]));
        Assert.Equal(30.0, Assert.IsType<double>(list[2]));
    }

    [Fact]
    public void ReturnStringKeyedTable_AsDictionary()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                return { name = "orc", level = 2 }
            end
            """);

        var dict = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f")));

        Assert.Equal("orc", dict["name"]);
        Assert.Equal(2.0, Assert.IsType<double>(dict["level"]));
    }

    [Fact]
    public void ReturnEmptyTable_AsEmptyDictionary()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return {} end");

        var dict = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f")));

        Assert.Empty(dict);
    }

    [Fact]
    public void ReturnNestedTable_AdaptiveRecursively()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                return { name = "orc", items = {1, 2}, meta = { nested = true } }
            end
            """);

        var dict = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f")));

        Assert.Equal("orc", dict["name"]);
        var items = Assert.IsType<List<object?>>(dict["items"]);
        Assert.Equal(2, items.Count);
        var meta = Assert.IsType<Dictionary<string, object?>>(dict["meta"]);
        Assert.True(Assert.IsType<bool>(meta["nested"]));
    }

    [Fact]
    public void ReturnNil_SuccessWithEmptyResult()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function implicit() end
            function explicit() return nil end
            """);

        Assert.Null(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "implicit")));
        Assert.Null(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "explicit")));
    }

    [Fact]
    public void MultipleReturnValues_OnlyFirstIsMarshalled()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return 1, 2, 3 end");

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void NoArguments_LuaReceivesExactlyOneEmptyTable()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f(args)
                return type(args) == "table" and next(args) == nil
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void EmptyDictionary_EquivalentToNoArguments()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return next(args) == nil end");

        Assert.True(Assert.IsType<bool>(
            TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"), TestInfra.Args())));
    }

    [Fact]
    public void ArgumentScalars_Null_NestedDictionariesAndLists_Marshalled()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f(args)
                return tostring(args.n)
                    .. "," .. tostring(args.maybe == nil)
                    .. "," .. tostring(args.list[2])
                    .. "," .. tostring(args.deep.inner)
                    .. "," .. tostring(args.flag)
                    .. "," .. tostring(args.text)
            end
            """);

        var result = TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"), TestInfra.Args(
            ("n", 5),
            ("maybe", null),
            ("list", new List<object?> { 10, 20, 30 }),
            ("deep", new Dictionary<string, object?> { ["inner"] = "x" }),
            ("flag", true),
            ("text", "s")));

        Assert.Equal("5,true,20,x,true,s", Assert.IsType<string>(result));
    }

    [Fact]
    public void EmptySource_InitializesSuccessfully_ButHasNoFunctions()
    {
        var engine = TestInfra.NewEngine();
        var result = engine.InitializeSource(string.Empty);

        Assert.True(result.IsSuccess, $"empty source should be a legal chunk: {result.Error}");
        var unit = result.Value!;
        Assert.True(unit.IsAvailable);

        var byName = unit.GetFunction("f");
        Assert.False(byName.IsSuccess);
        Assert.Equal(LuaErrorKind.MissingFunction, byName.Error!.Kind);

        var fromResult = unit.GetResultFunction();
        Assert.False(fromResult.IsSuccess);
        Assert.Equal(LuaErrorKind.MissingFunction, fromResult.Error!.Kind);
    }

    [Fact]
    public void GetFunction_ResolvesResultTableFirst_ThenGlobals()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function pick() return "global" end
            function onlyGlobal() return "g" end
            return { pick = function() return "result-table" end }
            """);

        // 结果表成员优先于同名全局。
        Assert.Equal("result-table", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "pick"))));
        // 结果表未命中 → 回退环境全局。
        Assert.Equal("g", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "onlyGlobal"))));
    }

    [Fact]
    public void MixedTableWithNumberKeys_LandsInDictionary()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                return { [1] = "a", name = "x" }
            end
            """);

        var dict = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f")));

        Assert.Equal("a", dict["1"]);
        Assert.Equal("x", dict["name"]);
    }
}
