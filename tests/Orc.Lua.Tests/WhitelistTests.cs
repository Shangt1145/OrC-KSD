using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 白名单（动词域）：注册（层级 / 重复拒绝 / 时序约束）、访问（未注册拒绝；注册可调）、
/// 写保护（能力不可篡改；含 _G 间接途径）、局部遮蔽自由、宿主回调语义、多实例独立。
/// </summary>
public class WhitelistTests
{
    [Fact]
    public void Domain_ExistsEvenWithoutRegistrations()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f() return type(orc) end
            function g() return orc.anything end
            """);

        // 读域本身合法（域始终存在，与注册数无关）。
        Assert.Equal("table", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
        // 域下未注册成员（0 注册时即全部）→ 白名单拒绝。
        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "g").Invoke(),
            LuaErrorKind.WhitelistRejected);
    }

    [Fact]
    public void UnregisteredMember_Read_FailsWithWhitelistRejection()
    {
        var engine = TestInfra.NewEngine(("greet", _ => "hi"));
        var unit = TestInfra.InitOk(engine, "function f() return orc.missing end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.WhitelistRejected);
    }

    [Fact]
    public void UnregisteredMember_Call_FailsWithWhitelistRejection()
    {
        var engine = TestInfra.NewEngine(("greet", _ => "hi"));
        var unit = TestInfra.InitOk(engine, "function f() return orc.missing(\"x\") end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.WhitelistRejected);
    }

    [Fact]
    public void RegisteredVerb_IsCallable_AndHostCallbackObserved()
    {
        var called = false;
        var engine = TestInfra.NewEngine(
            ("greet", args =>
            {
                called = true;
                return "hello " + args[0];
            }));
        var unit = TestInfra.InitOk(engine, "function f() return orc.greet(\"orc\") end");

        Assert.Equal("hello orc", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
        Assert.True(called, "host callback should have been observed on the host side");
    }

    [Fact]
    public void NestedVerbNames_MapToNestedDomainValues()
    {
        var engine = TestInfra.NewEngine(("combat.play", args => Convert.ToDouble(args[0]) * 2));
        var unit = TestInfra.InitOk(engine, """
            function f() return orc.combat.play(6) end
            function g() return type(orc.combat) end
            """);

        Assert.Equal(12.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
        Assert.Equal("table", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "g"))));
    }

    [Theory]
    [InlineData("orc.newthing = 1")]
    [InlineData("orc.greet = function() return 1 end")]
    [InlineData("orc.combat = {}")]
    [InlineData("orc = {}")]
    [InlineData("orc = nil")]
    [InlineData("_G.orc = {}")]
    [InlineData("_G.orc.greet = function() return 1 end")]
    [InlineData("_G.orc.combat = nil")]
    public void DomainWrite_FailsWithWhitelistRejection(string statement)
    {
        var engine = TestInfra.NewEngine(
            ("greet", _ => "hi"),
            ("combat.play", _ => 1.0));
        var unit = TestInfra.InitOk(engine, $"function f() {statement} end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.WhitelistRejected);
    }

    [Fact]
    public void WhitelistError_CatchableByPcall_StillGrantsNoCapability()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local captured
                local ok = pcall(function() captured = orc.missing end)
                return captured == nil and ok == false
            end
            """);

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void LocalShadowing_OfDomainName_IsFree()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local orc = { x = 3 }
                orc.x = 4
                orc.extra = 5
                return orc.x + orc.extra
            end
            """);

        Assert.Equal(9.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void HostCallback_ReceivesMarshalledArguments()
    {
        var engine = TestInfra.NewEngine(("sum", args => (double)args[0]! + (double)args[1]!));
        var unit = TestInfra.InitOk(engine, "function f() return orc.sum(3, 4) end");

        Assert.Equal(7.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void HostCallback_NullReturn_MapsToNilNormally()
    {
        var engine = TestInfra.NewEngine(("nothing", _ => null));
        var unit = TestInfra.InitOk(engine, "function f() return orc.nothing() == nil end");

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void HostCallback_ReturningNestedStructures_MarshalsToLuaValues()
    {
        var engine = TestInfra.NewEngine(("make", _ => new Dictionary<string, object?>
        {
            ["a"] = 5,
            ["list"] = new List<object?> { 1, 2 },
        }));
        var unit = TestInfra.InitOk(engine, """
            function f()
                local t = orc.make()
                return t.a + t.list[2]
            end
            """);

        Assert.Equal(7.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Fact]
    public void MultipleEngines_WhitelistsAreIndependent()
    {
        var engine1 = TestInfra.NewEngine(("greet", _ => "hi"));
        var engine2 = TestInfra.NewEngine();

        var unit1 = TestInfra.InitOk(engine1, "function f() return orc.greet() end");
        Assert.Equal("hi", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit1, "f"))));

        var unit2 = TestInfra.InitOk(engine2, "function f() return orc.greet() end");
        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit2, "f").Invoke(),
            LuaErrorKind.WhitelistRejected);
    }

    [Fact]
    public void RootNamespace_IsConfigurable()
    {
        var engine = new LuaHandlerEngine(new LuaHandlerEngineOptions { RootNamespace = "diymod" });
        engine.RegisterVerb("echo", args => args[0]);

        var unit = TestInfra.InitOk(engine, """
            function f() return diymod.echo("x") end
            function g() return tostring(orc) end
            """);

        Assert.Equal("x", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
        // 非配置的默认域名自然缺失（nil）。
        Assert.Equal("nil", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "g"))));
    }

    [Fact]
    public void ValidNestedIdentifiers_AreAccepted()
    {
        var engine = TestInfra.NewEngine();
        engine.RegisterVerb("quest.stage_2", _ => 1.0);

        var unit = TestInfra.InitOk(engine, "function f() return orc.quest.stage_2() end");

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("a-b")]
    [InlineData("1abc")]
    [InlineData("a b")]
    public void InvalidVerbNames_AreRejectedWithArgumentException(string name)
    {
        var engine = TestInfra.NewEngine();
        Assert.Throws<ArgumentException>(() => engine.RegisterVerb(name, _ => null));
    }

    [Fact]
    public void NullVerbName_ThrowsArgumentNullException()
    {
        var engine = TestInfra.NewEngine();
        Assert.Throws<ArgumentNullException>(() => engine.RegisterVerb(null!, _ => null));
    }

    [Fact]
    public void NullCallback_ThrowsArgumentNullException()
    {
        var engine = TestInfra.NewEngine();
        Assert.Throws<ArgumentNullException>(() => engine.RegisterVerb("ok", null!));
    }

    [Fact]
    public void DuplicateRegistration_IsRejected()
    {
        var engine = TestInfra.NewEngine();
        engine.RegisterVerb("a", _ => null);
        Assert.Throws<ArgumentException>(() => engine.RegisterVerb("a", _ => null));
    }

    [Fact]
    public void NamespaceConflicts_BothDirections_AreRejected()
    {
        var engine = TestInfra.NewEngine();
        engine.RegisterVerb("a", _ => null);
        Assert.Throws<ArgumentException>(() => engine.RegisterVerb("a.b", _ => null));

        var other = TestInfra.NewEngine();
        other.RegisterVerb("x.y", _ => null);
        Assert.Throws<ArgumentException>(() => other.RegisterVerb("x", _ => null));
    }

    [Fact]
    public void RegisterAfterInitialization_IsRejectedExplicitly()
    {
        var engine = TestInfra.NewEngine();
        TestInfra.InitOk(engine, "function f() return 1 end");

        Assert.Throws<InvalidOperationException>(() => engine.RegisterVerb("late", _ => null));
    }

    [Fact]
    public void RegisterAfterFailedInitialization_IsAlsoRejected()
    {
        var engine = TestInfra.NewEngine();
        engine.InitializeSource("function f( end");

        Assert.Throws<InvalidOperationException>(() => engine.RegisterVerb("late", _ => null));
    }
}
