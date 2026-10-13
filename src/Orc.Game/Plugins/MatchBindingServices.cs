using Orc.Core;
using Orc.Game.Cards;
using Orc.Game.Triggers;

namespace Orc.Game.Plugins;

/// <summary>
/// 对局绑定面实现（游戏层内部件；P11 甲·postCommand 相）：把原 <c>Match.Initialize</c> 的内置装配步骤
/// （流程触发器登记／词条效果库装配）交由插件声明执行——语义与迁移前逐字一致。
/// </summary>
internal sealed class MatchBindingServices : IMatchBindingServices
{
    private readonly Func<Commanding.CommandManager?> _commandManager;
    private readonly TriggerRegistry _triggerRegistry;
    private readonly LogicEngine _engine;

    internal MatchBindingServices(
        Func<Commanding.CommandManager?> commandManager,
        TriggerRegistry triggerRegistry,
        LogicEngine engine)
    {
        _commandManager = commandManager;
        _triggerRegistry = triggerRegistry;
        _engine = engine;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">CommandManager 未就绪（装配性错误）。</exception>
    public void RegisterFlowTriggers(TriggerLayer layer)
    {
        var commands = _commandManager()
            ?? throw new InvalidOperationException("CommandManager 未就绪（流程触发器登记须在 postCommand 相执行）。");

        _triggerRegistry.Register(commands.CommandTrigger, layer);
        _triggerRegistry.Register(commands.UnitMoveTrigger, layer);
        _triggerRegistry.Register(commands.UnitAttackTrigger, layer);
        _triggerRegistry.Register(commands.AttackDamageTrigger, layer);
    }

    /// <inheritdoc />
    public void AssembleKeywordEffectLibrary() => KeywordEffectAssembly.Assemble(_engine);
}
