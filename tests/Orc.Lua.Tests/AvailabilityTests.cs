using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 失败后可用性与恢复：不可用单元（复现原分类）、初始化重试、失败后 handler 仍可用（不回滚）、
/// 快照语义（正/反例）、错误隔离、同一线程重入（脚本 → 宿主回调 → 再调用 handler）。
/// </summary>
public class AvailabilityTests
{
    [Fact]
    public void FailedUnit_SyntaxError_IsUnavailable_OperationsReproduceOriginalKind()
    {
        var engine = TestInfra.NewEngine();
        var result = engine.InitializeSource("function f( end");

        TestInfra.AssertInitFailure(result, LuaErrorKind.SyntaxError);

        // 失败结果携带不可用单元句柄：后续获取操作复现原分类（不再执行任何操作）。
        var unit = result.Value;
        Assert.NotNull(unit);
        Assert.False(unit!.IsAvailable);
        Assert.NotNull(unit.InitializationError);
        Assert.Equal(LuaErrorKind.SyntaxError, unit.InitializationError!.Kind);

        var byName = unit.GetFunction("f");
        Assert.False(byName.IsSuccess);
        Assert.Equal(LuaErrorKind.SyntaxError, byName.Error!.Kind);
        Assert.False(string.IsNullOrEmpty(byName.Error.Message));

        var byResult = unit.GetResultFunction();
        Assert.False(byResult.IsSuccess);
        Assert.Equal(LuaErrorKind.SyntaxError, byResult.Error!.Kind);
    }

    [Fact]
    public void FailedUnit_SandboxKind_IsReproduced()
    {
        var engine = TestInfra.NewEngine();
        var result = engine.InitializeSource("local x = io");

        TestInfra.AssertInitFailure(result, LuaErrorKind.SandboxRestriction);

        var unit = result.Value;
        Assert.NotNull(unit);
        Assert.False(unit!.IsAvailable);

        var byName = unit.GetFunction("f");
        Assert.False(byName.IsSuccess);
        Assert.Equal(LuaErrorKind.SandboxRestriction, byName.Error!.Kind);
    }

    [Fact]
    public void RetryAfterFailedInitialization_ProducesFreshUsableUnit()
    {
        var engine = TestInfra.NewEngine();
        TestInfra.AssertInitFailure(engine.InitializeSource("function f( end"), LuaErrorKind.SyntaxError);

        var unit = TestInfra.InitOk(engine, "function f() return 7 end");
        Assert.Equal(7.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void Snapshot_HostMutationOfResult_DoesNotAffectLuaState()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            state = { x = 1 }
            function f() return state end
            """);
        var handler = TestInfra.HandlerOk(unit, "f");

        var first = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(handler));
        Assert.Equal(1.0, Assert.IsType<double>(first["x"]));

        // 宿主修改返回对象：不影响 Lua 侧内部状态。
        first["x"] = 999.0;
        first["extra"] = true;

        var second = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(handler));
        Assert.Equal(1.0, Assert.IsType<double>(second["x"]));
        Assert.False(second.ContainsKey("extra"));
    }

    [Fact]
    public void Snapshot_LuaMutationOfArgs_DoesNotAffectHostObjects()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f(args)
                args.list[1] = 999
                args.added = "yes"
                return { first = args.list[1], added = args.added }
            end
            """);
        var handler = TestInfra.HandlerOk(unit, "f");

        var hostList = new List<object?> { 10, 20 };
        var result = Assert.IsType<Dictionary<string, object?>>(TestInfra.InvokeOk(
            handler, TestInfra.Args(("list", hostList))));

        // Lua 侧看到的是快照（改了副本）。
        Assert.Equal(999.0, Assert.IsType<double>(result["first"]));
        Assert.Equal("yes", result["added"]);
        // 宿主原对象不受影响。
        Assert.Equal(10, hostList[0]);
        Assert.Equal(2, hostList.Count);
    }

    [Fact]
    public void FailThenSucceed_HandlerStillUsable_NoRollbackRequired()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            count = 0
            function f(args)
                count = count + 1
                if args.fail then error("intentional") end
                return count
            end
            """);
        var handler = TestInfra.HandlerOk(unit, "f");

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("fail", true))),
            LuaErrorKind.RuntimeError);

        // 失败后再调用仍可用；且不要求状态回滚（失败前已执行部分（count + 1）保留）。
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(handler)));
    }

    [Fact]
    public void FailureInOneUnit_DoesNotAffectAnotherUnit()
    {
        var engine = TestInfra.NewEngine();
        var unitA = TestInfra.InitOk(engine, "function f() error(\"x\") end");
        var unitB = TestInfra.InitOk(engine, "function f() return 5 end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unitA, "f").Invoke(),
            LuaErrorKind.RuntimeError);

        Assert.Equal(5.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitB, "f"))));
    }

    [Fact]
    public void Reentrancy_CallbackInvokesHandler_CrossSource()
    {
        LuaHandler? inner = null;
        var engine = TestInfra.NewEngine(("callInner", _ => inner!.Invoke().Value));

        var unitInner = TestInfra.InitOk(engine, "function innerFn() return 42 end");
        inner = TestInfra.HandlerOk(unitInner, "innerFn");

        var unitOuter = TestInfra.InitOk(engine, "function f() return orc.callInner() end");

        // 脚本 → 宿主回调 → 再调用（另一源的）handler：同一线程串行嵌套。
        Assert.Equal(42.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitOuter, "f"))));
    }

    [Fact]
    public void Reentrancy_CallbackInvokesHandler_SameSource()
    {
        LuaHandler? slot = null;
        var engine = TestInfra.NewEngine(("relay", _ => slot!.Invoke().Value));

        var unit = TestInfra.InitOk(engine, """
            function g() return 7 end
            function f() return orc.relay() end
            """);
        slot = TestInfra.HandlerOk(unit, "g");

        // 脚本 → 宿主回调 → 再调用（同源的）handler：同一环境、串行嵌套。
        Assert.Equal(7.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }
}
