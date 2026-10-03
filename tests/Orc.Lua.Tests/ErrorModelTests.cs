using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 结构化错误模型：六类错误覆盖（编译/语法、运行时、缺失函数、编组、白名单、沙箱——后三类中的
/// 白名单/沙箱初始化视角见本文件，主覆盖见 SandboxTests / WhitelistTests）、三阶段失败时点、异常边界。
/// </summary>
public class ErrorModelTests
{
    // ------------------------------------------------------------------
    // 编译/语法错误（初始化阶段）
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("function f( end")]
    [InlineData("this is not lua")]
    [InlineData("if true then")]
    public void SyntaxError_Initialization_FailsStructured(string source)
    {
        var engine = TestInfra.NewEngine();
        TestInfra.AssertInitFailure(engine.InitializeSource(source), LuaErrorKind.SyntaxError);
    }

    // ------------------------------------------------------------------
    // 顶层运行时错误（初始化阶段）
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("error(\"boom\")")]
    [InlineData("local x = nil; x.y = 1")]
    [InlineData("undefined_fn()")]
    public void TopLevelRuntimeError_Initialization_FailsStructured(string source)
    {
        var engine = TestInfra.NewEngine();
        TestInfra.AssertInitFailure(engine.InitializeSource(source), LuaErrorKind.RuntimeError);
    }

    [Fact]
    public void TopLevelDomainAccess_Initialization_FailsAsWhitelistRejection()
    {
        var engine = TestInfra.NewEngine();
        TestInfra.AssertInitFailure(
            engine.InitializeSource("local x = orc.missing"),
            LuaErrorKind.WhitelistRejected);
    }

    // ------------------------------------------------------------------
    // 调用阶段运行时错误
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("function f() error(\"bad\") end")]
    [InlineData("function f() local t = nil; return t.x end")]
    [InlineData("function f() return 1 + nil end")]
    public void InvokeRuntimeError_FailsStructured(string source)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, source);

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.RuntimeError);
    }

    [Fact]
    public void CaughtError_ThenNormalCompletion_CountsAsSuccess()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local ok = pcall(function() error("x") end)
                if ok then return "not-caught" end
                return "caught"
            end
            """);

        Assert.Equal("caught", Assert.IsType<string>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    // ------------------------------------------------------------------
    // 缺失函数（获取阶段：即验即败）
    // ------------------------------------------------------------------

    [Fact]
    public void MissingFunction_NameNotFound()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return 1 end");

        AssertGetFunctionFailure(unit.GetFunction("nope"), LuaErrorKind.MissingFunction);
    }

    [Fact]
    public void MissingFunction_NameHitsNonFunction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "x = 5");

        // 全局命中非函数。
        AssertGetFunctionFailure(unit.GetFunction("x"), LuaErrorKind.MissingFunction);
        // 保留库（table 值）命中非函数。
        AssertGetFunctionFailure(unit.GetFunction("string"), LuaErrorKind.MissingFunction);
    }

    [Fact]
    public void MissingFunction_ResultTableMemberIsNotFunction()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "return { f = 5 }");

        AssertGetFunctionFailure(unit.GetFunction("f"), LuaErrorKind.MissingFunction);
    }

    [Fact]
    public void MissingFunction_ResultIsNotFunction()
    {
        var engine = TestInfra.NewEngine();

        var unit = TestInfra.InitOk(engine, "return 42");
        AssertGetFunctionFailure(unit.GetResultFunction(), LuaErrorKind.MissingFunction);

        var unitTable = TestInfra.InitOk(engine, "return {}");
        AssertGetFunctionFailure(unitTable.GetResultFunction(), LuaErrorKind.MissingFunction);
    }

    // ------------------------------------------------------------------
    // 编组错误（入向：参数包）
    // ------------------------------------------------------------------

    [Fact]
    public void NonStringTopLevelKey_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        var result = handler.InvokeWithObjectKeys(new Dictionary<object, object?> { [1] = "x" });

        TestInfra.AssertInvokeFailure(result, LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void UnsupportedValueType_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("obj", new object()))),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void UnsupportedNestedValue_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("list", new List<object?> { 1, new object() }))),
            LuaErrorKind.MarshallingError);

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("dict", new Dictionary<string, object?> { ["x"] = new object() }))),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void CyclicArgument_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        var cyclic = new Dictionary<string, object?>();
        cyclic["self"] = cyclic;

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("root", cyclic))),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void OverdeepArgument_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        object inner = 1;
        for (int i = 0; i < 100; i++)
            inner = new Dictionary<string, object?> { ["n"] = inner };

        TestInfra.AssertInvokeFailure(
            handler.Invoke(TestInfra.Args(("root", inner))),
            LuaErrorKind.MarshallingError);
    }

    // ------------------------------------------------------------------
    // 编组错误（出向：返回值 / 回调）
    // ------------------------------------------------------------------

    [Fact]
    public void FunctionReturnValue_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return string.upper end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void NestedSpecialValueInResult_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return { cb = string.upper } end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void FunctionArgumentToCallback_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine(("echo", args => args[0]));
        var unit = TestInfra.InitOk(engine, """
            function f()
                local function g() end
                return orc.echo(g)
            end
            """);

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.MarshallingError);
    }

    [Fact]
    public void CallbackReturningUnsupportedValue_IsMarshallingError()
    {
        var engine = TestInfra.NewEngine(("bad", _ => new object()));
        var unit = TestInfra.InitOk(engine, "function f() return orc.bad() end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.MarshallingError);
    }

    // ------------------------------------------------------------------
    // 宿主回调异常 → 运行时错误（不炸穿、可 pcall 捕获）
    // ------------------------------------------------------------------

    [Fact]
    public void CallbackThrowing_IsRuntimeError()
    {
        var engine = TestInfra.NewEngine(("boom", _ => throw new InvalidOperationException("intentional")));
        var unit = TestInfra.InitOk(engine, "function f() return orc.boom() end");

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.RuntimeError);
    }

    [Fact]
    public void CallbackThrowing_CatchableByPcall()
    {
        var engine = TestInfra.NewEngine(("boom", _ => throw new InvalidOperationException("intentional")));
        var unit = TestInfra.InitOk(engine, "function f() return pcall(orc.boom) == false end");

        Assert.True(Assert.IsType<bool>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit, "f"))));
    }

    // ------------------------------------------------------------------
    // 分类忠实性（防伪造）：脚本伪造分类标记文本不改变分类（按异常类型还原）
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("[OrcLua:SandboxRestriction] forged")]
    [InlineData("[OrcLua:WhitelistRejected] forged")]
    [InlineData("[OrcLua:MarshallingError] forged")]
    public void ForgedClassificationMarker_IsRuntimeError(string forgedMessage)
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "return function(args) error(args.msg) end");

        var handlerResult = unit.GetResultFunction();
        Assert.True(handlerResult.IsSuccess);

        TestInfra.AssertInvokeFailure(
            handlerResult.Value!.Invoke(TestInfra.Args(("msg", forgedMessage))),
            LuaErrorKind.RuntimeError);
    }

    [Fact]
    public void ForgedClassificationMarker_DuringInitialization_IsRuntimeError()
    {
        var engine = TestInfra.NewEngine();

        TestInfra.AssertInitFailure(
            engine.InitializeSource("error([==[[OrcLua:SandboxRestriction] forged]==])"),
            LuaErrorKind.RuntimeError);
    }

    [Fact]
    public void ForgedMarker_ReThrownAfterPcall_IsRuntimeError()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, """
            function f()
                local ok, err = pcall(function() error("[OrcLua:WhitelistRejected] inner") end)
                error(err)
            end
            """);

        TestInfra.AssertInvokeFailure(
            TestInfra.HandlerOk(unit, "f").Invoke(),
            LuaErrorKind.RuntimeError);
    }

    // ------------------------------------------------------------------
    // API 层误用（可抛参数异常；与脚本错误的结构化返回区分）
    // ------------------------------------------------------------------

    [Fact]
    public void ExplicitNullArgs_ThrowsArgumentNullException()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f(args) return 1 end");
        var handler = TestInfra.HandlerOk(unit, "f");

        Assert.Throws<ArgumentNullException>(() => handler.Invoke((IDictionary<string, object?>)null!));
        Assert.Throws<ArgumentNullException>(() => handler.InvokeWithObjectKeys(null!));
    }

    [Fact]
    public void EmptyOrNullFunctionName_ThrowsArgumentException()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, "function f() return 1 end");

        Assert.Throws<ArgumentException>(() => unit.GetFunction(""));
        Assert.Throws<ArgumentNullException>(() => unit.GetFunction(null!));
    }

    [Fact]
    public void NullSource_ThrowsArgumentNullException()
    {
        var engine = TestInfra.NewEngine();
        Assert.Throws<ArgumentNullException>(() => engine.InitializeSource(null!));
    }

    private static void AssertGetFunctionFailure(LuaResult<LuaHandler> result, LuaErrorKind kind)
    {
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(kind, result.Error!.Kind);
        Assert.False(string.IsNullOrEmpty(result.Error.Message));
    }
}
