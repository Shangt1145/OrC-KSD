using Orc.Core;

namespace Orc.Tests;

/// <summary>
/// 判定器机制测试夹具（J1）：
/// ①统一形态判定器（直接继承基类；构造注入主逻辑）；
/// ②强类型适配形态判定器（子类声明强类型 delegate＋覆写主方法/适配面）；
/// ③测试用受控常量（判定器名——防散落字面；测试专用）。
/// </summary>
internal static class JudicatorTestData
{
    /// <summary>统一形态测试判定器名（受控常量；测试专用）。</summary>
    public const string ProbeName = "test.probe";

    /// <summary>强类型形态测试判定器名（受控常量；测试专用）。</summary>
    public const string PairName = "test.pair";
}

/// <summary>统一形态判定器（测试夹具）：直接继承基类；构造注入主逻辑（统一签名——拆包/组包由注入逻辑自理）。</summary>
internal sealed class PassThroughJudicator : Judicator
{
    private readonly Func<object[]?, object[]?> _logic;

    public PassThroughJudicator(Func<object[]?, object[]?> logic) => _logic = logic;

    public override object[]? Invoke(object[]? args) => _logic(args);
}

/// <summary>
/// 强类型适配形态判定器（测试夹具）：子类声明强类型 delegate（string tag, int count → bool）；
/// 覆写主方法（经适配面复用）＋实现适配面（拆包→强类型调用→组包；参数契约不符＝Unpack fail-fast）。
/// </summary>
internal sealed class PairJudicator : Judicator<PairJudicator.Rule>
{
    /// <summary>强类型 delegate（该判定器签名）。</summary>
    public delegate bool Rule(string tag, int count);

    private readonly Rule _rule;

    public PairJudicator(Rule rule) => _rule = rule;

    public override object[]? Invoke(object[]? args) => Adapt(_rule)(args);

    protected override Func<object[]?, object[]?> Adapt(Rule rule)
        => args => new object[] { rule(Unpack<string>(args, 0), Unpack<int>(args, 1)) };
}
