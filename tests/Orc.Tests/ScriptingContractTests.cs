using Orc.Core;
using Xunit;

namespace Orc.Tests;

/// <summary>
/// S-C6（内核半段）验收：求值契约结构化、经引擎注入、擦除形态委托按视图类型绑定。
/// </summary>
public class ScriptingContractTests
{
    private sealed class FakeEvaluator : IScriptEvaluator
    {
        public ScriptRequest? Last { get; private set; }

        public ScriptEvaluationResult Evaluate(ScriptRequest request)
        {
            Last = request;
            return ScriptEvaluationResult.Ok(
                (Func<CounterView, Context, CancellationToken, Task>)((v, c, ct) => Task.CompletedTask));
        }
    }

    [Fact]
    public void 脚本求值器_可空且经引擎注入()
    {
        var engine = new LogicEngine();
        Assert.Null(engine.ScriptEvaluator); // 未装配＝不可用（内核零依赖）

        engine.ScriptEvaluator = new FakeEvaluator();
        Assert.NotNull(engine.ScriptEvaluator);
    }

    [Fact]
    public void 求值结果_成功_可绑定回泛型签名()
    {
        var engine = new LogicEngine { ScriptEvaluator = new FakeEvaluator() };

        var result = engine.ScriptEvaluator!.Evaluate(
            new ScriptRequest("Task HandleAsync(Context ctx) => Task.CompletedTask;", "HandleAsync", typeof(CounterView)));

        Assert.True(result.Success);
        Assert.NotNull(result.Handler);

        var bound = ScriptHandlerAdapter.TryBind<CounterView>(result.Handler!);
        Assert.NotNull(bound);
    }

    [Fact]
    public void 求值结果_失败_结构化不抛()
    {
        var failure = ScriptEvaluationResult.Fail("compile", "语法错误");

        Assert.False(failure.Success);
        Assert.Null(failure.Handler);
        Assert.Equal("compile", failure.ErrorCategory);
        Assert.Equal("语法错误", failure.Error);
    }

    [Fact]
    public void 求值结果_成功须携带委托()
    {
        Assert.Throws<ArgumentNullException>(() => ScriptEvaluationResult.Ok(null!));
    }

    [Fact]
    public void 适配_泛型重载_类型不符返回null()
    {
        Delegate wrong = (Func<MatrixView, Context, CancellationToken, Task>)((v, c, ct) => Task.CompletedTask);
        Assert.Null(ScriptHandlerAdapter.TryBind<CounterView>(wrong));
    }

    [Fact]
    public void 适配_运行期类型重载()
    {
        Delegate handler = (Func<CounterView, Context, CancellationToken, Task>)((v, c, ct) => Task.CompletedTask);

        var bound = ScriptHandlerAdapter.TryBind(typeof(CounterView), handler);

        Assert.NotNull(bound);
        Assert.IsType<Func<CounterView, Context, CancellationToken, Task>>(bound);
        Assert.Null(ScriptHandlerAdapter.TryBind(typeof(MatrixView), handler));
    }
}
