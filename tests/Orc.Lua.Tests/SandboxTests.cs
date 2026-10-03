using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 沙箱：禁用面（读 / 写 / 调用均以"沙箱限制"失败；含经 _G 的间接途径）＋
/// 保留面（必须可用）＋ string.dump 成员封锁 ＋ 局部遮蔽自由 ＋ pcall 捕获语义（能力永不授予）。
/// </summary>
public class SandboxTests
{
    [Theory]
    [InlineData("io")]
    [InlineData("os")]
    [InlineData("package")]
    [InlineData("debug")]
    [InlineData("require")]
    [InlineData("loadstring")]
    [InlineData("loadfile")]
    [InlineData("dofile")]
    [InlineData("load")]
    [InlineData("print")]
    [InlineData("collectgarbage")]
    [InlineData("rawget")]
    [InlineData("rawset")]
    [InlineData("rawlen")]
    [InlineData("rawequal")]
    [InlineData("setmetatable")]
    [InlineData("getmetatable")]
    public void BlockedName_Read_FailsWithSandboxRestriction(string name)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, $"function f() return {name} end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("os")]
    [InlineData("package")]
    [InlineData("debug")]
    [InlineData("require")]
    [InlineData("loadstring")]
    [InlineData("loadfile")]
    [InlineData("dofile")]
    [InlineData("load")]
    [InlineData("print")]
    [InlineData("collectgarbage")]
    [InlineData("rawget")]
    [InlineData("rawset")]
    [InlineData("rawlen")]
    [InlineData("rawequal")]
    [InlineData("setmetatable")]
    [InlineData("getmetatable")]
    public void BlockedName_Write_FailsWithSandboxRestriction(string name)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, $"function f() {name} = nil end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("require")]
    [InlineData("loadstring")]
    [InlineData("dofile")]
    [InlineData("print")]
    public void BlockedName_Call_FailsWithSandboxRestriction(string name)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, $"function f() return {name}(\"x\") end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("os")]
    [InlineData("print")]
    public void BlockedName_TopLevelAccess_FailsInitializationWithSandboxRestriction(string name)
    {
        var engine = TestInfra.NewEngine();
        var result = engine.InitializeSource($"local x = {name}");

        TestInfra.AssertInitFailure(result, LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void BlockedName_TopLevelWrite_FailsInitializationWithSandboxRestriction()
    {
        var engine = TestInfra.NewEngine();
        var result = engine.InitializeSource("io = {}");

        TestInfra.AssertInitFailure(result, LuaErrorKind.SandboxRestriction);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("os")]
    [InlineData("print")]
    public void BlockedName_IndirectReadViaG_FailsWithSandboxRestriction(string name)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, $"function f() return _G.{name} end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void BlockedName_IndirectWriteViaG_FailsWithSandboxRestriction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() _G.print = function() end end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void Retained_StringMathTableLibraries_AreUsable()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local arr = {10, 20, 30}
                table.insert(arr, 40)
                return string.upper("orc") == "ORC"
                    and math.max(3, 7) == 7
                    and #arr == 4
                    and table.concat({"a", "b"}, "-") == "a-b"
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void Retained_Iterators_AreUsable()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local t = {a = 1, b = 2}
                local sum = 0
                for k, v in pairs(t) do sum = sum + v end
                local arr = {10, 20, 30}
                local total = 0
                for i, v in ipairs(arr) do total = total + v end
                return sum == 3 and total == 60 and next(t) ~= nil
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void Retained_BaseFunctions_AreUsable()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                return type({}) == "table"
                    and tostring(42) == "42"
                    and tonumber("42") == 42
                    and select("#", 1, 2, 3) == 3
                    and unpack({10, 20}) == 10
                    and (pcall(error, "boom")) == false
                    and assert(true) == true
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void Retained_StringColonCall_Works()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return (\"abc\"):upper() end");

        Assert.Equal("ABC", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void StringDump_Read_FailsWithSandboxRestriction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return string.dump end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void StringDump_Call_FailsWithSandboxRestriction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return string.dump(function() end) end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void StringDump_Write_FailsWithSandboxRestriction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() string.dump = function() end end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.SandboxRestriction);
    }

    [Fact]
    public void SandboxError_CatchableByPcall_CallSucceeds()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local ok = pcall(function() return io end)
                return ok == false
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void SandboxError_CaughtByPcall_StillGrantsNoCapability()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local captured
                local ok = pcall(function() captured = io end)
                return captured == nil and ok == false
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void LocalShadowing_OfBlockedName_IsFree()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local io = { read = 5 }
                local result = io.read
                io.read = 6
                return result + io.read
            end
            """);

        Assert.Equal(11.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void BlockedFailure_DoesNotAffectOtherHandlers()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function bad() return io end
            function good() return 1 end
            """);

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "bad").Invoke(),
            LuaErrorKind.SandboxRestriction);

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "good"))));
    }
}
