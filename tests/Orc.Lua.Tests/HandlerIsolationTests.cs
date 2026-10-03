using Orc.Lua;
using Xunit;

namespace Orc.Lua.Tests;

/// <summary>
/// 隔离语义：同源共享环境 / 跨源隔离 / 每次初始化独立 / 重复获取等价引用 / 调用间状态延续。
/// </summary>
public class HandlerIsolationTests
{
    private const string CounterSource = """
        count = 0
        function inc()
            count = count + 1
            return count
        end
        function get()
            return count
        end
        """;

    [Fact]
    public void SameSource_HandlersShareEnvironmentState()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, CounterSource);

        var inc = TestInfra.HandlerOk(unit, "inc");
        var get = TestInfra.HandlerOk(unit, "get");

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(inc)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(inc)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(get)));
    }

    [Fact]
    public void SameHandler_RepeatedInvocation_StateContinues()
    {
        var engine = TestInfra.NewEngine();
        var inc = TestInfra.HandlerOk(TestInfra.InitOk(engine, CounterSource), "inc");

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(inc)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(inc)));
        Assert.Equal(3.0, Assert.IsType<double>(TestInfra.InvokeOk(inc)));
    }

    [Fact]
    public void DifferentSources_EnvironmentsAreIsolated()
    {
        var engine = TestInfra.NewEngine();
        var unitA = TestInfra.InitOk(engine, CounterSource);
        var unitB = TestInfra.InitOk(engine, CounterSource);

        var incA = TestInfra.HandlerOk(unitA, "inc");
        var incB = TestInfra.HandlerOk(unitB, "inc");

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(incA)));
        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(incB)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(incA)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(incB)));
    }

    [Fact]
    public void SameSourceText_TwoInitializations_TwoIndependentEnvironments()
    {
        var engine = TestInfra.NewEngine();
        var unit1 = TestInfra.InitOk(engine, CounterSource);
        var unit2 = TestInfra.InitOk(engine, CounterSource);

        // 同一文本两次初始化 = 两个互不可见的环境（R8）。
        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit1, "inc"))));
        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unit2, "inc"))));
    }

    [Fact]
    public void RepeatedGetFunction_SameEnvironment_EquivalentReferences()
    {
        var engine = TestInfra.NewEngine();
        var unit = TestInfra.InitOk(engine, CounterSource);

        var first = TestInfra.HandlerOk(unit, "inc");
        var second = TestInfra.HandlerOk(unit, "inc");

        // 同一源被获取 N 次 = 同一环境中的等价引用（共享状态），不产生独立环境。
        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(first)));
        Assert.Equal(2.0, Assert.IsType<double>(TestInfra.InvokeOk(second)));
    }

    [Fact]
    public void CrossSource_GlobalsAndFunctionsAreInvisible()
    {
        var engine = TestInfra.NewEngine();
        var unitA = TestInfra.InitOk(engine, """
            g = 42
            function f() return g end
            """);
        var unitB = TestInfra.InitOk(engine, """
            function f() return g end
            """);

        // A 的全局 g 对 B 不可见。
        Assert.Equal(42.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitA, "f"))));
        Assert.Null(TestInfra.InvokeOk(TestInfra.HandlerOk(unitB, "f")));
    }

    [Fact]
    public void CrossSource_SameNamesDoNotInterfere()
    {
        var engine = TestInfra.NewEngine();
        var unitA = TestInfra.InitOk(engine, """
            g = 1
            function f() return g end
            """);
        var unitB = TestInfra.InitOk(engine, """
            g = 7
            function f() return g end
            """);

        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitA, "f"))));
        Assert.Equal(7.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitB, "f"))));
        Assert.Equal(1.0, Assert.IsType<double>(TestInfra.InvokeOk(TestInfra.HandlerOk(unitA, "f"))));
    }
}
