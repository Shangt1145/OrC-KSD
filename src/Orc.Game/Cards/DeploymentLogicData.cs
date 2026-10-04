using Orc.Core;
using Orc.Game.Board;

namespace Orc.Game.Cards;

/// <summary>
/// 部署逻辑条目：名称（日志定位用）＋handler（可空——「handler 非空」检查的承载：空 handler 条目＝无效条目、触发时跳过）。
/// 本类型不持运行期状态。
/// </summary>
public sealed class DeploymentLogicEntry
{
    internal DeploymentLogicEntry(string name, Func<DeploymentLogicContext, CancellationToken, Task>? handler)
    {
        Name = name;
        Handler = handler;
    }

    /// <summary>条目名称（日志定位用）。</summary>
    public string Name { get; }

    /// <summary>部署逻辑 handler（null＝无效条目、触发时跳过）；接收被部署单位与目标槽位等上下文。</summary>
    public Func<DeploymentLogicContext, CancellationToken, Task>? Handler { get; }
}

/// <summary>
/// 部署逻辑组件（2B；部署词条效果的登记载体；每卡至多一份——经 <see cref="Orc.Cards.Card.AddData"/> 挂载）：
/// 条目按登记序维护（先登记先触发）；部署链消费＝默认检查「组件存在且存在 handler 非空的有效条目」→ 按序触发有效条目；
/// 无组件或无有效条目＝跳过词条效果（部署不因此失败、不阻断单位化与扣费）。
/// 独立于词条系统（不要求与 <see cref="KeywordData"/> 登记挂钩）；「部署效果＝组件生成逻辑＋被动主效果（加载时生成部署组件）」
/// 属加载/生成链（装配机制）——本批仅消费（按序触发已生成/登记的部署逻辑组件），不展开装配生成细节。
/// 纯数据＋登记，无触发行为（触发编排在单位卡的部署触发器默认链中）。
/// </summary>
public sealed class DeploymentLogicData
{
    private readonly List<DeploymentLogicEntry> _entries = new();

    /// <summary>登记条目（登记序只读快照）。</summary>
    public IReadOnlyList<DeploymentLogicEntry> Entries => _entries;

    /// <summary>登记一条部署逻辑（登记序保留——触发顺序断言依据）。返回本组件（链式登记）。</summary>
    /// <param name="name">条目名称（非 null/空白）。</param>
    /// <param name="handler">部署逻辑 handler（可空——空 handler 条目＝无效、触发时跳过）。</param>
    /// <exception cref="ArgumentException">name 为 null/空白。</exception>
    public DeploymentLogicData Add(string name, Func<DeploymentLogicContext, CancellationToken, Task>? handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _entries.Add(new DeploymentLogicEntry(name, handler));
        return this;
    }
}

/// <summary>
/// 部署逻辑触发上下文（部署词条效果被触发时的可见信息）：
/// 被部署单位（<see cref="Unit"/>）＋本次部署的目标槽位（<see cref="Target"/>）＋引擎引用（<see cref="Engine"/>，写效果所需）。
/// 由部署链在触发每个有效条目时构造（每次触发独立实例）。
/// </summary>
public sealed class DeploymentLogicContext
{
    internal DeploymentLogicContext(UnitCard unit, Slot target, LogicEngine engine)
    {
        Unit = unit;
        Target = target;
        Engine = engine;
    }

    /// <summary>被部署的单位卡（本次触发所属）。</summary>
    public UnitCard Unit { get; }

    /// <summary>本次部署的目标槽位。</summary>
    public Slot Target { get; }

    /// <summary>引擎引用（写效果所需：发射更新 / 触发子触发器）。</summary>
    public LogicEngine Engine { get; }
}
