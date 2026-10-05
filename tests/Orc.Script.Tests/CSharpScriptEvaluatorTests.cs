using Orc.Cards;
using Orc.Core;
using Orc.Script;
using Xunit;

namespace Orc.Script.Tests;

/// <summary>
/// S-C6 satellite 验收：csx 求值（成功/编译失败/沙箱拒绝/入口缺失/运行时异常/签名不符）与缓存。
/// </summary>
public class CSharpScriptEvaluatorTests
{
    private static ScriptRequest Request(string source, string entry = "HandleAsync") =>
        new(source, entry, typeof(CardEventView));

    private const string ValidScript =
        "Func<CardEventView, Context, CancellationToken, Task> HandleAsync = (view, ctx, ct) => Task.CompletedTask;";

    [Fact]
    public void 有效脚本_编译成功且可绑定()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request(ValidScript));

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Handler);
        Assert.IsType<Func<CardEventView, Context, CancellationToken, Task>>(result.Handler);
    }

    [Fact]
    public void 同一请求_命中缓存()
    {
        var evaluator = new CSharpScriptEvaluator();

        var first = evaluator.Evaluate(Request(ValidScript));
        var second = evaluator.Evaluate(Request(ValidScript));

        Assert.Same(first, second);
        Assert.Equal(1, evaluator.CacheCount);
    }

    [Fact]
    public void 语法错误_编译失败()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request("this is not valid C# ;;;"));

        Assert.False(result.Success);
        Assert.Equal("compile", result.ErrorCategory);
    }

    [Fact]
    public void 命中屏蔽片段_沙箱拒绝()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request(
            "var x = File.ReadAllText(\"a\"); Func<CardEventView, Context, CancellationToken, Task> HandleAsync = (v, c, t) => Task.CompletedTask;"));

        Assert.False(result.Success);
        Assert.Equal("sandbox", result.ErrorCategory);
        Assert.Contains("File", result.Error);
    }

    [Fact]
    public void 入口缺失_结构化失败()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request("var x = 1;"));

        Assert.False(result.Success);
        Assert.Equal("entry-missing", result.ErrorCategory);
    }

    [Fact]
    public void 入口非委托_结构化失败()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request("var HandleAsync = 42;"));

        Assert.False(result.Success);
        Assert.Equal("entry-missing", result.ErrorCategory);
    }

    [Fact]
    public void 签名不符_结构化失败()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request(
            "Func<int, int> HandleAsync = x => x + 1;"));

        Assert.False(result.Success);
        Assert.Equal("signature", result.ErrorCategory);
    }

    [Fact]
    public void 运行时异常_结构化失败()
    {
        var evaluator = new CSharpScriptEvaluator();

        var result = evaluator.Evaluate(Request("throw new InvalidOperationException(\"boom\");"));

        Assert.False(result.Success);
        Assert.Equal("runtime", result.ErrorCategory);
    }

    [Fact]
    public void 求值器_可注入引擎并绑定到触发器签名()
    {
        var engine = new LogicEngine { ScriptEvaluator = new CSharpScriptEvaluator() };

        var evaluation = engine.ScriptEvaluator!.Evaluate(Request(ValidScript));
        var bound = ScriptHandlerAdapter.TryBind<CardEventView>(evaluation.Handler!);

        Assert.NotNull(bound);
    }
}
